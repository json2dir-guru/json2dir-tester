# json2dir-zig: extraction notes

Sources: `nix/smoke.nix` (CLI smoke test), `nix/zig-tests.nix` (only runs `zig build test`, no
tests of its own), inline `test` blocks in `src/manifest.zig`. Flags taken from `src/main.zig`:
`-o/--out DIR`, `-n/--dry-run`, `-v/--verbose`, `--no-clobber`, `-h/--help` (exit 0, stdout),
`-V/--version`, optional positional FILE. Exit codes 1 usage / 2 bad input / 3 fs error are not
expressible (only zero/non-zero).

## nix/smoke.nix

| Step | Check | Outcome |
|---|---|---|
| 1 | `-o out tree.json` materializes the example | converted: 001-smoke-materialize-file-into-out-dir.json (tree.json placed via setup; running the script replaced by the exec-bit check) |
| 2 | stdin + `-o out` rerun over existing out | converted: 002-smoke-stdin-rerun-into-existing-out.json |
| 3 | `--no-clobber` fails, stderr mentions `--no-clobber` | converted: 003-smoke-no-clobber-refuses.json |
| 4 | `--dry-run` writes nothing, plan mentions greeting/link | converted: 004-smoke-dry-run-writes-nothing.json (only "greeting" asserted; one stdout_contains per case) |
| 5 | invalid JSON exits 2 | skipped: duplicate of core/100-invalid-json (exit code 2 not expressible) |
| 6 | `{"a/b": "x"}` exits 2 | skipped: duplicate of core/135-name-two-components |
| 7 | `--wat` exits 1 | converted: 005-smoke-unknown-option-fails.json (shared; RFC 7 item 5) |
| 8 | symlink evil/greeting -> / replaced by file | converted, merged with the manifest.zig symlink test: 006-file-replaces-symlink-to-root.json |

## src/manifest.zig tests

| Test | Outcome |
|---|---|
| validateName rejects garbage | rejects "", ".", "..", "a/b", "/etc/passwd", "a\0b": duplicates of core/130-135, 138; accepts "...", ".hidden", "a b", "dot.json": duplicate of core/011; accepts "-": converted 007-name-single-dash.json |
| materialize: objects, files, link, script | skipped: duplicate of core/018-upstream-example |
| rerun is idempotent with default force | converted: 008-rerun-is-idempotent.json (shared) |
| --no-clobber refuses to replace | converted: 009-no-clobber-refuses-existing-file.json |
| --dry-run does not touch the filesystem | converted: 010-dry-run-creates-nothing.json |
| dry-run + no-clobber is faithful against an existing tree | converted: 011-dry-run-over-existing-tree.json, 012-dry-run-no-clobber-fails-on-existing-tree.json |
| dry-run below a nonexistent parent treats everything as new | converted: 013-dry-run-missing-out-dir.json (via `-o missing`, i.e. dir == null) |
| a pre-existing symlink at an entry path is replaced, not followed | converted: 006-file-replaces-symlink-to-root.json |
| unicode names, deep nesting, empty script | skipped: duplicate of core/010, core/009, core/017 |
| replace updates content and mode changes survive rerun | skipped: duplicate of overwrite/200-file-replaces-file |
| rejects unsupported documents | skipped: all 12 inputs duplicate core/106, 107, 109, 110, 111, 135, 130, 132, 123, 121, 126, 122 |
| link to a directory is replaced without recursing into it | skipped: duplicate of overwrite/209-directory-does-not-follow-symlink |

## Visible in the source but not asserted by any test (no case written)

- An object member over any existing entry calls `deleteTree` and recreates the directory, so an
  existing directory's unlisted contents are wiped (RFC 5.1 violation; conformance/210 and 211 should fail).
- A string/link/script over an existing directory deletes the directory tree instead of failing
  (RFC 5.4 violation; conformance/212-214 should fail).
- Files are forced to 0644 and scripts to 0755 regardless of umask (RFC 4.3: 0666 restricted by umask).
- `--help` exits 0 and prints to stdout, so json2dir/008-cli-help-argument-fails will fail here.
