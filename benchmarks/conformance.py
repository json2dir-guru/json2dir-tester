#!/usr/bin/env python3
"""Run conformance independently against prepared packages, retaining completed reports."""

import argparse
import json
import math
import os
from pathlib import Path
import signal
import subprocess
import time

from bundle import write


def invocation(command, seconds):
    process = subprocess.Popen(command, stdin=subprocess.DEVNULL, start_new_session=True)
    try:
        return process.wait(timeout=seconds)
    except subprocess.TimeoutExpired:
        return None
    finally:
        # A successful parent exit does not imply that all of its children exited.
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        process.wait()


def run(campaign, output, timeout, budget, duration, tester):
    if any(not math.isfinite(value) or value <= 0 for value in (timeout, budget, duration)):
        raise ValueError("timeouts and budgets must be finite and positive")
    if output.exists():
        raise ValueError("use a new results directory")
    lock = campaign / "lock.json"
    names = [item["name"] for item in json.loads(lock.read_text())["implementations"]]
    summary = {"suite": "conformance", "timeout": timeout, "implementationBudget": budget,
               "maxDuration": duration, "completed": False, "results": [], "unstarted": names.copy()}
    path = output / "summary.json"
    write(path, summary)
    deadline = time.monotonic() + duration
    for index, name in enumerate(names):
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            break
        print(f"Conformance [{index + 1}/{len(names)}]: {name}", flush=True)
        report = output / f"{index:03d}.json"
        command = ["dotnet", str(tester), "run", name, "--lock", str(lock), "--suite", "conformance",
                   "--timeout", str(timeout), "--json", str(report)]
        code = invocation(command, min(budget, remaining))
        status = ("campaign-time-limit" if remaining <= budget else "implementation-time-limit") if code is None else {
            0: "passed", 1: "failed"}.get(code, "runner-error")
        summary["results"].append({"implementation": name, "status": status, "exitCode": code,
                                   "report": report.name if report.is_file() else None})
        summary["unstarted"].remove(name)
        write(path, summary)
        if status == "campaign-time-limit":
            break
    summary["completed"] = not summary["unstarted"] and all(
        r["status"] in {"passed", "failed"} for r in summary["results"])
    write(path, summary)
    if not summary["completed"]:
        return 2
    return 1 if any(r["status"] == "failed" for r in summary["results"]) else 0


def main():
    def terminate(_signal, _frame):
        raise SystemExit(2)

    signal.signal(signal.SIGTERM, terminate)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dir", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--timeout", type=float, default=30)
    parser.add_argument("--budget", type=float, default=2400)
    parser.add_argument("--max-duration", type=float, required=True)
    parser.add_argument("--tester", type=Path, default=Path("src/Json2dirTester/bin/Release/net10.0/json2dir-tester.dll"))
    args = parser.parse_args()
    return run(args.dir.resolve(), args.out.resolve(), args.timeout, args.budget, args.max_duration, args.tester.resolve())


if __name__ == "__main__":
    raise SystemExit(main())
