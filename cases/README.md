# Test cases

| Suite | Cases | What it is |
| --- | --- | --- |
| [`conformance/`](conformance) | 68 | The awesome-json2dir [conformance suite](https://github.com/kitsunoff/awesome-json2dir/tree/main/conformance) (CC0), commit `eed169d`, unchanged |
| [`upstream/`](upstream) | 289 | Cases converted from the implementations' own test suites. Black-box and run against every implementation |
| [`specific/<impl>/`](specific) | 119 | Cases for one implementation only (`"only"`): its extra flags, or behavior the RFC leaves open that it pins down |

Every `upstream` and `specific` case has a `source` field naming the test it came from.
How each source test was handled (converted, duplicate, skipped and why) is recorded in
[`docs/extraction/`](../docs/extraction). Those notes use the numbering from before the merge, so
search the cases by `source`, not by file name.

## Format

The [conformance format](conformance/README.md#case-format), plus these optional fields:

| Field | Meaning |
| --- | --- |
| `source` | Where the case came from, e.g. `json2dir-scheme tests/test_cli.py::test_dangling_link` |
| `only` | Run only for these implementations (names from `implementations/*.json`) |
| `args` | Extra command-line arguments, appended to the implementation's command |
| `umask` | Umask for the run, octal string; default `022` |
| `requires_non_root` | Permissions matter. When the tester runs as root, the command runs as `nobody` via `setpriv` |
| `expect.stderr_contains` | Case-insensitive substring of stderr |
| `expect.stdout_contains` | Case-insensitive substring of stdout |
| `expect.stdout_empty` | Nothing may be printed to stdout |

Extra values in `setup`:

| Value | Creates |
| --- | --- |
| `["mode", "0700", "content"]` | A regular file with exactly this mode |
| `["dirmode", "0500", { ... }]` | A directory with these children, then this mode |

In `expect.tree`, `["mode", "0644", "content"]` checks the exact mode of a regular file. Other values
are compared through the execute bits only, as in the conformance suite.

## Known gaps

- On error the tree is never checked. Several source suites also assert that nothing is written on
  error, or which partial tree is left; those assertions were dropped.
- Only zero vs. non-zero exit status is checked, not exact codes.
- The root of the target directory cannot be given a mode, and stdin cannot be something other than
  a byte stream (e.g. a directory). Tests needing these were skipped.
