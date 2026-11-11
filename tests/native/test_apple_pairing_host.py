"""Non-advertising native helper checks. Set APPLE_PAIRING_HELPER to the built executable."""

import json
import os
import signal
import subprocess
import unittest


class ApplePairingHostTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.helper = os.environ.get("APPLE_PAIRING_HELPER")
        if not cls.helper or not os.path.isfile(cls.helper):
            raise RuntimeError("Set APPLE_PAIRING_HELPER to the built native helper.")

    def test_code_decoder(self):
        result = subprocess.run(
            [self.helper, "--self-test"], capture_output=True, text=True, timeout=5, check=True
        )
        self.assertEqual(json.loads(result.stdout), {"kind": "selfTestPassed"})
        self.assertEqual(result.stderr, "")

    def start_lifetime_check(self):
        process = subprocess.Popen(
            [self.helper, "--self-test-lifetime"],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
        )
        self.addCleanup(self.stop_if_running, process)
        self.assertEqual(json.loads(process.stdout.readline()), {"kind": "selfTestWaiting"})
        return process

    @staticmethod
    def stop_if_running(process):
        if process.poll() is None:
            process.kill()
            process.wait(timeout=5)
        for stream in (process.stdin, process.stdout, process.stderr):
            if stream is not None:
                stream.close()

    def assert_completion(self, process, expected):
        output, error = process.communicate(timeout=5)
        self.assertEqual(json.loads(output), {"kind": expected})
        self.assertEqual(error, "")
        self.assertEqual(process.returncode, 2)

    def test_parent_pipe_closure_stops_session(self):
        process = self.start_lifetime_check()
        process.stdin.close()
        process.stdin = None
        self.assert_completion(process, "cancelled")

    def test_sigterm_stops_session(self):
        process = self.start_lifetime_check()
        process.send_signal(signal.SIGTERM)
        # Keep stdin open until the native process has handled the signal.
        process.wait(timeout=5)
        self.assert_completion(process, "cancelled")

    def test_sigint_stops_session(self):
        process = self.start_lifetime_check()
        process.send_signal(signal.SIGINT)
        process.wait(timeout=5)
        self.assert_completion(process, "cancelled")

    def test_deadline_stops_session(self):
        process = self.start_lifetime_check()
        process.wait(timeout=5)
        self.assert_completion(process, "timedOut")

    def test_unconfirmed_advertisement_is_rejected(self):
        result = subprocess.run(
            [self.helper, "--advertise"], capture_output=True, text=True, timeout=5
        )
        self.assertEqual(result.returncode, 1)
        self.assertEqual(result.stdout, "")
        self.assertIn("requires Sherpa-owned pipes", result.stderr)

    def test_unbounded_advertisement_is_rejected_before_loading_framework(self):
        for timeout in ("0", "301", "-1", "garbage"):
            result = subprocess.run(
                [self.helper, "--advertise", "--confirm", "--timeout", timeout],
                capture_output=True, text=True, timeout=5,
            )
            self.assertEqual(result.returncode, 1)
            self.assertEqual(result.stdout, "")


if __name__ == "__main__":
    unittest.main()
