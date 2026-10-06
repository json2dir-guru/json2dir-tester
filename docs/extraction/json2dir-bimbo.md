# json2dir-bimbo — extraction notes

Sources: `tests/test_json2dir.py`, `tests/test_compiler.py`, `tests/demo.py`.
39 cases, 9 of them `only` (001–039).

Assertions that the case format cannot express were dropped:
- exit status exactly 1 (only zero vs. non-zero can be checked);
- "the tree stays empty / unchanged" after an error. The tester never checks the tree on error;
- running the generated script (`slay.sh`). Only its content and exec bits are checked.

## tests/test_json2dir.py — Json2DirTests

| Test / subcase | Result |
| --- | --- |
| test_example (closet.json tree) | 001-closet-example |
| test_example (stdout == b"") | 002-closet-example-stdout-empty (only) |
| test_invalid_inputs `""` | dup core/102 |
| `f` | dup core/100 |
| `3 4` | dup core/108 + core/103 |
| `{} {}` | dup core/103 |
| `[]` | dup core/106 |
| `"hi"` | dup core/107 |
| `null` | dup core/109 |
| `true` | 003-root-true |
| `false` | 004-root-false |
| `3`, `-1.2e3` | dup core/108 |
| `{"foo/bar":""}` | dup core/135 |
| `{"":""}` / `{"/":""}` / `{".":""}` / `{"..":""}` / `{"./foo":""}` / `{"/absolute":""}` | dup core/130 / 133 / 131 / 132 / 136 / 134 |
| `{"foo/../bar":""}` | 005-name-inner-parent-traversal |
| `{"x":1}` / `null` / `true` / `false` | dup core/110 / 112 / 111 / 111+113 |
| `{"x":[]}` / `["link"]` / `["link","y","z"]` / `[1,2]` / `["link",{}]` / `["linksym","y"]` | dup core/120 / 121 / 122 / 125 / 127 / 123 |
| `{"x":}` | 006-missing-member-value |
| `{"x":"y",}` | 007-trailing-comma-object |
| `{x:"y"}` | 008-unquoted-member-name |
| `{"x":"y"` | 009-unterminated-object |
| `{"x" "y"}` | 010-missing-colon |
| `{"x":["link","y",]}` | 011-trailing-comma-array |
| `{"x":"\q"}` | 012-invalid-escape |
| `{"x":"<LF>"}` | 013-raw-newline-in-string |
| `{"x":"\uD800"}` | dup core/104 |
| `{"x":"\uDC00"}` | 015-lone-low-surrogate |
| `{"x":"\uD800A"}` | 016-high-surrogate-then-non-low |
| `{"x":"\uZZZZ"}` | 017-bad-unicode-escape-hex |
| `01`, `+1`, `1.`, `1e`, `1e+`, `.1`, `1e309` as values | dup core/110: any number value fails, so a parse error gives the same outcome |
| `NaN`, `Infinity` as values | dup core/105 |
| `{"x":"unterminated}` | 018-unterminated-string |
| `﻿{}` (BOM) | 019-bom-rejected (only: RFC §3 lets a consumer ignore a BOM) |
| `{"x":"<TAB>"}` | 014-raw-tab-in-string |
| `{"a":"x"} garbage` | dup core/103 |
| `{"a\u0000b":"x"}` | dup core/138 |
| `{"x":["link","a\u0000b"]}` | 020-link-target-with-nul |
| `b'\xff'` | dup core/101 |
| `{"x":"\xc0\x80"}` | 021-utf8-overlong-nul |
| `{"x":"\xed\xa0\x80"}` | 022-utf8-encoded-surrogate |
| `{"x":"\xf4\x90\x80\x80"}` | 023-utf8-above-max-code-point |
| `{"x":"\xe2\x82"}` | 024-utf8-truncated-sequence |
| (stderr starts with `Error:`) | 025-error-message-prefix (only; checked as a substring, once) |
| test_arguments `--help` / `--version` / `file.json` | 026 / 027 / 028 (only, stderr contains "usage:") |
| test_unicode_escapes_and_binary_string_content | 029-escapes-in-name-and-content |
| test_overwrite_merge_and_symlink_replacement | 030-overwrite-merge-and-symlink-combined. The parts are covered by overwrite/200, 202, 207 and 210; kept because they run together in one document |
| test_existing_directory_is_not_recursively_removed `"new"` / `["script","new"]` / `["link","target"]` | dup overwrite/212 / 214 / 213 |
| test_duplicate_keys_last_wins | 031-duplicate-names-invalid-first-occurrence (accept_error, as in core/020) |
| test_sorted_keys_and_partial_semantic_failure | 032-invalid-value-over-existing-file (error only). The order-dependent partial state cannot be expressed: `a` created, existing `b` removed, `z` not created |
| test_entire_json_parsed_before_writes | 033-truncated-document-over-existing (error only; "a stays old" cannot be expressed) |
| test_cannot_escape_root `../sentinel` | dup core/137 |
| test_cannot_escape_root absolute path | dup core/134 |
| test_cannot_escape_root `dir/../../sentinel` | 034-name-inner-double-parent-traversal |
| test_script_mode_respects_umask_then_adds_execute | 035-umask-077-file-and-script-modes |
| test_unsearchable_directory | 036-unsearchable-existing-directory-fails (only, requires_non_root: the RFC does not require failing on an existing unsearchable directory with an empty object) |
| test_depth_limit depth 1 | dup core/002 |
| test_depth_limit depth 126, 127 | dup core/125: a nested array is an invalid kind whatever the depth |
| test_depth_limit depth 128 | 038-nesting-128-rejected-by-parser (only, stderr "convert stdin to JSON") |
| test_depth_limit depth 2000 | 037-deeply-nested-arrays-2000 (shared, error only) + 039-nesting-2000-rejected-by-parser (only, with the stderr text) |
| test_generated_valid_trees | skipped: random |
| DifferentialTests.test_original_contract_and_invalid_input | skipped: differential |
| DifferentialTests.test_generated_trees | skipped: differential/random |
| DifferentialTests.test_replacement_and_partial_failures | skipped: differential |
| DifferentialTests.test_depth_boundaries | skipped: differential |
| DifferentialTests.test_number_range_and_rounding_before_writes | skipped: differential/random |
| DifferentialTests.test_mutated_json | skipped: differential/fuzz |

## tests/test_compiler.py — CompilerTests (bimbo-lang compiler, not json2dir)

| Test | Result |
| --- | --- |
| test_direct_llvm_control_flow | skipped: unit-internal (compiler) |
| test_diagnostics (25 subcases) | skipped: unit-internal (compiler) |
| test_native_semantics_at_two_optimization_levels | skipped: unit-internal (compiler) |
| test_runtime_arithmetic_error | skipped: unit-internal (compiler) |
| test_cli_source_location | skipped: unit-internal (compiler CLI `bimboc check`) |

## tests/demo.py

| Item | Result |
| --- | --- |
| demo (runs examples/closet.json, prints tree, runs slay.sh) | same scenario as test_example, covered by 001. It asserts nothing beyond `check=True` |

## Notes

- The depth limit is 128 nested containers including the root, as in serde_json. Nested objects therefore reach 126 levels below the root, which meets the RFC minimum of 64.
- Rejecting a BOM is allowed by the RFC (MAY ignore), so case 019 is `only`.
- Duplicate names use last-wins: an invalid earlier value (`null`) is ignored and does not fail the run.
