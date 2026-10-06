# json2dir (Rust reference): extraction notes

Sources: `src/tests.rs` (`test_unix`, `test`: table-driven calls to `parse_and_run` after an
environment setup) and `scripts/helpers/test-cli`. Other `scripts/*` and `.github/workflows/*`
are build/coverage plumbing, not tests.

## src/tests.rs::test_unix

| # | Input / environment | Result | Outcome |
|---|---|---|---|
| 1 | `{"/": ""}` | NotRegularComponent | skipped: duplicate of core/133-name-slash |
| 2 | `{".": ""}` | NotRegularComponent | skipped: duplicate of core/131-name-dot |
| 3 | `{"..": ""}` | NotRegularComponent | skipped: duplicate of core/132-name-dot-dot |
| 4 | `{"foo": [1, 2, 3]}` | InvalidJsonArray | converted: 001-array-three-numbers.json |
| 5 | `{"foo": "kek"}`, foo -> bar symlink | Ok | converted: 002-file-replaces-dangling-symlink.json (tree inferred from the code: link removed, then file written) |
| 6 | `{"foo": {}}`, foo dir mode 0600 | ChangeDir error | converted: 003-unsearchable-existing-dir-fails.json (`only`: the RFC does not require entering a dir for an empty object; requires_non_root) |
| 7 | `{"foo": ["link", "bar"]}` | Ok | skipped: duplicate of core/012-symlink |
| 8 | `{"foo": ["script", "Hello"]}`, foo dir | Create error | skipped: duplicate of overwrite/214-script-over-directory-fails |
| 9 | `{"foo": ["script", ""]}` | Ok | skipped: duplicate of core/017-empty-script |
| 10 | `{"foo": ["linksym", ""]}` | InvalidArrayKind | skipped: duplicate of core/123-array-unknown-kind |
| 11 | `{"foo": {}}` + CauseChangeDirUpError | ChangeDirUp | skipped: fault injection (crate::Test action) |
| 12 | `{"foo": ["script", ""]}` + RemoveScriptAfterCreation | CouldNotMakeFileExecutable | skipped: fault injection |
| 13 | `{"foo": ["script", ""]}` + RemoveScriptAfterGettingMode | CouldNotMakeFileExecutable | skipped: fault injection |
| 14 | `{"foo": ["link", ""]}` (Linux only) | Create error | converted: 004-empty-link-target-fails.json |
| 15 | `{"foo": ["link", ""]}`, foo dir | Create error | skipped: duplicate of overwrite/213-symlink-over-directory-fails |

## src/tests.rs::test

| # | Input / environment | Result | Outcome |
|---|---|---|---|
| 1 | `3` | InvalidTopJson | skipped: duplicate of core/108-root-number |
| 2 | `{"foo": {}}` | Ok | skipped: duplicate of core/008-nested-directories |
| 3 | `3 4` | CouldNotParseJson | converted: 005-trailing-data-after-number.json |
| 4 | `{"foo/bar": ""}` | MultiplePathComponents | skipped: duplicate of core/135-name-two-components |
| 5 | `{"file": "Hello!"}` | Ok | skipped: duplicate of core/002-regular-file |
| 6 | `{}` | Ok | skipped: duplicate of core/001-empty-object |
| 7 | `{"file": 3}` | InvalidJsonPart | skipped: duplicate of core/110-value-number |
| 8 | `{"foo": {}}`, foo dir | Ok | skipped: duplicate of overwrite/210-merge-into-directory |
| 9 | `{"foo": "Hello"}`, foo dir | Create error | skipped: duplicate of overwrite/212-file-over-directory-fails |
| 10 | `{"foo": "Hello"}`, foo empty file | Ok | skipped: duplicate of overwrite/200-file-replaces-file |
| 11 | `{"foo": {"bar": "baz"}}`, cwd read-only | Create error | converted: 006-unwritable-existing-dir-fails.json (the case format cannot set the target dir's own mode, so the read-only dir is `d`, one level down; requires_non_root) |
| 12 | `{"foo": {"bar": {"": "error"}}}` | MultiplePathComponents | converted: 007-nested-empty-name.json |

## scripts/helpers/test-cli

| Check | Outcome |
|---|---|
| `--help` exits non-zero and prints "usage" (2>&1) | converted: 008-cli-help-argument-fails.json (shared, exit status only) and 009-cli-help-argument-prints-usage.json (`only`, stderr text) |
| `{}` exits 0 | skipped: duplicate of core/001-empty-object |
| `f` exits non-zero | skipped: duplicate of core/100-invalid-json |
| byte 0xFF exits non-zero | converted: 010-cli-bare-invalid-utf8-byte.json |
