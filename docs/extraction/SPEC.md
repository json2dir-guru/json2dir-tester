# Extraction spec: turning implementation test suites into json2dir-tester cases

Goal: convert each implementation's own tests into **black-box, language-agnostic cases** that
json2dir-tester can run against ANY json2dir implementation (stdin JSON in, directory tree out).

## Reference material (read these first)

- Case format and judging rules: `C:\work\json2dir_MANY\awesome-json2dir\conformance\README.md`
- RFC: `C:\work\json2dir_MANY\awesome-json2dir\spec\rfc-json2dir.md`
- Existing 68 cases (do NOT duplicate them): `C:\work\json2dir_MANY\projects\json2dir-tester\cases\conformance\` (core/, overwrite/)
- Raw tests to convert: `C:\work\json2dir_MANY\projects\json2dir-tester\tests-dirty\<impl>\`
- Full source of the implementation if needed: `C:\work\json2dir_MANY\others\<impl>\`

## Case format = conformance format + these optional extensions

```jsonc
{
  "description": "One sentence",
  "section": "5.3",                 // RFC section; "7" for CLI behavior; "-" if none fits
  "level": "core" | "overwrite" | "cli",
  "source": "json2dir-scheme tests/test_cli.py::test_dangling_link",   // REQUIRED: where it came from
  "only": ["json2dir-zig"],         // optional: run only for these implementations (see below)
  "args": ["--dry-run"],            // optional: extra CLI arguments, appended to the command
  "umask": "077",                   // optional: umask for the run (default 022)
  "requires_non_root": true,        // optional: case is meaningless as root (permission checks)
  "setup": { ... },                 // pre-existing tree; values as in expect.tree, plus:
                                    //   ["mode", "0700", "content"]  regular file with exact mode
                                    //   ["dirmode", "0500", { ...children }]  directory with exact mode (applied after children)
  "input" | "input_text" | "input_base64": ...,
  "expect": {
    "tree": { ... },                // expected tree; ["mode", "0644", "content"] also allowed for exact file modes
    "error": true,
    "accept_error": true,
    "stderr_contains": "usage",     // optional, case-insensitive substring of stderr
    "stdout_contains": "...",       // optional, case-insensitive substring of stdout
    "stdout_empty": true            // optional
  }
}
```

The tree read back from disk uses the conformance scheme: plain string = file with no exec bits,
["script", c] = file with all three exec bits, ["link", target], ["mode", "0744", c] = any other
exec-bit combination. Directories are objects. Directory modes are not compared, file modes
only through the exec bits (unless you use "mode").

## Rules

1. Only behavior observable from outside: exit status (zero / non-zero), resulting tree,
   stdout/stderr substrings. Skip pure unit tests of internal functions (parsers, helpers),
   proofs and theorems — BUT if a theorem/unit test states a concrete observable rule
   (e.g. "name `a/b` is rejected"), you MAY turn its concrete examples into cases.
2. Standard interface: the implementation reads the JSON from stdin and writes into the current
   directory. Cases that need implementation-specific flags (dry-run, -C dir, --plan, etc.) or
   assert behavior the RFC leaves open / that this implementation deliberately does differently
   from the RFC MUST have `"only": ["<impl>"]`.
3. Do not duplicate the 68 conformance cases — skip a test if an existing case checks the same
   (same setup+input+outcome, ignoring trivial content differences). When in doubt, keep it.
4. Differential / random / fuzz tests against the Rust reference: skip (note them).
5. Tests depending on file ownership/permissions of the target (unwritable dirs etc.): convert
   if possible and set `"requires_non_root": true` (the tester runs as root in WSL).
6. Exact error messages are implementation-specific: never assert message text in shared cases;
   only in `only` cases if the test really checks it.
7. Make the expected outcome what the SOURCE test asserts. If it contradicts the RFC, still
   write it but with `"only"` and mention it in notes.
8. One JSON file per case, pretty-printed (2-space indent), UTF-8, LF newlines.
   File name: `NNN-short-kebab-name.json` with NNN starting at 001 per implementation.

## Output

Write into `C:\work\json2dir_MANY\projects\json2dir-tester\tests-dirty\_extracted\<impl>\`:
- the case files;
- `NOTES.md`: a table of EVERY test in the source with what happened to it
  (converted → file name / skipped: duplicate of conformance/xxx / skipped: unit-internal /
  skipped: differential / skipped: other reason). Keep it terse.

Do not touch anything outside your `_extracted\<impl>\` directories. Do not run builds.
Report in your final message: number of cases written per implementation, number of `only`
cases, and anything surprising (RFC contradictions, behaviors that differ between implementations).
