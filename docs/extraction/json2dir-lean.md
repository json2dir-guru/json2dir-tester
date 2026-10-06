# json2dir-lean: extraction notes

Source: `Spec.lean` (definitions + theorems; `Main.lean` uses `parseName` and `classify`).
No theorem contains a concrete name literal, so the cases come from the concrete behavior of the
`parseName` definition the theorems are about: it drops empty and "." segments, then requires
exactly one segment and no leading "/" or "./". Accepting trailing-slash names is an RFC "MAY",
so those cases use `accept_error` instead of `only`.

| Item | Outcome |
|---|---|
| def safeComponent | skipped: unit-internal (covered via parseName cases) |
| def interpretParts | skipped: unit-internal |
| theorem interpretParts_safe | skipped: proof |
| def parseName: "a/" accepted as "a" | converted: 001-name-trailing-slash.json |
| def parseName: "a/." accepted as "a" | converted: 002-name-trailing-slash-dot.json |
| def parseName: "a/./" accepted as "a" | converted: 003-name-trailing-slash-dot-slash.json |
| def parseName: "a//" accepted as "a" | converted: 004-name-double-trailing-slash.json |
| def parseName: "a/.." rejected | converted: 005-name-trailing-dot-dot.json |
| def parseName: "a/./b" rejected | converted: 006-name-dot-segment-in-middle.json |
| def parseName: "", ".", "..", "/", "/x", "./a", "a/b" rejected | skipped: duplicates of core/130-137 |
| theorem parseName_safe | skipped: proof (no concrete example) |
| theorem parseName_noTraversal | skipped: proof (no concrete example) |
| def classify | skipped: unit-internal |
| theorems classify_directory / classify_file / classify_link / classify_script | skipped: duplicates of core/008, 002, 012, 016 |
| theorem classify_unknown_kind | skipped: duplicate of core/123-array-unknown-kind |
| theorem classify_empty_array | skipped: duplicate of core/120-array-empty |
| theorems classify_null / classify_bool / classify_num | skipped: duplicates of core/112, 111, 110 |
