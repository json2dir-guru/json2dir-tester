# json2dir-llvm-IR: extraction notes

Source: `tests/test_json2dir.py`. Cases written: 100 (30 with `only`).
General notes:
- The `invoke` helper in the source asserts on every run: exit code is 0 or 1, stdout is empty, stderr is empty on success and not empty on error, and there is no AddressSanitizer output. These implementation-wide checks are not repeated in each case; only the usage cases set `stdout_empty`.
- Many source tests also assert that the tree stays `{}` after an error (for example "invalid JSON never writes" and "sorted application leaves only `a`"). The case format does not check the tree on error, so these assertions are lost. The exit status is kept.
- Message assertions (`assertIn(b'...', stderr)`) appear once per diagnostic family, as a separate `only` case. The shared cases never check stderr.
- Duplicate-name cases use `accept_error` (like core/020) because the RFC lets a consumer reject duplicates. The source asserts that the last value wins.
- `trailing slashes/dots` (15 cases) and the nesting limits are `only`: the RFC leaves name trimming and depths above 64 open.

Differential test (skipped): `DifferentialTests.test_reference`, run with `--reference BIN`. It builds 820 inputs and runs each against 3 directory states (none; `files` = foo, a files + dir/keep; `links` = foo -> target dir with keep, a -> missing), for 820 x 3 = 2460 runs. It compares the exit code, the snapshot (types, modes, contents, link targets) and stdout with the Rust binary. The 820 inputs are:
10 fixed byte strings (duplicates, surrogates, `\xff`, ...); 23 keys x 15 values = 345 single-member documents (keys such as `''`, `.`, `foo/`, `foo/./`, `русский`, `foo\0bar`; values such as strings, objects, link/script, bad arrays, false/null/numbers); 11 number edge cases as `{"f":N}`; 4 nesting depths (126, 127, 128, 1000); 200 random documents from `random.Random(20261006)` (depth up to 3, names from a/b/c/dir/😀/./bad/name); and 250 random byte mutations (1 to 3 byte replacements or deletions) of a fixed valid document.

