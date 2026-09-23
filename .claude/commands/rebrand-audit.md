---
description: Find leftover Alpha 6 references and classify each as intended or unintended
---

Audit the tracked files for the old brand. Read-only; do not edit anything.

1. Run `git grep -n -i -I -E "alpha ?[-_]?6|\bA6\b"` from the repository root. `-I` skips binaries.
2. Load the allowlist from the "Stays unchanged on purpose" section of `docs/s4-migration/rebrand-plan.md`.
   It covers historical release notes, applied migrations such as the `alpha6_*` Postgres functions,
   compatibility fallbacks for old data paths/prefixes/User-Agent, sample flight numbers, and the migration docs.
3. Group the remaining hits by the plan's categories (a)–(h) and give counts per file.
4. Report: total hits, allowlisted hits, **unintended hits** (the number that must reach zero), and the
   top files to fix next. If $ARGUMENTS names a category or path, limit the audit to it.
