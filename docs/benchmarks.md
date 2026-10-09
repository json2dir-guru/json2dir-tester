# Benchmarks

Benchmarks compare complete Linux CLI invocations: startup, JSON parsing,
validation and filesystem work. They use implementation manifests, but a separate
process runner and result format. Existing conformance commands are unchanged.

## Run and publish

With Python 3.11+ and Nix (`nix-command` and `flakes` enabled), configure the
public binary cache in `nix.conf`:

```conf
extra-substituters = https://json2dirpkgs.cachix.org
extra-trusted-public-keys = json2dirpkgs.cachix.org-1:65NBBfjvYOT+/ebY7XFg/XXSWTTsrCvGJuEhEVcRSGM=
```

Prepare packaged executables and run a campaign:

```sh
python3 benchmarks/prepare_packages.py --dir .bench/campaign
export PATH="$PWD/.bench/campaign/work/runtimes/tools/bin:$PATH"
./run.sh bench --all --dir .bench/campaign
./run.sh bench-export --dir .bench/campaign --out .bench/export
```

Packaged preparation selects all 160 implementations in the pinned
[json2dirpkgs](https://github.com/monadix/json2dirpkgs) revision. Supply package
names for a subset. It downloads executables and their dependencies with local
builds disabled, and fails visibly if the publishing workflow has not populated
the cache. No Cachix token is needed. Commands use the packaged wrappers, which
include runtime dependencies and may differ from upstream build flags; comparisons
describe these Nix packages. The Rust reference is included in this package selection.

The campaign lock records the immutable package-set URL and NAR hash, each
implementation's source pin, executable store path, the pinned Nixpkgs, benchmark
tool versions and the complete realised runtime closure. GC roots keep downloaded
outputs available throughout the campaign. Package preparation and downloads
finish before measurements; cached implementations are not rebuilt by the runner.

The original source-build mode remains available through `benchmarks/prepare.py`.
With Git, it selects Rust, Zig, Python, JavaScript, C#, Nushell, Scheme and Nix by
default. Supply implementation names to prepare a different selection. It reuses manifest
commands, replacing the initial eight implementations' workstation-specific
runtime paths with the campaign's Nix toolchain environment. Other adapters may
need additional runtime provisioning; unsupported builds are not rankings.

Source preparation resolves current source commits and nixpkgs, then writes
`lock.json`. This is a generated campaign lock, not a claim that upstream HEADs
are stable. Nix verifies source/package hashes; the lock retains its revision,
source NAR hash, realised store closure, actual tool versions and a dated Rust
toolchain with its verified manifest checksum. Preparation and all builds finish
before measurements. Reproduce the software selection in a **new** directory:

```sh
python3 benchmarks/prepare.py --lock .bench/campaign/lock.json --dir .bench/reproduction
```

The benchmark command requires a prepared lock. For source builds it requires
clean sources at the recorded commits; packaged campaigns require the locked
executables in the Nix store. It prepares all implementations and annotates their existing
conformance results before timing. Unrelated conformance failures do not exclude
correct workloads. Build failures are visible. Conformance inspection temporarily
adds owner read permission to mode-000 files and restores the original mode.

The manually triggered **Benchmarks** Actions workflow downloads the packaged
selection from Cachix and runs sequentially on one `ubuntu-24.04` VM. It uses no
upload secret. Its suite input defaults to the eight-workload standard suite;
select extended for all 26 workloads. An optional workload filter applies within
the selected suite. The default allowance is 240 seconds per implementation,
workload and storage pair; each invocation has a 30-second deadline. Both limits
are configurable at dispatch. Downloads, preparation and conformance count toward
an overall deadline 345 minutes after the first job step, reserving 15 minutes
within the six-hour job limit for cleanup, export and upload. Reaching the deadline
cancels the active invocation, retains completed pairs, and leaves interrupted or
unstarted pairs unranked. Individual invocation and pair limits are unchanged.
Export and upload run as separate steps even when the campaign stops or fails.
Download its artifact, export if necessary, and replace Awesome's `results/benchmarks.json`
and `results/benchmark-samples.json` together through a PR. Its existing Pages
workflow publishes the data. No cross-repository credentials are needed. Older
campaigns remain Actions artifacts for 90 days; the website shows the latest one.

## Incremental runs and complete downloads

Set the workflow's `baseline_run` input to an earlier **Benchmarks** run ID from
the same repository. The workflow downloads its artifact, benchmarks added or
changed implementations and retries unfinished entries. Leave the input empty
for a fresh full campaign. Expired or missing baseline artifacts fail visibly.

Reuse requires matching executable store paths, source revisions, benchmark tool
versions and pinned Nixpkgs identity, harness and conformance-case hashes, suite/filter and timing options.
A package-set revision change alone does not invalidate unchanged executables.
Resource/setup failures are retried; completed timeout and incorrect-output
results remain valid recorded outcomes. A deadline-interrupted rerun retains
compatible completed pairs from the baseline. Package preparation still downloads
the complete executable selection before timing.

The latest artifact contains `bundle/index.json` with the combined results and
their originating campaign IDs. Under `bundle/campaigns/<id>/`, each campaign
keeps its original export, raw samples, lock, date and runner environment. Removed
or changed package versions are excluded from the current combined results;
campaigns still referenced by the index are carried forward. When nothing changed,
the workflow uploads a complete bundle without running measurements.

Download that one artifact from the Actions run page or with:

```sh
gh run download RUN_ID --repo json2dir-guru/json2dir-tester --dir bench-results/RUN_ID
```

The bundle may contain measurements from different machines and dates; it is not
a single campaign suitable for cross-run rankings. The existing Awesome export
remains a separate campaign. Older artifacts without bundle fingerprints can be
read, but require a fresh run before incremental reuse is possible. Results do
not need to be committed to this repository; each new artifact carries the
referenced previous measurements forward beyond their original artifact's expiry.

## Workloads and metrics

The standard suite runs `empty`, `files-1000`, `payload-8MiB`, `balanced-1000`,
`depth-64`, `escapes-1MiB`, `config` and `update`: eight workloads, or sixteen pairs
with both disk and tmpfs. `--suite extended` retains all 26 workloads below
(52 pairs with both storages). For example, `--suite extended --filter payload-64MiB`
selects the largest payload workload.

| Family | Sizes | Purpose |
| --- | --- | --- |
| Startup | Empty object; one small file | Runtime and command startup |
| Flat files | 100, 1,000, 10,000 × 64 bytes | Metadata operations and entry throughput |
| Payload | 1, 8, 64 MiB in one file | Parsing, copying and buffered writing |
| Balanced tree | 1,000, 10,000 files, branching factor 10 | Directory traversal and breadth |
| Deep tree | 16, 64, 256 directory levels | Depth limits and traversal overhead |
| Directories, links, scripts | 100, 1,000 each | Entry-specific operations |
| UTF-8 versus escapes | Equal decoded payloads of 1, 8 MiB | String decoding costs |
| Configuration | 500 files, 50 links, 50 scripts in ten directories | Mixed workload |
| Reapply/update | The configuration tree with existing content | Merge, truncation and type replacement |

Update preparation is repeated before every invocation. It includes an unlisted
file, directories with mode 0750, file/script/link transitions and a symlink
whose target is a sentinel outside the destination. Verification checks retained
content and permissions as well as replacements. The unlisted file contributes
no processed-entry or payload throughput. Actual counts, depths, byte sizes and
SHA-256 input hashes are exported; directory overhead is additional to the file
counts in the balanced family.

Defaults are three discarded warmups, fifteen latency rounds, and three separate
GNU `time` resource rounds per workload/storage pair. Every invocation is a fresh
process. Each round shuffles implementation order with the recorded seed, and
only one command runs at a time. Both streams are drained with bounded retained
logs; asynchronous stdin feeding shares the deadline with process execution.
Timed wrappers (`setsid` and `/bin/sh`) are identical for all implementations;
their overhead is included, with no subtraction from small startup timings.
Commands use `C.UTF-8`; runtime caches live inside the campaign workspace.

Disk and `/dev/shm` tmpfs results are separate. The latter is checked with
`findmnt`; unavailable storage is explicit. Cache state is warm, and disk timing
ends at command exit without `fsync`. These measure buffered CLI completion,
not durable writes or cold-cache performance. Tmpfs results include allocation
and filesystem metadata costs. Paths and storage capacity can impose limits;
errors are retained rather than counted as fast completions.

Latency summaries use the median, linearly interpolated Q1/Q3, minimum and maximum.
Entries/second uses processed entry count; output MiB/second uses decoded file and
script payload bytes. Separate profiling invocations report median user/system
CPU seconds and **maximum process RSS in KiB**. GNU `time` child accounting records
the largest process, not summed or simultaneous peak process-tree memory. Its
wrapper is excluded from latency rounds. There is no combined score or automatic
historical regression verdict; ratios use a reference from the same campaign.

## Correctness, limits and data

Every warmup, timing and resource output is inspected after command execution.
An iterative walker checks exact paths, streamed contents, symlink targets,
execute bits and preserved update permissions without traversing symlinks. It
rejects special files before opening them. The outside sentinel must remain a
regular file with its original contents. This is an output check, not a security
sandbox against deliberately hostile implementations.

The invocation deadline defaults to 30 seconds. Each implementation receives a
fresh 240-second budget for every workload/storage pair, including target
preparation, verification and cleanup; fixture generation, provisioning,
compilation and conformance annotation are excluded. Preparation or verification
can finish slightly past the budget; the next invocation in that pair will not
start. A command timeout skips larger sizes within that family and storage
condition. A deadline shortened by the remaining pair budget is instead reported
as budget exhaustion and does not suppress later workloads or storage conditions.
Slow pairs can still exhaust these limits and remain incomplete.
Budget exhaustion, wrong output, leaked descendants, missing
resources and command failures remain explicit. A process group is terminated
on timeout or descendant leaks; implementations that deliberately escape their
session are unsupported. Directory permissions are repaired during cleanup
without following symlinks.

`--suite standard|extended`, `--filter`, `--storage disk|tmpfs|both`,
`--repetitions`, `--warmups`, `--profiles`, `--timeout`, `--budget`, `--max-duration` and `--seed`
override campaign defaults. Results never
append to an existing campaign. Options record the suite and
`budgetScope: "implementation-workload-storage"`; exports of older campaigns keep
these fields null when absent, retaining their original campaign-wide budget.
Result `budgetUsedSeconds` records charged seconds for that pair, including
preparation, verification and cleanup; implementation `budgetUsedSeconds` sums
all pairs. Export preserves this telemetry separately from recomputed latency
samples; older results without it retain null. `campaign.json` and `samples.json`
are checkpointed after each storage pair and approximately every ten seconds
between invocations. `--max-duration SEC` bounds the benchmark command's
preparation, conformance and timing phases together; local runs have no overall
deadline unless it is supplied. Stopped campaigns record `stopReason` and remain
`completed: false`; completed pairs remain usable when exported. The workflow
passes the time remaining after setup and downloads, with an external timeout as
a guard against blocked cleanup. The version-1 export contains summaries,
workload metadata,
source/build/toolchain provenance and environment information, plus raw samples
in a separate file linked by campaign ID. Export validates samples and recomputes
statistics; interrupted campaigns retain incomplete cells, which receive no
throughput or comparison ratios. Missing measurements are null, not zero.

## Checks

```sh
python3 -m unittest discover -s benchmarks -v
dotnet build src/Json2dirTester -c Release
dotnet run --project tests/Benchmarks -c Release
```

The executable test project uses no third-party test packages. It covers fixture
determinism, equal decoded sizes, output failures, preserved modes, bounded logs,
blocked stdin, child cleanup, statistics, resource measurements and export.
