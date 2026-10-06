# json2dir-agda: extraction notes

Source: `tests/test_cli.py + src/Json2dir/Spec.agda`. Cases written: 69 (18 with `only`).
General notes:
- The `run_json` helper asserts on every run: exit code 0/1, stdout empty, stderr empty if and only if success. These checks are not repeated per case; only the usage cases set `stdout_empty`.
- Assertions that the tree stays `{}` after an error ("has no effects", "overflow before writes") are lost, because the format does not check the tree on error.
- Sequential multi-run tests were split into separate cases, with the earlier runs folded into `setup` (test_replacement_and_merge, test_normalized_names_keep_original_io_spelling).
- Spec.agda: only `parseName` gives observable rules beyond the conformance suite. Names that still have two or more components after dropping empty and `.` parts are rejected (`a/./b`, `a/b/`), and names with trailing `/` or `/.` are reduced to one component (already covered by the test_cli trailing-name cases). `safeComponent` (empty, `.`, `..`, `/`, NUL) and `classify` (object/string/link/script, bad arrays, other values) only restate core/110-139. The error strings in Spec.agda are not asserted by any test, so no message cases were made from them. `decide`, `SafeName`, `partsAcc`, `regularParts` and `hasPrefix` are internal: skipped as unit-internal.

Differential test (skipped): `test_rust_differential`, enabled with `JSON2DIR_REFERENCE=BIN`. It builds 117 inputs, each run in an empty directory, and compares the exit code and snapshot with the Rust binary. The inputs are: 100 random trees from `random.Random(20261006)` (depth 3, 1 to 5 members, names = index + one of русский/ascii/😀/.dot/back\slash, random content from `ab\n\t\0😀я"\\`, kinds file/script/link `../target`/subtree, with ensure_ascii alternating); 11 fixed edge documents (duplicates with a number, `dir/./`, `dir/`, `.`, bad kind, lone surrogate, `a/b`, ±1e400); 4 number-duplicate cases (0e99999, 1e-99999, DBL_MAX, 1.7976931348623158e308); and nesting 126 and 127.

