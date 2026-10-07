# Benchmarks

Benchmarks compare complete Linux CLI invocations: startup, JSON parsing,
validation and filesystem work. They use implementation manifests, but a separate
process runner and result format. Existing conformance commands are unchanged.

## Run and publish

With Git, Python 3.11+ and Nix (`nix-command` and `flakes` enabled):

```sh
python3 benchmarks/prepare.py --dir .bench/campaign
export PATH="$PWD/.bench/campaign/work/runtimes/tools/bin:$PATH"
./run.sh bench --all --dir .bench/campaign
./run.sh bench-export --dir .bench/campaign --out .bench/export
```

Preparation selects Rust, Zig, Python, JavaScript, C#, Nushell, Scheme and Nix.
Supply implementation names to prepare a different selection. It reuses manifest
commands, replacing the initial eight implementations' workstation-specific
runtime paths with the campaign's Nix toolchain environment. Other adapters may
need additional runtime provisioning; unsupported builds are not rankings.

The first preparation resolves current source commits and nixpkgs, then writes
`lock.json`. This is a generated campaign lock, not a claim that upstream HEADs
are stable. Nix verifies source/package hashes; the lock retains its revision,
source NAR hash, realised store closure, actual tool versions and a dated Rust
toolchain with its verified manifest checksum. Preparation and all builds finish
before measurements. Reproduce the software selection in a **new** directory:

```sh
python3 benchmarks/prepare.py --lock .bench/campaign/lock.json --dir .bench/reproduction
```

The benchmark command requires a prepared lock and clean sources at its recorded
commits. It builds all selected implementations and annotates their existing
conformance results before timing. Unrelated conformance failures do not exclude
correct workloads. Build failures are visible. Conformance inspection temporarily
adds owner read permission to mode-000 files and restores the original mode.

The manually triggered **Benchmarks** Actions workflow runs this pipeline on one
`ubuntu-24.04` VM. An optional workload filter permits shorter campaigns. Download
its artifact, export if necessary, and replace Awesome's `results/benchmarks.json`
and `results/benchmark-samples.json` together through a PR. Its existing Pages
workflow publishes the data. No cross-repository credentials are needed. Older
campaigns remain Actions artifacts for 90 days; the website shows the latest one.

## Workloads and metrics

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

The invocation deadline defaults to 30 seconds. The per-implementation benchmark
budget defaults to ten minutes, including target preparation, verification and
cleanup; provisioning, compilation and conformance annotation are excluded.
Preparation or verification can finish slightly past the budget; the next
invocation will not start. A timeout skips larger sizes within that family and
storage condition. Budget exhaustion, wrong output, leaked descendants, missing
resources and command failures remain explicit. A process group is terminated
on timeout or descendant leaks; implementations that deliberately escape their
session are unsupported. Directory permissions are repaired during cleanup
without following symlinks.

`--filter`, `--storage disk|tmpfs|both`, `--repetitions`, `--warmups`, `--profiles`,
`--timeout`, `--budget` and `--seed` override campaign defaults. Results never
append to an existing campaign. `campaign.json` and `samples.json` are checkpointed
between workloads. The version-1 export contains summaries, workload metadata,
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
