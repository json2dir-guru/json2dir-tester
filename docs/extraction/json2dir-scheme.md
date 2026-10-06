# json2dir-scheme: extraction notes

Source: `tests-dirty/json2dir-scheme/tests/test_cli.py`. 78 cases written, 14 of them `only`.

| Test | Subtest / variant | Outcome |
| --- | --- | --- |
| test_example |  | skipped: duplicate of core/018-upstream-example (same example tree) |
| test_empty_object |  | skipped: duplicate of core/001-empty-object + core/019-whitespace-around-document |
| test_arguments | `--help` | converted -> 001-usage-help.json (only) |
| test_arguments | `input.json` | converted -> 002-usage-input-json.json (only) |
| test_arguments | `--` | converted -> 003-usage-dashdash.json (only) |
| test_arguments | `a b` | converted -> 004-usage-a-b.json (only) |
| test_utf8_independent_of_locale |  | skipped: needs LC_ALL=C environment (not expressible); content covered by core/005, 006, 010 |
| test_string_escapes_and_surrogate_pairs |  | converted -> 005-all-json-escapes.json |
| test_invalid_utf8 | `b'\xff'` | converted -> 006-invalid-utf8-bare-byte.json |
| test_invalid_utf8 | `b'{"file":"\xc0\xaf"}'` | converted -> 007-invalid-utf8-overlong.json |
| test_invalid_utf8 | `b'{"file":"\xed\xa0\x80"}'` | converted -> 008-invalid-utf8-encoded-surrogate.json |
| test_invalid_utf8 | `b'{}\xff'` | converted -> 009-invalid-utf8-after-document.json |
| test_invalid_utf8 | `b'{"file":"\xe2\x82"}'` | converted -> 010-invalid-utf8-truncated.json |
| test_invalid_json_has_no_side_effects | `''` | skipped: duplicate of core/102-empty-input |
| test_invalid_json_has_no_side_effects | `' '` | converted -> 011-invalid-json-whitespace-only.json |
| test_invalid_json_has_no_side_effects | `'f'` | skipped: duplicate of core/100-invalid-json |
| test_invalid_json_has_no_side_effects | `'3 4'` | converted -> 012-invalid-json-two-values.json |
| test_invalid_json_has_no_side_effects | `'{} {}'` | skipped: duplicate of core/103-trailing-data |
| test_invalid_json_has_no_side_effects | `'{} trailing'` | converted -> 013-invalid-json-trailing-garbage.json |
| test_invalid_json_has_no_side_effects | `'{'` | converted -> 014-invalid-json-lone-open-brace.json |
| test_invalid_json_has_no_side_effects | `'['` | converted -> 015-invalid-json-lone-open-bracket.json |
| test_invalid_json_has_no_side_effects | `'{,}'` | converted -> 016-invalid-json-lone-comma.json |
| test_invalid_json_has_no_side_effects | `'{"a": "x",}'` | converted -> 017-invalid-json-object-trailing-comma.json |
| test_invalid_json_has_no_side_effects | `'{"a": "x" "b": "y"}'` | converted -> 018-invalid-json-missing-comma.json |
| test_invalid_json_has_no_side_effects | `'{,"a": "x"}'` | converted -> 019-invalid-json-leading-comma.json |
| test_invalid_json_has_no_side_effects | `'{"a": "x",, "b": "y"}'` | converted -> 020-invalid-json-double-comma.json |
| test_invalid_json_has_no_side_effects | `'{"a" "x"}'` | converted -> 021-invalid-json-missing-colon.json |
| test_invalid_json_has_no_side_effects | `'{a: "x"}'` | converted -> 022-invalid-json-unquoted-name.json |
| test_invalid_json_has_no_side_effects | `'{"a": ["script", "x",]}'` | converted -> 023-invalid-json-array-trailing-comma.json |
| test_invalid_json_has_no_side_effects | `'{"a": "\\q"}'` | converted -> 024-invalid-json-invalid-escape-q.json |
| test_invalid_json_has_no_side_effects | `'{"a": "\n"}'` | converted -> 025-invalid-json-raw-newline-in-string.json |
| test_invalid_json_has_no_side_effects | `'{"a": "\\uZZZZ"}'` | converted -> 026-invalid-json-a-uzzzz.json |
| test_invalid_json_has_no_side_effects | `'{"a": "\\uD800"}'` | skipped: duplicate of core/104-lone-surrogate |
| test_invalid_json_has_no_side_effects | `'{"a": "\\uDC00"}'` | converted -> 027-invalid-json-a-udc00.json |
| test_invalid_json_has_no_side_effects | `'{"a": "\\uD800\\u0041"}'` | converted -> 028-invalid-json-a-ud800-u0041.json |
| test_invalid_json_has_no_side_effects | `'{"a": "unfinished}'` | converted -> 029-invalid-json-unterminated-string.json |
| test_invalid_json_has_no_side_effects | `'{"a": 01}'` | converted -> 030-invalid-json-number-leading-zero.json |
| test_invalid_json_has_no_side_effects | `'{"a": +1}'` | converted -> 031-invalid-json-number-plus-sign.json |
| test_invalid_json_has_no_side_effects | `'{"a": .1}'` | converted -> 032-invalid-json-number-leading-dot.json |
| test_invalid_json_has_no_side_effects | `'{"a": 1.}'` | converted -> 033-invalid-json-number-trailing-dot.json |
| test_invalid_json_has_no_side_effects | `'{"a": 1e}'` | converted -> 034-invalid-json-number-bare-exponent.json |
| test_invalid_json_has_no_side_effects | `'{"a": 1e+}'` | converted -> 035-invalid-json-number-signed-bare-exponent.json |
| test_invalid_json_has_no_side_effects | `'{"a": NaN}'` | skipped: duplicate of core/105-nan-literal |
| test_invalid_json_has_no_side_effects | `'{"a": Infinity}'` | converted -> 036-invalid-json-a-infinity.json |
| test_invalid_json_has_no_side_effects | `'{"a": 1e309}'` | converted -> 037-invalid-json-a-1e309.json |
| test_invalid_json_has_no_side_effects | `'{"a": 1E999999}'` | converted -> 038-invalid-json-a-1e999999.json |
| test_invalid_json_has_no_side_effects | `'{"a": 0x10}'` | converted -> 039-invalid-json-a-0x10.json |
| test_invalid_json_has_no_side_effects | `'\ufeff{}'` | converted -> 040-bom-rejected.json (only) |
| test_invalid_json_has_no_side_effects | `'{}\x0b'` | converted -> 041-invalid-json-vertical-tab-after-document.json |
| test_invalid_json_has_no_side_effects | `'{"a": truefalse}'` | converted -> 042-invalid-json-a-truefalse.json |
| test_invalid_json_has_no_side_effects | `'{"a":"would write", "z":}'` | converted -> 043-invalid-json-parse-before-write.json |
| test_root_must_be_object | `3` | skipped: duplicate of core/108-root-number |
| test_root_must_be_object | `1.5` | skipped: duplicate of core/108-root-number |
| test_root_must_be_object | `-1e-10` | skipped: duplicate of core/108-root-number |
| test_root_must_be_object | `true` | converted -> 044-root-true.json |
| test_root_must_be_object | `false` | converted -> 045-root-false.json |
| test_root_must_be_object | `null` | skipped: duplicate of core/109-root-null |
| test_root_must_be_object | `[]` | skipped: duplicate of core/106-root-array |
| test_root_must_be_object | `"text"` | skipped: duplicate of core/107-root-string |
| test_unsupported_entry_values | `1` | skipped: duplicate of core/110-value-number |
| test_unsupported_entry_values | `-3.5` | skipped: duplicate of core/110-value-number |
| test_unsupported_entry_values | `True` | skipped: duplicate of core/111-value-boolean |
| test_unsupported_entry_values | `False` | skipped: duplicate of core/111-value-boolean |
| test_unsupported_entry_values | `None` | skipped: duplicate of core/112-value-null |
| test_unsupported_entry_values | `0e999999` | converted -> 046-value-extreme-number-0e999999.json |
| test_unsupported_entry_values | `1e-999999` | converted -> 047-value-extreme-number-1e-999999.json |
| test_unsupported_entry_values | `1e-309` | converted -> 048-value-extreme-number-1e-309.json |
| test_unsupported_entry_values | `1.7976931348623157e308` | converted -> 049-value-extreme-number-1-7976931348623157e308.json |
| test_array_validation | `[]` | skipped: duplicate of core/120-array-empty |
| test_array_validation | `["link"]` | skipped: duplicate of core/121-array-one-element |
| test_array_validation | `["script", "x", "y"]` | skipped: duplicate of core/122-array-three-elements |
| test_array_validation | `[1, "x"]` | skipped: duplicate of core/125-array-non-string-kind |
| test_array_validation | `["link", 3]` | skipped: duplicate of core/126-array-non-string-payload |
| test_array_validation | `["script", {}]` | skipped: duplicate of core/127-array-object-payload |
| test_array_validation | `["link", null]` | converted -> 050-array-null-payload.json |
| test_array_validation | `["unknown", "x"]` | skipped: duplicate of core/123-array-unknown-kind |
| test_rejected_paths | `''` | skipped: duplicate of core/130 |
| test_rejected_paths | `'/'` | skipped: duplicate of core/133 |
| test_rejected_paths | `'.'` | skipped: duplicate of core/131 |
| test_rejected_paths | `'..'` | skipped: duplicate of core/132 |
| test_rejected_paths | `'../escape'` | skipped: duplicate of core/137 |
| test_rejected_paths | `'/absolute'` | skipped: duplicate of core/134 |
| test_rejected_paths | `'a/b'` | skipped: duplicate of core/135 |
| test_rejected_paths | `'./a'` | skipped: duplicate of core/136 |
| test_single_component_directory_paths | `foo/` | converted -> 051-trimmed-name-dir-slash.json (only) |
| test_single_component_directory_paths | `foo//` | converted -> 052-trimmed-name-dir-double-slash.json (only) |
| test_single_component_directory_paths | `foo/.` | converted -> 053-trimmed-name-dir-slash-dot.json (only) |
| test_single_component_directory_paths | `foo//./` | converted -> 054-trimmed-name-dir-double-slash-dot-slash.json (only) |
| test_normal_unusual_names |  | converted -> 055-unusual-names-colon-newline.json |
| test_existing_files_are_recreated | `file fixture -> string` | converted -> 056-replace-0700-file-gets-default-mode.json |
| test_existing_files_are_recreated | `second step: file -> object` | skipped: duplicate of overwrite/202-directory-replaces-file |
| test_existing_directory_is_merged |  | skipped: duplicate of overwrite/210-merge-into-directory |
| test_directory_cannot_be_replaced_by_file_or_link | `text` | skipped: duplicate of overwrite/212 (keep-file check not expressible on error) |
| test_directory_cannot_be_replaced_by_file_or_link | `["script", "text"]` | skipped: duplicate of overwrite/214 (keep-file check not expressible on error) |
| test_directory_cannot_be_replaced_by_file_or_link | `["link", "target"]` | skipped: duplicate of overwrite/213 (keep-file check not expressible on error) |
| test_symlinks_are_unlinked_without_touching_targets | `kind=file-link, value="new"` | skipped: duplicate of overwrite/207-file-does-not-follow-symlink |
| test_symlinks_are_unlinked_without_touching_targets | `kind=file-link, value={"child": "x"}` | converted -> 057-object-replaces-file-link.json |
| test_symlinks_are_unlinked_without_touching_targets | `kind=file-link, value=["link", "elsewhere"]` | converted -> 058-link-replaces-file-link.json |
| test_symlinks_are_unlinked_without_touching_targets | `kind=file-link, value=["script", "x"]` | skipped: duplicate of overwrite/208-script-does-not-follow-symlink |
| test_symlinks_are_unlinked_without_touching_targets | `kind=directory-link, value="new"` | converted -> 059-string-replaces-directory-link.json |
| test_symlinks_are_unlinked_without_touching_targets | `kind=directory-link, value={"child": "x"}` | skipped: duplicate of overwrite/209-directory-does-not-follow-symlink |
| test_symlinks_are_unlinked_without_touching_targets | `kind=directory-link, value=["link", "elsewhere"]` | converted -> 060-link-replaces-directory-link.json |
| test_symlinks_are_unlinked_without_touching_targets | `kind=directory-link, value=["script", "x"]` | converted -> 061-script-replaces-directory-link.json |
| test_symlinks_are_unlinked_without_touching_targets | `kind=dangling-link, value="new"` | converted -> 062-string-replaces-dangling-link.json |
| test_symlinks_are_unlinked_without_touching_targets | `kind=dangling-link, value={"child": "x"}` | converted -> 063-object-replaces-dangling-link.json |
| test_symlinks_are_unlinked_without_touching_targets | `kind=dangling-link, value=["link", "elsewhere"]` | skipped: duplicate of overwrite/206-symlink-replaces-symlink |
| test_symlinks_are_unlinked_without_touching_targets | `kind=dangling-link, value=["script", "x"]` | converted -> 064-script-replaces-dangling-link.json |
| test_duplicate_keys_last_value_wins |  | converted -> 065-duplicate-keys-earlier-invalid-values-ignored.json |
| test_sorted_order_and_partial_changes |  | converted -> 066-invalid-member-error-names-path.json (only) |
| test_invalid_entry_still_unlinks_existing_file |  | converted -> 067-invalid-value-over-existing-file-fails.json (only) |
| test_script_permissions_respect_umask_then_add_execute_bits | `mask=022` | converted -> 068-script-mode-umask-022.json |
| test_script_permissions_respect_umask_then_add_execute_bits | `mask=077` | converted -> 069-script-mode-umask-077.json |
| test_script_permissions_respect_umask_then_add_execute_bits | `mask=777` | converted -> 070-script-mode-umask-777.json |
| test_nul_in_filename_or_link_target_is_a_clean_error | `name "bad\0name", value "x"` | skipped: duplicate of core/138-name-nul |
| test_nul_in_filename_or_link_target_is_a_clean_error | `name "bad\0name", value {}` | converted -> 071-nul-in-name-object.json |
| test_nul_in_filename_or_link_target_is_a_clean_error | `name "bad\0name", value ["script", "x"]` | converted -> 072-nul-in-name-script.json |
| test_nul_in_filename_or_link_target_is_a_clean_error | `name "bad\0name", value ["link", "target"]` | converted -> 073-nul-in-name-link.json |
| test_nul_in_filename_or_link_target_is_a_clean_error | `link target with NUL` | converted -> 074-nul-in-link-target.json |
| test_permission_errors | `dir 0600, {}` | converted -> 075-untraversable-dir-empty-object-fails.json (only) |
| test_permission_errors | `dir 0500, file child` | converted -> 076-unwritable-dir-file-fails.json |
| test_recursion_limit | `126 levels` | converted -> 077-depth-126-accepted.json (only) |
| test_recursion_limit | `127 levels` | converted -> 078-depth-127-rejected.json (only) |
| test_library_restores_working_directory_and_locale |  | skipped: unit-internal (Guile library API) |
| ParityTests::test_fixed_cases |  | skipped: differential (JSON2DIR_REFERENCE) |
| ParityTests::test_overwrite_matrix |  | skipped: differential (JSON2DIR_REFERENCE) |
| ParityTests::test_generated_archives |  | skipped: differential (JSON2DIR_REFERENCE) |