| Test | Parameter | Outcome |
| --- | --- | --- |
| test_example |  | skipped: duplicate of core/018 (also executes the script; not expressible) |
| test_empty_and_whitespace |  | skipped: duplicate of core/019 + core/001 |
| test_usage | --help | converted -> 001-usage-help.json (only) |
| test_usage | x | converted -> 002-usage-x.json (only) |
| test_usage | x y | converted -> 003-usage-x-y.json (only) |
| test_unicode_and_escapes |  | converted -> 004-escapes-in-content.json |
| test_unicode_boundaries | ensure_ascii=True | converted -> 005-unicode-boundaries-escaped.json |
| test_unicode_boundaries | ensure_ascii=False | converted -> 006-unicode-boundaries-raw.json |
| test_invalid_json_has_no_effects | `` | skipped: duplicate of core/102 |
| test_invalid_json_has_no_effects | `f` | skipped: duplicate of core/100 |
| test_invalid_json_has_no_effects | `3 4` | converted -> 007-invalid-json-trailing-number.json |
| test_invalid_json_has_no_effects | `{}{}` | skipped: duplicate of core/103 |
| test_invalid_json_has_no_effects | `{"x":}` | converted -> 008-invalid-json-missing-value.json |
| test_invalid_json_has_no_effects | `{"x":"unterminated}` | converted -> 009-invalid-json-unterminated-string.json |
| test_invalid_json_has_no_effects | `{"x":"raw\nnewline"}` | converted -> 010-invalid-json-raw-newline-in-string.json |
| test_invalid_json_has_no_effects | `{"x":"\q"}` | converted -> 011-invalid-json-invalid-escape.json |
| test_invalid_json_has_no_effects | `{"x":"\ud800"}` | skipped: duplicate of core/104 |
| test_invalid_json_has_no_effects | `{"x":"\udc00"}` | converted -> 012-invalid-json-lone-low-surrogate.json |
| test_invalid_json_has_no_effects | `{"x":"\ud800\u0041"}` | converted -> 013-invalid-json-high-surrogate-then-bmp.json |
| test_invalid_json_has_no_effects | `{"x":"\u0xx0"}` | converted -> 014-invalid-json-non-hex-unicode-escape.json |
| test_invalid_json_has_no_effects | `{"x":01}` | converted -> 015-invalid-json-number-leading-zero.json |
| test_invalid_json_has_no_effects | `{"x":-}` | converted -> 016-invalid-json-number-lone-minus.json |
| test_invalid_json_has_no_effects | `{"x":1.}` | converted -> 017-invalid-json-number-no-fraction-digits.json |
| test_invalid_json_has_no_effects | `{"x":1e}` | converted -> 018-invalid-json-number-no-exponent-digits.json |
| test_invalid_json_has_no_effects | `{"x":+1}` | converted -> 019-invalid-json-number-plus-sign.json |
| test_invalid_json_has_no_effects | `{"x":NaN}` | skipped: duplicate of core/105 |
| test_invalid_json_has_no_effects | `{"x":Infinity}` | converted -> 020-invalid-json-infinity-literal.json |
| test_invalid_json_has_no_effects | `{"x":false,}` | converted -> 021-invalid-json-trailing-comma-object.json |
| test_invalid_json_has_no_effects | `[1,]` | converted -> 022-invalid-json-trailing-comma-array.json |
| test_invalid_json_has_no_effects | `{x:"y"}` | converted -> 023-invalid-json-unquoted-name.json |
| test_invalid_json_has_no_effects | `{"x":null // comment\n}` | converted -> 024-invalid-json-line-comment.json |
| test_invalid_json_has_no_effects | `\ufeff{}` | converted -> 025-bom-rejected.json (only: RFC allows ignoring a BOM) |
| test_invalid_json_has_no_effects | `{"a":"ok","z":}` | converted -> 026-invalid-json-missing-value-after-valid-member.json |
| test_invalid_json_has_no_effects | (message check) | converted -> 027-invalid-json-message.json (only) |
| test_invalid_utf8 | b'\xff' | converted -> 028-invalid-utf8-bare-ff.json |
| test_invalid_utf8 | b'\xc0\xaf' | converted -> 029-invalid-utf8-bare-overlong.json |
| test_invalid_utf8 | b'\xed\xa0\x80' | converted -> 030-invalid-utf8-bare-encoded-surrogate.json |
| test_invalid_utf8 | b'\xf4\x90\x80\x80' | converted -> 031-invalid-utf8-bare-above-u10ffff.json |
| test_invalid_utf8 | b'{"x":"\xe2\x82"}' | converted -> 032-invalid-utf8-truncated-in-string.json |
| test_stdin_error |  | skipped: stdin is a directory fd; not expressible |
| test_top_level | "x" | skipped: duplicate of core/107 |
| test_top_level | [] | skipped: duplicate of core/106 |
| test_top_level | 0 | skipped: duplicate of core/108 |
| test_top_level | -1.25e+3 | skipped: duplicate of core/108 |
| test_top_level | true | converted -> 033-root-true.json |
| test_top_level | false | converted -> 034-root-false.json |
| test_top_level | null | skipped: duplicate of core/109 |
| test_top_level | (message check) | converted -> 035-root-not-object-message.json (only) |
| test_invalid_values | `null` | skipped: duplicate of core/112 |
| test_invalid_values | `true` | skipped: duplicate of core/111 |
| test_invalid_values | `false` | skipped: duplicate of core/111 |
| test_invalid_values | `7` | skipped: duplicate of core/110 |
| test_invalid_values | `1.5` | skipped: duplicate of core/110 |
| test_invalid_values | `[]` | skipped: duplicate of core/120 |
| test_invalid_values | `["link"]` | skipped: duplicate of core/121 |
| test_invalid_values | `["link", 7]` | skipped: duplicate of core/126 |
| test_invalid_values | `["script", "x", "y"]` | skipped: duplicate of core/122 |
| test_invalid_values | `["unknown", "x"]` | skipped: duplicate of core/123 |
| test_invalid_values | `[{}, "x"]` | converted -> 036-array-object-kind.json |
| test_paths | `` | skipped: duplicate of core/130 |
| test_paths | `.` | skipped: duplicate of core/131 |
| test_paths | `..` | skipped: duplicate of core/132 |
| test_paths | `/` | skipped: duplicate of core/133 |
| test_paths | `/root` | skipped: duplicate of core/134 |
| test_paths | `a/b` | skipped: duplicate of core/135 |
| test_paths | `./a` | skipped: duplicate of core/136 |
| test_paths | `../a` | skipped: duplicate of core/137 |
| test_paths | `a/../b` | converted -> 037-name-inner-parent.json |
| test_paths | `a\0b` | skipped: duplicate of core/138 |
| test_paths | success .hidden / back\slash / -option | skipped: duplicate of core/011 |
| test_normalized_names_keep_original_io_spelling | step 1 `folder//./`, no folder | converted -> 038-trailing-slash-dot-new-dir-rejected.json (only) |
| test_normalized_names_keep_original_io_spelling | step 2 `folder//` | converted -> 039-trailing-double-slash-creates-dir.json (only) |
| test_normalized_names_keep_original_io_spelling | step 3 `folder//./`, folder exists | converted -> 040-trailing-slash-dot-existing-dir-accepted.json (only) |
| test_normalized_names_keep_original_io_spelling | step 4 `file/` string | converted -> 041-trailing-slash-file-rejected.json (only) |
| test_duplicates_last_wins |  | converted -> 042-duplicate-number-then-string.json (accept_error: RFC allows rejecting) |
| test_sorted_keys_and_partial_failure |  | skipped: distinctive part is the partial tree on error (not checked by format); exit status dup of core/112 |
| test_invalid_value_removes_old_file |  | converted -> 043-invalid-value-over-existing-file.json (source also asserts the old file is deleted; not checkable on error) |
| test_replacement_and_merge |  | converted -> 044-overwrite-mixed.json (first run folded into setup) |
| test_existing_directories_stay | file | skipped: duplicate of overwrite/212 |
| test_existing_directories_stay | script | skipped: duplicate of overwrite/214 |
| test_existing_directories_stay | link | skipped: duplicate of overwrite/213 |
| test_symlink_replacement_keeps_target | link -> target | skipped: duplicate of overwrite/207 |
| test_symlink_replacement_keeps_target | link -> missing | converted -> 045-file-replaces-dangling-symlink.json |
| test_directory_symlink_replacement |  | skipped: duplicate of overwrite/209 |
| test_symlink_payload_is_literal |  | converted -> 046-link-target-literal.json |
| test_nul_in_link_target |  | converted -> 047-nul-in-link-target.json |
| test_umask | 022 | converted -> 048-umask-022.json |
| test_umask | 077 | converted -> 049-umask-077.json |
| test_umask | 777 | converted -> 050-umask-777.json |
| test_unwritable_directory |  | converted -> 051-unwritable-existing-dir.json |
| test_unwritable_directory | (message check) | converted -> 052-unwritable-existing-dir-message.json (only) |
| test_unsearchable_empty_directory |  | converted -> 053-unsearchable-existing-dir.json (only: RFC does not require failure) |
| test_overflow_before_writes_and_duplicates | `{"a":"x","z":1e400}` | skipped: duplicate of core/110 (number value is an error anyway; no-write not checkable) |
| test_overflow_before_writes_and_duplicates | `{"x":1e400,"x":"last"}` | converted -> 054-duplicate-overflow-number-1e400.json (only) |
| test_overflow_before_writes_and_duplicates | `{"a":"x","z":-1e400}` | skipped: duplicate of core/110 (number value is an error anyway; no-write not checkable) |
| test_overflow_before_writes_and_duplicates | `{"x":-1e400,"x":"last"}` | converted -> 055-duplicate-overflow-number-minus-1e400.json (only) |
| test_overflow_before_writes_and_duplicates | `{"a":"x","z":9*500}` | skipped: duplicate of core/110 (number value is an error anyway; no-write not checkable) |
| test_overflow_before_writes_and_duplicates | `{"x":9*500,"x":"last"}` | converted -> 056-duplicate-overflow-number-500-nines.json (only) |
| test_overflow_before_writes_and_duplicates | `{"a":"x","z":1.7976931348623158e308}` | skipped: duplicate of core/110 (number value is an error anyway; no-write not checkable) |
| test_overflow_before_writes_and_duplicates | `{"x":1.7976931348623158e308,"x":"last"}` | converted -> 057-duplicate-overflow-number-dbl-max-plus.json (only) |
| test_finite_number_duplicate_is_accepted | 0e99999 | converted -> 058-duplicate-finite-number-zero-huge-exponent.json (accept_error: RFC allows rejecting duplicates) |
| test_finite_number_duplicate_is_accepted | 0e9999999999999999999999 | converted -> 059-duplicate-finite-number-zero-giant-exponent.json (accept_error: RFC allows rejecting duplicates) |
| test_finite_number_duplicate_is_accepted | 1e-9999999999999999999 | converted -> 060-duplicate-finite-number-underflow.json (accept_error: RFC allows rejecting duplicates) |
| test_finite_number_duplicate_is_accepted | 1.7976931348623157e308 | converted -> 061-duplicate-finite-number-dbl-max.json (accept_error: RFC allows rejecting duplicates) |
| test_finite_number_duplicate_is_accepted | 0.0001 | converted -> 062-duplicate-finite-number-small-fraction.json (accept_error: RFC allows rejecting duplicates) |
| test_finite_number_duplicate_is_accepted | 18446744073709551616 | converted -> 063-duplicate-finite-number-2-pow-64.json (accept_error: RFC allows rejecting duplicates) |
| test_nesting_limit | 127 + `{}` | converted -> 064-nesting-128-objects-rejected.json (only: RFC MAY reject, does not require) |
| test_nesting_limit | 126 + `{}` | converted -> 065-nesting-127-objects-accepted.json (only: beyond RFC minimum of 64) |
| test_large_text |  | converted -> 066-large-text.json |
| test_wide_object |  | converted -> 067-wide-object.json |
| test_rust_differential | 117 inputs | skipped: differential (see below) |
| Spec.agda parseName | `a/./b` | converted -> 068-name-inner-dot.json |
| Spec.agda parseName | `a/b/` | converted -> 069-name-two-components-trailing-slash.json |
| Spec.agda safeComponent / classify | | skipped: restate core/110-139 (duplicate) |
| Spec.agda decide, SafeName, partsAcc, regularParts, hasPrefix, interpretParts | | skipped: unit-internal (parseName behavior converted above) |
