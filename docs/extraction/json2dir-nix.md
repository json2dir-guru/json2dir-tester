# json2dir-nix: extraction notes

Sources: `tests/cli.py` (black-box CLI), `tests/lib.nix` (Nix evaluation of `lib.nix`), `flake.nix` checks.
29 cases, 5 of them `only: ["json2dir-nix"]`.

## tests/cli.py

| Test / subtest | Result |
| --- | --- |
| test_example | skipped: duplicate of core/018-upstream-example |
| test_literal_bytes_and_names | converted → 001-literal-bytes-and-names |
| test_duplicate_keys_and_components: duplicate `a` | skipped: duplicate of core/020-duplicate-names (kept inside 002) |
| test_duplicate_keys_and_components: `dir//./` | converted → 002-trailing-separators-and-dot-in-name (accept_error: the RFC makes accepting trimmed names a MAY) |
| test_invalid_input_does_not_mutate: "destination unchanged" assertion | not expressible: error cases never check the tree, so it is dropped from every subcase below; stderr non-empty is also dropped |
| … `b""` | skipped: duplicate of core/102 |
| … `f` | skipped: duplicate of core/100 |
| … `{} {}` | skipped: duplicate of core/103 |
| … `{"x":"\xff"}` | skipped: duplicate of core/101 |
| … `\ud800` | skipped: duplicate of core/104 |
| … `\udc00` | converted → 003-lone-low-surrogate |
| … `"before\u0000after"` | converted → 004-nul-escape-in-content-rejected (**only**; contradicts RFC 4.3 / core/006) |
| … raw NUL byte in a string | converted → 005-raw-nul-byte-in-string |
| … raw newline in a string | converted → 006-raw-newline-in-string |
| … `{"x":1,}` | converted → 007-trailing-comma (value changed to a string so that only the trailing comma is wrong) |
| … `NaN` | skipped: duplicate of core/105 |
| … `Infinity` | converted → 008-infinity-literal |
| … `// comment` | converted → 009-comment |
| … `1e1000` | converted → 010-huge-number-value |
| … root `null` / `1` / `1.5` / `"root"` / `[]` | skipped: duplicates of core/109, 108, 108, 107, 106 |
| … root `true` | converted → 011-root-true |
| … root `false` | converted → 012-root-false |
| … names `""` `.` `..` `/` `/absolute` `./foo` `foo/bar` | skipped: duplicates of core/130–136 |
| … name `foo/../x` | converted → 013-name-interior-parent |
| … nested values `null` `true` `1` `[]` `["link"]` `["link","x","y"]` `["other","x"]` `[1,"x"]` `["script",{}]` | skipped: duplicates of core/112, 111, 110, 120, 121, 122, 123, 125, 126/127 (with the tree check dropped, the valid sibling `a` adds nothing) |
| test_arguments `--help` | converted → 015-argument-help (**only**: the RFC allows other interfaces) |
| test_arguments `file.json` | converted → 016-argument-file (**only**) |
| test_arguments `a b` | converted → 017-two-arguments (**only**) |
| test_replacement_matrix: missing → file/script/link/directory | skipped: duplicates of core/002, 016, 015, 008 |
| … file → file/script/link/directory | skipped: duplicates of overwrite/200, 204, 203, 202 |
| … script → file | skipped: duplicate of overwrite/205 |
| … script → script | converted → 018-script-replaces-script |
| … script → link | converted → 019-symlink-replaces-script |
| … script → directory | converted → 020-directory-replaces-script |
| … link (to a directory) → file | converted → 021-file-replaces-symlink-to-directory |
| … link (to a directory) → script | converted → 022-script-replaces-symlink-to-directory |
| … link → link | skipped: duplicate of overwrite/206 |
| … link → directory | skipped: duplicate of overwrite/209 |
| … directory → file/script/link | skipped: duplicates of overwrite/212, 214, 213 |
| … directory → directory | skipped: duplicate of overwrite/210 |
| test_dangling_symlink_replacement | converted → 023-dangling-symlink-replaced-by-file |
| test_umask 022 / 077 / 027 / 777 | converted → 024-umask-022, 025-umask-077, 026-umask-027, 027-umask-777 (exact file modes; directory modes are not compared) |
| test_unsearchable_empty_directory | converted → 028-unsearchable-existing-directory (**only**, requires_non_root; the RFC does not require entering an existing directory that has no members) |
| test_large_content | converted → 029-large-content (about 1 MB case file) |
| test_generated_trees | skipped: random (seeded) generated trees |
| `success()` asserts empty stdout | not carried into the shared cases: the RFC does not require empty stdout |

## tests/lib.nix

| Test | Result |
| --- | --- |
| `valid` list (toShell accepts) | skipped: unit-internal; the same examples are covered by core cases and 001/002 |
| `invalid` list, entries that match cli.py | skipped: the same as the cli.py subtests above |
| `invalid` root `""` | skipped: duplicate of core/107 |
| `invalid` `{ deep.nested."../outside" = ...; }` | converted → 014-deep-nested-name-parent-traversal |
| `invalid` `{ a = ...; z.bad = false; }` | skipped: duplicate of core/113 |
| `parsed.a == "last"` (fromJSON duplicate keys) | skipped: duplicate of core/020 |
| `fromFile` equals `builtins.fromJSON` | skipped: unit-internal |
| `validate` returns its input | skipped: unit-internal |

## flake.nix checks

| Check | Result |
| --- | --- |
| library | skipped: runs tests/lib.nix (see above) |
| cli | skipped: runs tests/cli.py (see above) |
| tree (mkTree on example-tree.json) | skipped: unit-internal (Nix store build); same content as core/018 |
| context (store-path context in mkTree) | skipped: unit-internal (Nix-only feature) |
