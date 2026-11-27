"""Exercise startup validation against real child processes."""

import contextlib
import importlib.util
import io
import os
from pathlib import Path
import tempfile
import textwrap
import unittest


SCRIPT = Path(__file__).resolve().parents[2] / "build/verify_macos_launch.py"
SPEC = importlib.util.spec_from_file_location("verify_macos_launch", SCRIPT)
LAUNCH = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(LAUNCH)


class MacOSReleaseStartupTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.app = Path(directory.name) / "MAUI Sherpa.app"
        self.executable = self.app / "Contents/MacOS/MauiSherpa.MacOS"
        self.executable.parent.mkdir(parents=True)

    def write_executable(self, source):
        self.executable.write_text(
            "#!/usr/bin/env python3\n" + textwrap.dedent(source), encoding="utf-8",
        )
        self.executable.chmod(0o755)

    def test_rejects_missing_executable(self):
        with contextlib.redirect_stderr(io.StringIO()) as errors:
            result = LAUNCH.verify_launch(self.app, startup_seconds=0.2)
        self.assertEqual(result, 1)
        self.assertIn("executable is missing", errors.getvalue())

    def test_reports_startup_failure_and_rejects_early_clean_exit(self):
        for exit_code in (134, 0):
            with self.subTest(exit_code=exit_code):
                self.write_executable(f"""
                    import sys
                    print("runtime initialization failed", file=sys.stderr)
                    sys.exit({exit_code})
                """)
                with contextlib.redirect_stderr(io.StringIO()) as errors:
                    result = LAUNCH.verify_launch(self.app, startup_seconds=1)
                self.assertEqual(result, 1)
                self.assertIn(f"exit code {exit_code}", errors.getvalue())
                self.assertIn("runtime initialization failed", errors.getvalue())

    def test_isolates_app_data_and_stops_its_own_process_after_success(self):
        pid_file = self.app / "pid"
        self.write_executable(f"""
            import os
            import time
            from pathlib import Path
            assert os.environ["MAUI_SHERPA_SCREENSHOT_MODE"] == "1"
            Path({str(pid_file)!r}).write_text(str(os.getpid()))
            time.sleep(30)
        """)
        with contextlib.redirect_stdout(io.StringIO()):
            result = LAUNCH.verify_launch(self.app, startup_seconds=0.5)
        self.assertEqual(result, 0)
        pid = int(pid_file.read_text())
        with self.assertRaises(ProcessLookupError):
            os.kill(pid, 0)


if __name__ == "__main__":
    unittest.main()
