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

| Implementation | Toolchain setup |
| --- | --- |
| `json2dir` | rustup into `runtimes/rust-linux`: `RUSTUP_HOME=…/rust-linux/rustup CARGO_HOME=…/rust-linux/cargo sh rustup-init.sh -y --no-modify-path --profile minimal --default-toolchain none` |

## Test cases

[`cases/conformance`](cases/conformance) is the [conformance suite](https://github.com/kitsunoff/awesome-json2dir/tree/main/conformance) from awesome-json2dir (CC0), commit `eed169d`.
The tester judges results exactly like its `run.py`:

1. Success cases must exit with 0 and produce exactly the expected tree.
2. Error cases must exit non-zero; the tree left behind is not checked.
3. Every case fails if anything is created outside the target directory.
4. `accept_error` cases pass on either the expected tree or a non-zero exit.

## Results

| Implementation | core | overwrite |
| --- | --- | --- |
| `json2dir` | 52/52 | 15/16 — `215-trailing-slash-does-not-follow-symlink`: the name `d/` writes through an existing symlink `d` |
