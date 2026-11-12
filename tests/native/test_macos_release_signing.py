"""Exercise the actual macOS release signing steps with simulated Apple tools."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import textwrap
import unittest


WORKFLOW = Path(__file__).resolve().parents[2] / ".github/workflows/build.yml"


def workflow_script(name):
    marker = f"      - name: {name}\n"
    section = WORKFLOW.read_text(encoding="utf-8").split(marker, 1)[1]
    section = section.split("      - name: ", 1)[0]
    return textwrap.dedent(section.split("        run: |\n", 1)[1])


class MacOSReleaseSigningTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.app = self.root / "artifacts/macos/MAUI Sherpa.app"
        self.helper = self.app / "Contents/MacOS/sherpa-apple-pairing"
        self.helper.parent.mkdir(parents=True)
        self.helper.touch()
        copilot = self.app / "Contents/MonoBundle/runtimes/osx-arm64/native/copilot"
        copilot.parent.mkdir(parents=True)
        copilot.touch()

        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.log = self.root / "codesign.jsonl"
        self.write_tool("codesign", """
            #!/usr/bin/env python3
            import json, os, sys

            args = sys.argv[1:]
            with open(os.environ["CODESIGN_LOG"], "a", encoding="utf-8") as log:
                log.write(json.dumps(args) + "\\n")
            target = args[-1]
            is_helper = target.endswith("/sherpa-apple-pairing")
            if "--verify" in args:
                sys.exit(1 if os.environ.get("INVALID_SIGNATURE") == "1" else 0)
            if "--entitlements" in args:
                print("com.apple.security.cs.allow-jit")
                print("com.apple.security.cs.allow-unsigned-executable-memory")
                print("com.apple.security.cs.disable-library-validation")
            elif "-dvv" in args:
                if not is_helper:
                    print("CodeDirectory flags=0x10000(runtime)")
                else:
                    arch = args[args.index("--arch") + 1]
                    failure = os.environ.get("BAD_HELPER") if arch == os.environ.get("BAD_HELPER_ARCH") else ""
                    print("CodeDirectory flags=0x0(none)" if failure == "runtime"
                          else "CodeDirectory flags=0x10000(runtime)")
                    if failure != "identity":
                        print("Authority=Developer ID Application: Test")
                    if failure != "timestamp":
                        print("Timestamp=Nov 11, 2026")
            elif is_helper and os.environ.get("FAIL_HELPER_SIGN") == "1":
                sys.exit(1)
        """)
        self.write_tool("lipo", """
            #!/usr/bin/env python3
            import os, sys

            args = sys.argv[1:]
            if (len(args) != 3 or not args[0].endswith("/sherpa-apple-pairing")
                    or args[1] != "-verify_arch" or args[2] not in ("arm64", "x86_64")):
                sys.exit(2)
            sys.exit(1 if args[2] == os.environ.get("MISSING_ARCH") else 0)
        """)
        self.env = os.environ.copy()
        for key in ("BAD_HELPER", "BAD_HELPER_ARCH", "FAIL_HELPER_SIGN",
                    "INVALID_SIGNATURE", "MISSING_ARCH"):
            self.env.pop(key, None)
        self.env.update({
            "APPLE_CODESIGN_IDENTITY": "Developer ID Application: Test",
            "CODESIGN_LOG": str(self.log),
            "PATH": f"{self.bin}{os.pathsep}{self.env['PATH']}",
        })

    def write_tool(self, name, contents):
        tool = self.bin / name
        tool.write_text(textwrap.dedent(contents).lstrip(), encoding="utf-8")
        tool.chmod(0o755)

    def run_step(self, name, **overrides):
        return subprocess.run(
            ["bash", "-e", "-o", "pipefail", "-c", workflow_script(name)],
            cwd=self.root,
            env={**self.env, **overrides},
            capture_output=True,
            text=True,
        )

    def calls(self):
        if not self.log.exists():
            return []
        return [json.loads(line) for line in self.log.read_text(encoding="utf-8").splitlines()]

    def test_signs_pairing_helper_before_outer_app(self):
        result = self.run_step("Re-sign with Hardened Runtime")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        calls = self.calls()
        helper = next(i for i, args in enumerate(calls) if args[-1].endswith("/sherpa-apple-pairing"))
        app = next(i for i, args in enumerate(calls) if args[-1].endswith("/MAUI Sherpa.app"))
        self.assertLess(helper, app)
        self.assertEqual(calls[helper][:-1], [
            "--force", "--options", "runtime", "--timestamp",
            "--sign", "Developer ID Application: Test",
        ])
        copilot = next(args for args in calls if args[-1].endswith("/copilot"))
        self.assertIn("--preserve-metadata=entitlements", copilot)

    def test_missing_or_failed_helper_signing_stops_before_outer_app(self):
        for missing in (True, False):
            with self.subTest(missing=missing):
                if missing:
                    self.helper.unlink()
                else:
                    self.helper.touch()
                self.log.unlink(missing_ok=True)
                overrides = {} if missing else {"FAIL_HELPER_SIGN": "1"}
                result = self.run_step("Re-sign with Hardened Runtime", **overrides)
                self.assertNotEqual(result.returncode, 0)
                if missing:
                    self.assertIn("Apple pairing helper is missing", result.stdout)
                self.assertFalse(any(args[-1].endswith("/MAUI Sherpa.app") for args in self.calls()))

    def test_verifies_each_helper_architecture(self):
        result = self.run_step("Verify code signature")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        calls = self.calls()
        for arch in ("arm64", "x86_64"):
            self.assertTrue(any(
                "-dvv" in args and "--arch" in args and args[args.index("--arch") + 1] == arch
                and args[-1].endswith("/sherpa-apple-pairing")
                for args in calls
            ))
        self.assertTrue(any(
            "--verify" in args and "--all-architectures" in args and "--strict" in args
            and args[-1].endswith("/sherpa-apple-pairing")
            for args in calls
        ))

    @unittest.skipUnless(sys.platform == "darwin", "requires macOS developer tools")
    def test_verifies_architectures_with_real_lipo(self):
        objects = []
        for arch in ("arm64", "x86_64"):
            obj = self.root / f"{arch}.o"
            subprocess.run(
                ["clang", "-arch", arch, "-x", "c", "-c", "-", "-o", str(obj)],
                input="int main(void) { return 0; }\n",
                text=True,
                check=True,
            )
            objects.append(obj)
        subprocess.run(
            ["lipo", "-create", *(str(obj) for obj in objects), "-output", str(self.helper)],
            check=True,
        )
        (self.bin / "lipo").unlink()

        result = self.run_step("Verify code signature")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

        shutil.copyfile(objects[0], self.helper)
        result = self.run_step("Verify code signature")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("missing the x86_64 architecture", result.stdout)

    def test_rejects_bad_helper_signature_on_either_architecture(self):
        messages = {
            "identity": "not signed with a Developer ID certificate",
            "timestamp": "has no secure timestamp",
            "runtime": "does not have Hardened Runtime enabled",
        }
        for arch in ("arm64", "x86_64"):
            for failure, message in messages.items():
                with self.subTest(arch=arch, failure=failure):
                    result = self.run_step(
                        "Verify code signature", BAD_HELPER=failure, BAD_HELPER_ARCH=arch
                    )
                    self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
                    self.assertIn(arch, result.stdout)
                    self.assertIn(message, result.stdout)

    def test_rejects_missing_architecture_and_invalid_signature(self):
        for overrides in (
            {"MISSING_ARCH": "arm64"}, {"MISSING_ARCH": "x86_64"}, {"INVALID_SIGNATURE": "1"}
        ):
            with self.subTest(**overrides):
                result = self.run_step("Verify code signature", **overrides)
                self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
                if "MISSING_ARCH" in overrides:
                    self.assertIn(f"missing the {overrides['MISSING_ARCH']} architecture", result.stdout)

    def test_rejects_missing_helper_during_verification(self):
        self.helper.unlink()
        result = self.run_step("Verify code signature")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Apple pairing helper is missing", result.stdout)


if __name__ == "__main__":
    unittest.main()
