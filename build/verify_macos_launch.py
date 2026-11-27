"""Reject macOS release bundles that exit during startup."""

import argparse
import os
from pathlib import Path
import subprocess
import sys
import tempfile


def verify_launch(app, architecture=None, startup_seconds=15):
    executable = Path(app) / "Contents/MacOS/MauiSherpa.MacOS"
    if not executable.is_file():
        print(f"macOS app executable is missing: {executable}", file=sys.stderr)
        return 1

    command = [str(executable.resolve())]
    if architecture:
        command = ["/usr/bin/arch", f"-{architecture}", *command]

    environment = {**os.environ, "MAUI_SHERPA_SCREENSHOT_MODE": "1"}
    with tempfile.TemporaryFile(mode="w+") as output:
        process = subprocess.Popen(
            command, env=environment, stdin=subprocess.DEVNULL,
            stdout=output, stderr=subprocess.STDOUT,
        )
        try:
            try:
                exit_code = process.wait(timeout=startup_seconds)
            except subprocess.TimeoutExpired:
                print(f"macOS app remained running for {startup_seconds} seconds.")
                return 0

            output.seek(0)
            print(f"macOS app exited during startup (exit code {exit_code}).", file=sys.stderr)
            print("".join(output.readlines()[-30:]), file=sys.stderr)
            return 1
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("app", type=Path)
    parser.add_argument("--architecture", choices=("arm64", "x86_64"))
    arguments = parser.parse_args()
    return verify_launch(arguments.app, arguments.architecture)


if __name__ == "__main__":
    sys.exit(main())
