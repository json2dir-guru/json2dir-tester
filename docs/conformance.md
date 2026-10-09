# Packaged conformance checks

The manually dispatched **Conformance** workflow runs the 68 cases from
`cases/conformance` against the same pinned Cachix packages used by benchmarks.
It is independent of the **Benchmarks** workflow and does not gate timings.
Leave `implementations` empty for all 160 packages, or enter space-separated
package names. Preparation downloads cached executables with builds disabled.

Defaults are 30 seconds per case and 2,400 seconds per implementation, including
verification and cleanup. The workflow stops checks 345 minutes after its first
step, leaving time to upload reports within GitHub's six-hour job limit. Failed
implementations do not stop later implementations; an implementation exceeding
its budget is terminated and recorded separately from conformance failures.

Download `json2dir-conformance-RUN_ID` from the Actions run. The artifact contains
`packages/lock.json` with package/source/tool provenance, numbered JSON reports
under `results/`, and `results/summary.json` mapping reports to implementations.
Completed reports are saved immediately. A terminated implementation may have no
report; the summary records the limit reached and lists unstarted implementations.
Failures give the job a failing status while reports still upload.

To check a prepared package selection locally:

```sh
python3 benchmarks/prepare_packages.py --dir .conformance/packages json2dir-python
export PATH="$PWD/.conformance/packages/work/runtimes/tools/bin:$PATH"
dotnet build src/Json2dirTester -c Release
python3 benchmarks/conformance.py --dir .conformance/packages \
  --out .conformance/results --max-duration 3600
```

The existing `run` command also accepts a packaged lock directly:

```sh
./run.sh run --all --lock .conformance/packages/lock.json \
  --suite conformance --timeout 30 --json conformance.json
```

Omit `--suite conformance` to run all applicable tester suites. Source-based
`run` commands keep their existing behavior. Benchmark exports retain an empty
`conformance` array for format compatibility and explicitly record that the
suite was not run. Older campaign exports retain their original annotations.