## Notes

- test_invalid_json_has_no_side_effects / test_invalid_utf8 / test_rejected_paths also assert the tree stays empty; error cases cannot check the tree, so only the non-zero exit is kept.
- The BOM test is `only`: RFC 3 says a consumer MAY ignore a leading BOM; scheme (like Rust) rejects it.
- test_single_component_directory_paths: accepting trimmed names is a MAY in RFC 4.2.1, so `only`. json2dir-ats REJECTS `foo/.` and `foo//./` (passes the raw spelling to mkdir) while json2dir-scheme normalizes and accepts them.
- Duplicate-key tests use `accept_error` (RFC 4.2 allows rejecting duplicates), like core/020; the source asserts success.
- test_sorted_order_and_partial_changes / test_invalid_entry_still_unlinks_existing_file: the partial tree after an error (sorted processing leaves `a`; the existing `foo` is unlinked before the value is validated) cannot be expressed, since error cases don't compare trees. Only exit status and stderr are kept, as `only` cases.
- test_permission_errors first step is `only`: an implementation that does not need to enter a directory to process `{}` may legitimately succeed.
- Depth limit (126 accepted, 127 rejected: serde_json's limit) is beyond the RFC minimum of 64, so both are `only`. The source's follow-up assertion that nothing is written for the 127-level case is not expressible.
