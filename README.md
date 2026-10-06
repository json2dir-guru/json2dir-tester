# json2dir-tester

A C# test runner for [json2dir](https://github.com/alurm/json2dir) implementations.
It builds each implementation from source, feeds it the same test cases and compares the directory trees it produces.

## Requirements

- Linux (the implementations are POSIX tools: symlinks and execute bits). On Windows the tester runs in WSL.
- .NET 10 SDK on the host; .NET 10 runtime inside WSL.
- Each implementation's own toolchain, installed into the workspace `runtimes/` directory rather than system-wide.

## Usage

```sh
./run.sh list                         # Linux
./run.sh build json2dir               # clone (if missing) and build
./run.sh run json2dir                 # run every case
./run.sh run --all --level core --json results.json
```

On Windows use `./run.ps1` with the same arguments: it builds the tester and runs it in WSL (`$env:J2D_WSL_DISTRO`, `Ubuntu` by default).

| Option | Meaning |
| --- | --- |
| `--suite NAME` | Only cases from `cases/NAME` (repeatable) |
| `--level NAME` | Only cases of this level, e.g. `core` (repeatable) |
| `--filter TEXT` | Only cases whose name contains `TEXT` |
| `--timeout SEC` | Time limit per case, default 10 |
| `--json FILE` | Also write results as JSON |
| `--verbose` | Print stderr of failing cases |
| `--sources DIR` | Where implementation sources live, default `<workspace>/others` |
| `--runtimes DIR` | Where toolchains live, default `<workspace>/runtimes` |

The workspace is the directory two levels above this repository (`json2dir_MANY/projects/json2dir-tester`).

## Implementations

Each implementation is a file in [`implementations/`](implementations):

```json
{
  "name": "json2dir",
  "description": "Reference implementation (Rust)",
  "repo": "https://github.com/alurm/json2dir",
  "build": "...",
  "command": "{src}/target/release/json2dir"
}
```

`build` and `command` are `/bin/sh` snippets. `{src}` is the source checkout (`<sources>/<name>`), `{runtimes}` the toolchain directory.
`command` runs inside the empty target directory with the document on stdin; `{input}` and `{output}` expand to the input file and the target directory for implementations that want them.

| Implementation | Toolchain |
| --- | --- |
| `json2dir` | Rust via rustup in `runtimes/rust-linux`: `RUSTUP_HOME=…/rust-linux/rustup CARGO_HOME=…/rust-linux/cargo sh rustup-init.sh -y --no-modify-path --profile minimal --default-toolchain none` (the repo pins nightly) |
| `json2dir-zig` | Zig 0.16.0 tarball unpacked to `runtimes/zig-0.16.0`. The build cache lives in `/tmp`: Zig's cache needs renames that `/mnt/c` does not support |
| `json2dir-scheme` | Guile 3.0.7 from Ubuntu jammy `.deb`s unpacked with `dpkg -x` into `runtimes/guile-3.0.7`, started through the `runtimes/guile-3.0.7/guile` wrapper |
| `json2dir-cs`, `json2dir-msbuild` | .NET 10 SDK (the one in WSL). Built with `UseAppHost=false`: Ubuntu's dotnet has no apphost for its distro RID |

## Test cases

| Suite | Cases | Source |
| --- | --- | --- |
| `conformance` | 68 | The awesome-json2dir conformance suite (CC0), unchanged |
| `upstream` | 289 | Converted from the test suites of json2dir, json2dir-zig, -scheme, -ats, -llvm-IR, -agda, -bimbo, -F-, -nix, -lean |
| `specific/<impl>` | 119 | Same sources, but only valid for one implementation |

See [cases/README.md](cases/README.md) for the format and its extensions.
Results are judged exactly like the conformance suite's `run.py`:

1. Success cases must exit with 0 and produce exactly the expected tree.
2. Error cases must exit non-zero; the tree left behind is not checked.
3. Every case fails if anything is created outside the target directory.
4. `accept_error` cases pass on either the expected tree or a non-zero exit.

## Results

| Implementation | conformance | upstream | specific |
| --- | --- | --- | --- |
| `json2dir` | 67/68 | 289/289 | 2/2 |

`json2dir` fails `conformance/overwrite/215-trailing-slash-does-not-follow-symlink`: the name `d/` writes through an existing symlink `d`.
