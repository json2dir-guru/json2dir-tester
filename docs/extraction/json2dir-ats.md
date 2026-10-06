# json2dir-ats: extraction notes

Source: `tests-dirty/json2dir-ats/tests/test_cli.py`. 72 cases written, 14 of them `only`.

| Test | Subtest / variant | Outcome |
| --- | --- | --- |
| Contract::test_empty_and_usage | `whitespace + {}` | skipped: duplicate of core/001 + core/019 |
| Contract::test_empty_and_usage | `--help` | converted -> 001-usage-help.json (only) |
| Contract::test_empty_and_usage | `anything else` | converted -> 002-usage-anything-else.json (only) |
| Contract::test_tree |  | skipped: duplicate of core/018-upstream-example (script has trailing newline) |
| Contract::test_utf8_escapes_and_nul_content | `ascii=False` | converted -> 003-utf8-boundary-chars-raw.json |
| Contract::test_utf8_escapes_and_nul_content | `ascii=True` | converted -> 004-utf8-boundary-chars-escaped.json |
| Contract::test_utf8_escapes_and_nul_content | `escape string` | converted -> 005-escaped-slash-nul-surrogates.json |
| Contract::test_script_modes_respect_umask | `mask=000` | converted -> 006-modes-umask-000.json |
| Contract::test_script_modes_respect_umask | `mask=022` | converted -> 007-modes-umask-022.json |
| Contract::test_script_modes_respect_umask | `mask=077` | converted -> 008-modes-umask-077.json |
| Contract::test_script_modes_respect_umask | `mask=777` | converted -> 009-modes-umask-777.json |
| Contract::test_symlinks_and_literal_names |  | converted -> 010-literal-names-and-link-targets.json |
| Contract::test_replacement_and_directory_merge | `combined fixture` | converted -> 011-combined-replace-and-merge.json |
| Contract::test_replacement_and_directory_merge | `file -> {}` | skipped: duplicate of overwrite/202-directory-replaces-file |
| Contract::test_existing_directory_cannot_be_replaced_by_file | `text` | skipped: duplicate of overwrite/212 |
| Contract::test_existing_directory_cannot_be_replaced_by_file | `["script", "text"]` | skipped: duplicate of overwrite/214 |
| Contract::test_existing_directory_cannot_be_replaced_by_file | `["link", "elsewhere"]` | skipped: duplicate of overwrite/213 |
| Contract::test_sorted_last_duplicate_wins | `false, string, object` | converted -> 012-duplicate-keys-last-object-wins.json |
| Contract::test_sorted_last_duplicate_wins | `escaped duplicate` | converted -> 013-duplicate-keys-via-escape.json |
| Contract::test_sorted_last_duplicate_wins | `partial on error` | converted -> 014-invalid-member-error-message.json (only) |
| Contract::test_sorted_last_duplicate_wins | `nested order` | converted -> 015-nested-tree-unsorted-input.json |
| Contract::test_paths | `invalid ''` | skipped: duplicate of core/130 |
| Contract::test_paths | `invalid '/'` | skipped: duplicate of core/133 |
| Contract::test_paths | `invalid '.'` | skipped: duplicate of core/131 |
| Contract::test_paths | `invalid '..'` | skipped: duplicate of core/132 |
| Contract::test_paths | `invalid './'` | converted -> 016-name-dot-slash.json |
| Contract::test_paths | `invalid '//'` | converted -> 017-name-double-slash.json |
| Contract::test_paths | `invalid './foo'` | skipped: duplicate of core/136 |
| Contract::test_paths | `invalid '/foo'` | skipped: duplicate of core/134 |
| Contract::test_paths | `invalid 'foo/bar'` | skipped: duplicate of core/135 |
| Contract::test_paths | `invalid 'foo/..'` | converted -> 018-name-trailing-dot-dot.json |
| Contract::test_paths | `invalid 'foo/./bar'` | converted -> 019-name-interior-dot.json |
| Contract::test_paths | `'foo/' -> {}` | converted -> 020-trailing-slash-dir-accepted.json (only) |
| Contract::test_paths | `'foo/' -> string` | converted -> 021-trailing-slash-file-rejected.json (only) |
| Contract::test_paths | `'foo//' -> {}` | converted -> 022-trailing-double-slash-dir-accepted.json (only) |
| Contract::test_paths | `'foo//' -> string` | converted -> 023-trailing-double-slash-file-rejected.json (only) |
| Contract::test_paths | `'foo/.' -> {}` | converted -> 024-trimmed-slash-dot-dir-rejected.json (only) |
| Contract::test_paths | `'foo//./' -> {}` | converted -> 025-trimmed-double-slash-dot-slash-dir-rejected.json (only) |
| Contract::test_paths | `NUL name, value "x"` | skipped: duplicate of core/138-name-nul |
| Contract::test_paths | `NUL name, value {}` | converted -> 026-nul-in-name-object.json |
| Contract::test_paths | `NUL name, value ["link", "elsewhere"]` | converted -> 027-nul-in-name-link.json |
| Contract::test_paths | `NUL link target` | converted -> 028-nul-in-link-target.json |
| Contract::test_paths | `nested ..` | converted -> 029-nested-dot-dot-error-names-path.json (only) |
| Contract::test_invalid_entries | `value None` | skipped: duplicate of core/112 |
| Contract::test_invalid_entries | `value False` | skipped: duplicate of core/111 |
| Contract::test_invalid_entries | `value True` | skipped: duplicate of core/111 |
| Contract::test_invalid_entries | `value 1` | skipped: duplicate of core/110 |
| Contract::test_invalid_entries | `value -1` | skipped: duplicate of core/110 |
| Contract::test_invalid_entries | `value 0.25` | skipped: duplicate of core/110 |
| Contract::test_invalid_entries | `value 1e-200` | skipped: duplicate of core/110 |
| Contract::test_invalid_entries | `array []` | skipped: duplicate of core/120 |
| Contract::test_invalid_entries | `array ["link"]` | skipped: duplicate of core/121 |
| Contract::test_invalid_entries | `array ["link", "x", "extra"]` | skipped: duplicate of core/122 |
| Contract::test_invalid_entries | `array [1, "x"]` | skipped: duplicate of core/125 |
| Contract::test_invalid_entries | `array ["script", null]` | converted -> 030-array-script-null-payload.json |
| Contract::test_invalid_entries | `array [["link"], "x"]` | converted -> 031-array-nested-array-kind.json |
| Contract::test_invalid_entries | `kind "unknown"` | skipped: duplicate of core/123 |
| Contract::test_invalid_entries | `kind "LINK"` | skipped: duplicate of core/124 |
| Contract::test_invalid_entries | `kind ''` | converted -> 032-array-kind-empty.json |
| Contract::test_invalid_entries | `kind 'link\x00'` | converted -> 033-array-kind-link-nul.json |
| Contract::test_invalid_entries | `kind 'scripts'` | converted -> 034-array-kind-scripts.json |
| Contract::test_invalid_entries | `root None` | skipped: duplicate of core/109 |
| Contract::test_invalid_entries | `root 1` | skipped: duplicate of core/108 |
| Contract::test_invalid_entries | `root "hello"` | skipped: duplicate of core/107 |
| Contract::test_invalid_entries | `root []` | skipped: duplicate of core/106 |
| Contract::test_invalid_entries | `root False` | converted -> 035-root-false.json |
| Contract::test_invalid_entries | `root ["script", "x"]` | converted -> 036-root-script-array.json |
| Contract::test_malformed_json_has_no_effects | `b''` | skipped: duplicate of core/102-empty-input |
| Contract::test_malformed_json_has_no_effects | `b' '` | converted -> 037-invalid-json-whitespace-only.json |
| Contract::test_malformed_json_has_no_effects | `b'{'` | converted -> 038-invalid-json-lone-open-brace.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":"ok",}'` | converted -> 039-invalid-json-object-trailing-comma.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":}'` | converted -> 040-invalid-json-missing-value.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x" "y"}'` | converted -> 041-invalid-json-missing-colon.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":"unterminated}'` | converted -> 042-invalid-json-unterminated-string.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":"raw\nline"}'` | converted -> 043-invalid-json-raw-newline-in-string.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":"\\v"}'` | converted -> 044-invalid-json-invalid-escape-v.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":"\\u12xz"}'` | converted -> 045-invalid-json-x-u12xz.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":"\\ud800"}'` | skipped: duplicate of core/104-lone-surrogate |
| Contract::test_malformed_json_has_no_effects | `b'{"x":"\\udc00"}'` | converted -> 046-invalid-json-x-udc00.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":"\\ud800\\u0041"}'` | converted -> 047-invalid-json-x-ud800-u0041.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":"a"} trailing'` | converted -> 048-invalid-json-trailing-garbage.json |
| Contract::test_malformed_json_has_no_effects | `b'{} {}'` | skipped: duplicate of core/103-trailing-data |
| Contract::test_malformed_json_has_no_effects | `b'3 4'` | converted -> 049-invalid-json-two-values.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":01}'` | converted -> 050-invalid-json-number-leading-zero.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":-}'` | converted -> 051-invalid-json-number-bare-minus.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":1.}'` | converted -> 052-invalid-json-number-trailing-dot.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":1e}'` | converted -> 053-invalid-json-number-bare-exponent.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":1e+}'` | converted -> 054-invalid-json-number-signed-bare-exponent.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":1e400}'` | converted -> 055-invalid-json-x-1e400.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":NaN}'` | skipped: duplicate of core/105-nan-literal |
| Contract::test_malformed_json_has_no_effects | `b'{"x":TRUE}'` | converted -> 056-invalid-json-uppercase-true.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":[1,]}'` | converted -> 057-invalid-json-array-trailing-comma.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":[1 2]}'` | converted -> 058-invalid-json-array-missing-comma.json |
| Contract::test_malformed_json_has_no_effects | `b'\xef\xbb\xbf{}'` | converted -> 059-bom-rejected.json (only) |
| Contract::test_malformed_json_has_no_effects | `b'{"x":"\x80"}'` | converted -> 060-invalid-utf8-lone-continuation-byte.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":"\xc0\xaf"}'` | converted -> 061-invalid-utf8-overlong.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":"\xed\xa0\x80"}'` | converted -> 062-invalid-utf8-encoded-surrogate.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":"\xf4\x90\x80\x80"}'` | converted -> 063-invalid-utf8-above-u10ffff.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":"\xe2\x82"}'` | converted -> 064-invalid-utf8-truncated.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":"\x00"}'` | converted -> 065-invalid-json-raw-nul-in-string.json |
| Contract::test_malformed_json_has_no_effects | `b'{"x":"a"}\xff'` | converted -> 066-invalid-utf8-after-document.json |
| Contract::test_malformed_json_has_no_effects | `b'{"a": "would be written", "z": [1,]}'` | converted -> 067-invalid-json-parse-before-write.json |
| Contract::test_depth_limit | `126 levels` | converted -> 068-depth-126-accepted.json (only) |
| Contract::test_depth_limit | `127 levels` | converted -> 069-depth-127-rejected.json (only) |
| Contract::test_large_content_and_wide_object | `large content` | converted -> 070-large-content.json |
| Contract::test_large_content_and_wide_object | `wide object` | converted -> 071-wide-object-1501-files.json |
| Contract::test_random_valid_trees |  | skipped: random (seeded generator; optional reference comparison is differential) |
| Contract::test_stdin_read_error |  | skipped: needs stdin to be a directory fd (not expressible) |
| Contract::test_filesystem_permissions | `dir 0600, {}` | converted -> 072-untraversable-dir-empty-object-fails.json (only) |
| Contract::test_filesystem_permissions | `target dir 0500` | skipped: needs the target directory itself read-only (setup cannot set the root's mode) |
| Contract::test_reference_edge_cases |  | skipped: differential (JSON2DIR_REFERENCE) |

## Notes

- Umask 777 drops the empty directory, as in the source (a mode-000 directory cannot be entered).
- Duplicate-key tests use `accept_error` (RFC 4.2 allows rejection), like core/020; the source asserts success. The partial tree after an error is not expressible, so that subtest is an `only` stderr check.
- Trimmed names (RFC 4.2.1 MAY, hence `only`): ats accepts `foo/` and `foo//` for objects only, fails them for strings, and fails `foo/.` and `foo//./` even for objects. json2dir-scheme accepts all four for objects. That is a real disagreement between the two ports of the same Rust original.
- Malformed-input tests also assert an empty tree; error cases cannot check the tree, so only the non-zero exit is kept. Invalid UTF-8 gives 'couldn't convert stdin to JSON' in ats but 'couldn't read stdin' in scheme; shared cases don't check the message. The BOM case is `only` (RFC 3: MAY ignore a BOM).
- Depth 126 accepted / 127 rejected exceeds the RFC minimum of 64, so `only`.