| Test | Parameter | Outcome |
| --- | --- | --- |
| test_example | example-tree.json | skipped: duplicate of core/018-upstream-example |
| test_empty_and_whitespace | `{}` | skipped: duplicate of core/001 |
| test_empty_and_whitespace | whitespace around `{}` | skipped: duplicate of core/019 |
| test_usage | --help | converted -> 001-usage-help.json (only) |
| test_usage | --version | converted -> 002-usage-version.json (only) |
| test_usage | file.json | converted -> 003-usage-file-json.json (only) |
| test_usage | a b | converted -> 004-usage-a-b.json (only) |
| test_top_level | 3 | skipped: duplicate of core/108 |
| test_top_level | null | skipped: duplicate of core/109 |
| test_top_level | true | converted -> 005-root-true.json |
| test_top_level | false | converted -> 006-root-false.json |
| test_top_level | [] | skipped: duplicate of core/106 |
| test_top_level | "foo" | skipped: duplicate of core/107 |
| test_top_level | -3.12e-2 | skipped: duplicate of core/108 |
| test_top_level | (message check) | converted -> 007-root-not-object-message.json (only) |
| test_invalid_json_never_writes | `` | skipped: duplicate of core/102 |
| test_invalid_json_never_writes | `f` | skipped: duplicate of core/100 |
| test_invalid_json_never_writes | `{} {}` | skipped: duplicate of core/103 |
| test_invalid_json_never_writes | `{"a":"ok"} garbage` | converted -> 008-invalid-json-trailing-garbage.json |
| test_invalid_json_never_writes | `{"a":"ok",}` | converted -> 009-invalid-json-trailing-comma-object.json |
| test_invalid_json_never_writes | `{"a":"ok","z":}` | converted -> 010-invalid-json-missing-value.json |
| test_invalid_json_never_writes | `{"a":"ok","z":"\q"}` | converted -> 011-invalid-json-invalid-escape.json |
| test_invalid_json_never_writes | `{"a":"ok","z":[1,]}` | converted -> 012-invalid-json-trailing-comma-array.json |
| test_invalid_json_never_writes | `{a:"x"}` | converted -> 013-invalid-json-unquoted-name.json |
| test_invalid_json_never_writes | `{"a" "x"}` | converted -> 014-invalid-json-missing-colon.json |
| test_invalid_json_never_writes | `{"a":"x"` | converted -> 015-invalid-json-unclosed-object.json |
| test_invalid_json_never_writes | `{"a":"unterminated}` | converted -> 016-invalid-json-unterminated-string.json |
| test_invalid_json_never_writes | `{"a":"raw\nnewline"}` | converted -> 017-invalid-json-raw-newline-in-string.json |
| test_invalid_json_never_writes | `{"a":01}` | converted -> 018-invalid-json-number-leading-zero.json |
| test_invalid_json_never_writes | `{"a":-}` | converted -> 019-invalid-json-number-lone-minus.json |
| test_invalid_json_never_writes | `{"a":1.}` | converted -> 020-invalid-json-number-no-fraction-digits.json |
| test_invalid_json_never_writes | `{"a":1e}` | converted -> 021-invalid-json-number-no-exponent-digits.json |
| test_invalid_json_never_writes | `{"a":+1}` | converted -> 022-invalid-json-number-plus-sign.json |
| test_invalid_json_never_writes | `{"a":NaN}` | skipped: duplicate of core/105 |
| test_invalid_json_never_writes | `{"a":Infinity}` | converted -> 023-invalid-json-infinity-literal.json |
| test_invalid_json_never_writes | `{"a":1e400}` | converted -> 024-invalid-json-number-overflow.json |
| test_invalid_json_never_writes | `{"a":.1}` | converted -> 025-invalid-json-number-no-integer-part.json |
| test_invalid_json_never_writes | `{"a":--1}` | converted -> 026-invalid-json-number-double-minus.json |
| test_invalid_json_never_writes | `{"a":trueX}` | converted -> 027-invalid-json-literal-true-suffix.json |
| test_invalid_json_never_writes | `{"a":falseX}` | converted -> 028-invalid-json-literal-false-suffix.json |
| test_invalid_json_never_writes | `{"a":nul}` | converted -> 029-invalid-json-literal-truncated-null.json |
| test_invalid_json_never_writes | `{"a":1e+}` | converted -> 030-invalid-json-number-exponent-sign-only.json |
| test_invalid_json_never_writes | `{"a":0x10}` | converted -> 031-invalid-json-number-hex.json |
| test_invalid_json_never_writes | `{}\0` | converted -> 032-invalid-json-trailing-nul-byte.json |
| test_invalid_json_never_writes | `{"a":"\u000"}` | converted -> 033-invalid-json-short-unicode-escape.json |
| test_invalid_json_never_writes | `{"a":"\uXX00"}` | converted -> 034-invalid-json-non-hex-unicode-escape.json |
| test_invalid_json_never_writes | `{"a":"\uD800"}` | skipped: duplicate of core/104 |
| test_invalid_json_never_writes | `{"a":"\uDC00"}` | converted -> 035-invalid-json-lone-low-surrogate.json |
| test_invalid_json_never_writes | `{"a":"\uD800\u0041"}` | converted -> 036-invalid-json-high-surrogate-then-bmp.json |
| test_invalid_json_never_writes | `{"a":"\uD800\uD800"}` | converted -> 037-invalid-json-two-high-surrogates.json |
| test_invalid_json_never_writes | `{"a":"\uD800x"}` | converted -> 038-invalid-json-high-surrogate-then-char.json |
| test_invalid_json_never_writes | `{"a":"\uD800\n"}` | converted -> 039-invalid-json-high-surrogate-then-escape.json |
| test_invalid_json_never_writes | `\ufeff{}` | converted -> 040-bom-rejected.json (only: RFC allows ignoring a BOM) |
| test_invalid_json_never_writes | (message check) | converted -> 041-invalid-json-message.json (only) |
| test_invalid_utf8 | b'\xff' | skipped: duplicate of core/101 |
| test_invalid_utf8 | b'\xc0\xaf' | converted -> 042-invalid-utf8-overlong-slash.json |
| test_invalid_utf8 | b'\x80' | converted -> 043-invalid-utf8-lone-continuation.json |
| test_invalid_utf8 | b'\xc2' | converted -> 044-invalid-utf8-truncated-2-byte.json |
| test_invalid_utf8 | b'\xe0\x80\x80' | converted -> 045-invalid-utf8-overlong-3-byte.json |
| test_invalid_utf8 | b'\xed\xa0\x80' | converted -> 046-invalid-utf8-encoded-surrogate.json |
| test_invalid_utf8 | b'\xf0\x80\x80\x80' | converted -> 047-invalid-utf8-overlong-4-byte.json |
| test_invalid_utf8 | b'\xf4\x90\x80\x80' | converted -> 048-invalid-utf8-above-u10ffff.json |
| test_invalid_utf8 | b'\xf5\x80\x80\x80' | converted -> 049-invalid-utf8-invalid-lead-f5.json |
| test_invalid_utf8 | b'\xe2(\xa1' | converted -> 050-invalid-utf8-bad-continuation.json |
| test_invalid_utf8 | b'\xf0\x9f\x98' | converted -> 051-invalid-utf8-truncated-4-byte.json |
| test_invalid_utf8 | (message check) | converted -> 052-invalid-utf8-message.json (only) |
| test_unicode_and_escapes | escaped name + all escapes | converted -> 053-all-escapes-and-boundaries.json |
| test_unicode_and_escapes | raw Unicode name/content | skipped: duplicate of core/005 + core/010 |
| test_empty_files |  | skipped: duplicate of core/003 + core/017 |
| test_nul_in_contents |  | converted -> 054-nul-in-script.json (file half dup of core/006; script half new) |
| test_nul_in_paths | name `x\0y` | skipped: duplicate of core/138 |
| test_nul_in_paths | link target `x\0y` | converted -> 055-nul-in-link-target.json |
| test_path_validation | `` | skipped: duplicate of core/130 |
| test_path_validation | `.` | skipped: duplicate of core/131 |
| test_path_validation | `..` | skipped: duplicate of core/132 |
| test_path_validation | `/` | skipped: duplicate of core/133 |
| test_path_validation | `/foo` | skipped: duplicate of core/134 |
| test_path_validation | `./foo` | skipped: duplicate of core/136 |
| test_path_validation | `foo/bar` | skipped: duplicate of core/135 |
| test_path_validation | `foo/../bar` | converted -> 056-name-inner-parent.json |
| test_path_validation | `foo/..` | converted -> 057-name-trailing-parent.json |
| test_path_validation | `foo//bar` | converted -> 058-name-double-slash.json |
| test_path_validation | `../foo` | skipped: duplicate of core/137 |
| test_path_validation | `./` | converted -> 059-name-dot-slash.json |
| test_path_validation | `//` | converted -> 060-name-double-slash-only.json |
| test_path_validation | success `back\slash`, `..normal` | converted -> 061-name-leading-dot-dot-prefix.json |
| test_trailing_slashes_and_dots | `foo/` string | converted -> 062-name-foo-slash-file-rejected.json (only) |
| test_trailing_slashes_and_dots | `foo/` object, no foo | converted -> 063-name-foo-slash-dir-new-accepted.json (only) |
| test_trailing_slashes_and_dots | `foo/` object, foo exists | converted -> 064-name-foo-slash-dir-existing-accepted.json (only) |
| test_trailing_slashes_and_dots | `foo//` string | converted -> 065-name-foo-slash-slash-file-rejected.json (only) |
| test_trailing_slashes_and_dots | `foo//` object, no foo | converted -> 066-name-foo-slash-slash-dir-new-accepted.json (only) |
| test_trailing_slashes_and_dots | `foo//` object, foo exists | converted -> 067-name-foo-slash-slash-dir-existing-accepted.json (only) |
| test_trailing_slashes_and_dots | `foo/.` string | converted -> 068-name-foo-slash-dot-file-rejected.json (only) |
| test_trailing_slashes_and_dots | `foo/.` object, no foo | converted -> 069-name-foo-slash-dot-dir-new-rejected.json (only) |
| test_trailing_slashes_and_dots | `foo/.` object, foo exists | converted -> 070-name-foo-slash-dot-dir-existing-accepted.json (only) |
| test_trailing_slashes_and_dots | `foo/./` string | converted -> 071-name-foo-slash-dot-slash-file-rejected.json (only) |
| test_trailing_slashes_and_dots | `foo/./` object, no foo | converted -> 072-name-foo-slash-dot-slash-dir-new-rejected.json (only) |
| test_trailing_slashes_and_dots | `foo/./` object, foo exists | converted -> 073-name-foo-slash-dot-slash-dir-existing-accepted.json (only) |
| test_trailing_slashes_and_dots | `foo//./` string | converted -> 074-name-foo-slash-slash-dot-slash-file-rejected.json (only) |
| test_trailing_slashes_and_dots | `foo//./` object, no foo | converted -> 075-name-foo-slash-slash-dot-slash-dir-new-rejected.json (only) |
| test_trailing_slashes_and_dots | `foo//./` object, foo exists | converted -> 076-name-foo-slash-slash-dot-slash-dir-existing-accepted.json (only) |
| test_invalid_values | null | skipped: duplicate of core/112 |
| test_invalid_values | true | skipped: duplicate of core/111 |
| test_invalid_values | false | skipped: duplicate of core/111/113 |
| test_invalid_values | 0 | skipped: duplicate of core/110 |
| test_invalid_values | -1 | skipped: duplicate of core/110 |
| test_invalid_values | 1.25 | skipped: duplicate of core/110 |
| test_invalid_values | (message check) | converted -> 077-invalid-value-message.json (only) |
| test_array_shapes | `[]` | skipped: duplicate of core/120 |
| test_array_shapes | `["link"]` | skipped: duplicate of core/121 |
| test_array_shapes | `["script"]` | skipped: duplicate of core/121 (same rule) |
| test_array_shapes | `[1, 2]` | skipped: duplicate of core/125 |
| test_array_shapes | `["link", 2]` | skipped: duplicate of core/126 |
| test_array_shapes | `[true, "x"]` | skipped: duplicate of core/125 |
| test_array_shapes | `["link", "x", "extra"]` | skipped: duplicate of core/122 |
| test_array_shapes | `[["link"], "x"]` | converted -> 078-array-array-kind.json |
| test_array_shapes | (shape message check) | converted -> 079-array-shape-message.json (only) |
| test_array_shapes | kind `` | converted -> 080-array-kind-empty.json |
| test_array_shapes | kind `Link` | skipped: duplicate of core/124 |
| test_array_shapes | kind `scripts` | converted -> 081-array-kind-scripts.json |
| test_array_shapes | kind `linksym` | skipped: duplicate of core/123 |
| test_array_shapes | kind `link\0` | converted -> 082-array-kind-link-nul.json |
| test_array_shapes | kind `script\0` | converted -> 083-array-kind-script-nul.json |
| test_array_shapes | (kind message check) | converted -> 084-array-kind-message.json (only) |
| test_duplicate_last_wins | `{"f":"old","f":"new"}` | skipped: duplicate of core/020 |
| test_duplicate_last_wins | `{"f":false,"f":"new"}` | converted -> 085-duplicate-invalid-then-valid.json (accept_error: RFC allows rejecting) |
| test_duplicate_last_wins | `{"f":{"nested":"old"},"f":"new"}` | converted -> 086-duplicate-object-then-string.json (accept_error: RFC allows rejecting) |
| test_duplicate_last_wins | `{"f":"old","\u0066":"new"}` | converted -> 087-duplicate-via-escaped-name.json (accept_error: RFC allows rejecting) |
| test_duplicate_last_wins | `{"f":"one","f":"two","f":"new"}` | converted -> 088-duplicate-three-times.json (accept_error: RFC allows rejecting) |
| test_sorted_application |  | skipped: only distinctive assertion is the partial tree left on error (format does not check trees on error); exit status alone dup of core/110 |
| test_sorted_unicode_keys |  | skipped: same reason as test_sorted_application |
| test_overwrite_and_preserve_unrelated_entries |  | converted -> 089-overwrite-mixed.json (combination of 200/207/210) |
| test_symlink_replacement_with_directory |  | skipped: duplicate of overwrite/209 |
| test_overwrite_file_with_directory |  | skipped: duplicate of overwrite/202 |
| test_directory_is_not_removed_for_file_or_link | text | skipped: duplicate of overwrite/212 |
| test_directory_is_not_removed_for_file_or_link | script | skipped: duplicate of overwrite/214 |
| test_directory_is_not_removed_for_file_or_link | link | skipped: duplicate of overwrite/213 |
| test_invalid_value_unlinks_existing_file |  | converted -> 090-invalid-value-over-existing-file.json (source also asserts the old file is deleted; not checkable on error) |
| test_script_umask | umask 000 | converted -> 091-umask-000.json |
| test_script_umask | umask 022 | converted -> 092-umask-022.json |
| test_script_umask | umask 077 | converted -> 093-umask-077.json |
| test_script_umask | umask 777 | converted -> 094-umask-777.json |
| test_nesting_limit | 127 | converted -> 095-nesting-127-objects-accepted.json (only: beyond RFC minimum of 64) |
| test_nesting_limit | 128 | converted -> 096-nesting-128-objects-rejected.json (only: RFC MAY reject, does not require) |
| test_nesting_limit | 3000 | converted -> 097-nesting-3000-objects-rejected.json (only: RFC MAY reject, does not require) |
| test_large_string_crosses_read_chunks |  | converted -> 098-large-string.json |
| test_many_reverse_order_keys |  | converted -> 099-many-reverse-order-keys.json |
| test_read_error |  | skipped: stdin is a directory fd; not expressible |
| test_closed_stderr |  | skipped: closed fd 2; not expressible |
| test_directory_permission_error |  | converted -> 100-unsearchable-existing-dir.json (only: RFC does not require failure for an empty object) |
| DifferentialTests::test_reference | 2460 runs | skipped: differential (see below) |
