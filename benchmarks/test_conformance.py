import json
import shlex
import time
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import conformance


class ConformanceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.campaign = self.root / "packages"
        self.campaign.mkdir()
        (self.campaign / "lock.json").write_text(json.dumps({
            "implementations": [{"name": "a"}, {"name": "b"}]}))
        self.output = self.root / "results"

    def run_checks(self, duration=100):
        return conformance.run(self.campaign, self.output, 30, 40, duration, Path("tester.dll"))

    def summary(self):
        return json.loads((self.output / "summary.json").read_text())

    def test_failures_do_not_stop_later_implementations_and_commands_use_lock(self):
        def invoke(command, seconds):
            Path(command[-1]).write_text('{}')
            return 1 if command[3] == "a" else 0

        with patch.object(conformance, "invocation", side_effect=invoke) as execute:
            self.assertEqual(self.run_checks(), 1)
        commands = [call.args[0] for call in execute.call_args_list]
        self.assertTrue(all("--lock" in command and "conformance" in command for command in commands))
        summary = self.summary()
        self.assertTrue(summary["completed"])
        self.assertEqual([r["status"] for r in summary["results"]], ["failed", "passed"])
        self.assertEqual(summary["unstarted"], [])

    def test_implementation_limit_continues_and_global_limit_preserves_unstarted(self):
        with patch.object(conformance, "invocation", return_value=None):
            self.assertEqual(self.run_checks(), 2)
        self.assertEqual([r["status"] for r in self.summary()["results"]],
                         ["implementation-time-limit", "implementation-time-limit"])
        self.output = self.root / "short-results"
        with patch.object(conformance, "invocation", return_value=None) as execute:
            self.assertEqual(self.run_checks(duration=1), 2)
        self.assertEqual(execute.call_count, 1)
        summary = self.summary()
        self.assertEqual(summary["results"][0]["status"], "campaign-time-limit")
        self.assertIsNone(summary["results"][0]["report"])
        self.assertEqual(summary["unstarted"], ["b"])

    def test_runner_errors_are_not_conformance_failures(self):
        with patch.object(conformance, "invocation", return_value=2):
            self.assertEqual(self.run_checks(), 2)
        self.assertEqual({r["status"] for r in self.summary()["results"]}, {"runner-error"})

    def test_real_descendants_are_terminated_after_exit_and_timeout(self):
        for wait in (False, True):
            pid_file = self.root / f"child-{wait}.pid"
            command = "sleep 30 </dev/null >/dev/null 2>&1 & echo $! > " + shlex.quote(str(pid_file))
            if wait:
                command += "; wait"
            code = conformance.invocation(["/bin/sh", "-c", command], 0.2)
            self.assertEqual(code, None if wait else 0)
            pid = pid_file.read_text().strip()
            stat = Path("/proc") / pid / "stat"
            for _ in range(100):
                if not stat.exists() or stat.read_text().split()[2] == "Z":
                    break
                time.sleep(0.01)
            self.assertTrue(not stat.exists() or stat.read_text().split()[2] == "Z")

    def test_invalid_limits_fail_before_launch(self):
        with patch.object(conformance, "invocation") as execute:
            for duration in (0, -1, float("nan"), float("inf")):
                with self.assertRaises(ValueError):
                    self.run_checks(duration)
        execute.assert_not_called()


if __name__ == "__main__":
    unittest.main()
