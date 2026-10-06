# json2dir-F- — extraction notes

Sources: `tests/test_cli.py`, `src/Json2dir.Proofs.fst`.
44 cases, 18 of them `only` (001–044).

Assertions that the case format cannot express were dropped:
- `run_json` asserts empty stdout on every run, and non-empty stderr on every failure. The RFC does not require empty stdout, so it is kept only in 001 (only);
- "the directory stays empty / unchanged" after an error. The tester never checks the tree on error;
- running the generated script.

## tests/test_cli.py — CliTests

| Test / subcase | Result |
| --- | --- |
| test_example | 001-example-stdout-empty (only). The tree alone duplicates core/018 |
| test_empty_and_whitespace | dup core/019 + core/001 |
| test_arguments `--help` / `x` / `x y` | 002 / 003 / 004 (only, stderr contains "usage:") |
| test_unicode_and_escapes | 005-unicode-name-and-escaped-content |
| test_every_unicode_boundary escaped / raw | 006-unicode-boundaries-escaped / 007-unicode-boundaries-raw |
| test_invalid_json `""` / `f` / `{}{}` | dup core/102 / 100 / 103 |
| `3 4` | dup core/108 + core/103 |
| `{"x":}` | 008-missing-member-value |
| `{"x":"unterminated}` | 009-unterminated-string |
| `{"x":"line<LF>break"}` | 010-raw-newline-in-string |
| `\q` | 011-invalid-escape |
| `\ud800` | dup core/104 |
| `\udc00` | 012-lone-low-surrogate |
| `\ud800A` | 013-high-surrogate-then-non-low |
| `\u0xx0` | 014-bad-unicode-escape-hex |
| `01`, `-`, `1.`, `1e`, `+1` as values | dup core/110: any number value fails, so a parse error gives the same outcome |
| `NaN`, `Infinity` | dup core/105 |
| `{"x":false,}` | dup core/111: `false` fails whatever the trailing comma does |
| `[1,]` | dup core/106 |
| `{x:"y"}` | 015-unquoted-member-name |
| `{"x":null // comment\n}` | dup core/112: `null` fails whatever the comment does |
| `﻿{}` (BOM) | 016-bom-rejected (only: RFC §3 lets a consumer ignore a BOM) |
| test_invalid_utf8 `\xff` | dup core/101 |
| `\xc0\xaf`, `\xed\xa0\x80`, `\xf4\x90\x80\x80` (bare bytes, not inside a document) | dup core/100/101: invalid JSON whatever the encoding |
| `{"x":"\xe2\x82"}` | 017-utf8-truncated-sequence |
| test_number_range_is_validated_before_writes `{"a":"unchanged","z":1e400}` | dup core/110 ("nothing written" cannot be expressed) |
| `{"x":1e400,"x":"last"}` | 018-duplicate-with-out-of-range-number (only) |
| `{"x":-1e400}`, `{"x":999…9}` | dup core/110 |
| test_stdin_read_error | skipped: stdin is a directory fd, which the case format cannot express |
| test_top_level `"x"` / `[]` / `0` / `-1.25e+3` / `true` / `false` / `null` | 021–027 (only: they assert the stderr text "expected provided JSON to be an object"). The plain outcome is new only for `true` / `false`: 019-root-true, 020-root-false |
| test_invalid_values (None, True, False, 7, 1.5, [], ["link"], ["link",7], ["script","x","y"], ["unknown","x"], [{},"x"]) | dup core/112, 111, 111, 110, 110, 120, 121, 126, 122, 123, 125 |
| test_path_validation `""`, `.`, `..`, `/`, `/root`, `a/b`, `./a`, `../a`, `a\x00b` | dup core/130, 131, 132, 133, 134, 135, 136, 137, 138 |
| test_path_validation `a/../b` | 028-name-inner-parent-traversal |
| test_path_validation valid names (.hidden, back\\slash, -option) | dup core/011 |
| test_rust_path_normalization step 1 `folder//./` with no `folder` → fail | 029-trailing-slash-dot-new-dir-fails (only) |
| step 2 `folder//` → creates folder | 030-trailing-double-slash-creates-dir (accept_error) |
| step 3 `folder//./` with existing folder → merge | 031-trailing-slash-dot-existing-dir (accept_error) |
| step 4 `bad/` with a string → fail | 032-trailing-slash-file-fails (only) |
| test_duplicates_and_order run 1 | 033-duplicate-names-invalid-first-occurrence (accept_error, as in core/020) |
| test_duplicates_and_order run 2 | 034-invalid-value-with-existing-tree (error only; "z unchanged" cannot be expressed) |
| test_replacement_and_directory_merge run 1 (fresh tree) | covered by the setup of 035 / core basics |
| test_replacement_and_directory_merge run 2 | 035-overwrite-merge-and-symlink-combined |
| test_directories_are_not_removed `"file"` / script / link | dup overwrite/212 / 214 / 213 |
| test_symlink_target_is_not_modified `target` | dup overwrite/207 |
| test_symlink_target_is_not_modified `missing` (dangling link) | 036-file-replaces-dangling-symlink |
| test_directory_symlink_is_replaced | dup overwrite/209 |
| test_unwritable_directory | 037-unwritable-existing-directory-fails (requires_non_root) + 038-unwritable-directory-message-names-path (only, stderr "./locked/file") |
| test_unsearchable_empty_directory | 039-unsearchable-existing-directory-fails (only, requires_non_root: the RFC does not require this failure; the Rust part of the test is differential and was skipped) |
| test_modes_respect_umask 022 / 077 / 777 | 040-umask-022-modes / 041-umask-077-modes-on-replace / 042-umask-777-modes-on-replace. In the source, runs 2 and 3 replace the files from the previous run, so they are converted with that setup |
| test_nesting_limit | 043-nesting-128-below-root-rejected (only: RFC requires 64 levels and allows rejecting deeper documents) |
| test_long_payload | 044-long-payload (about 1 MB case file) |
| test_differential | skipped: differential/random |

## src/Json2dir.Proofs.fst

Every lemma is a general theorem about `Json2dir.Spec`. The concrete rules they state are already covered by conformance cases, so no new cases.

| Lemma | Result |
| --- | --- |
| interpret_parts_safe | skipped: proof (internal helper) |
| parse_name_safe | skipped: proof; observable rule = core/130–139 |
| parse_name_no_traversal | skipped: proof; examples "", ".", "..", "/", NUL = dup core/130, 131, 132, 133, 138 |
| parse_name_single_component | skipped: proof; no prefix / single component = dup core/134–137 |
| valid_name_has_safe_component | skipped: proof (internal) |
| classify_directory / classify_file / classify_link / classify_script | dup core/008 / 002 / 012 / 016 |
| classify_unknown_kind | dup core/123 |
| classify_empty_array | dup core/120 |
| classify_null / classify_bool / classify_num | dup core/112 / 111 / 110 |

## Notes

- Duplicate names use last-wins, and an invalid earlier value (`3`) is ignored. But `{"x":1e400,"x":"last"}` fails, because serde-style parsing rejects an out-of-range number before last-wins applies. bimbo accepts `{"x":null,"x":"last"}`; it was not tested with 1e400.
- Trailing-slash names copy the Rust std quirks: `folder//` and `folder//./` are accepted for an object value, but `folder//./` fails when the directory does not exist yet, and `bad/` with a string value fails. The RFC allows either rejecting or accepting these names, so the error cases are `only`.
- In case 042 the file has mode 0000. Reading it back needs root, which the tester has.
