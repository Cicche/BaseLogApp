# BaseLogbook DB (Rich Model) – Working Contract

You are working on branch: feature/db-codex

Goal:
- Keep using the existing SQLite database (Core Data style: Z_PK, Z_ENT, Z_OPT, Z_PRIMARYKEY).
- Implement read/write logic for *rich model only* jump entries (ignore minimal model).
- Produce a GAP REPORT of missing features vs this contract before implementing changes.
- Then implement step-by-step, small commits.

Inputs:
- docs/db/rich-model.schema.json is the source of truth for tables/columns/relationships and query templates.

Rules (must follow):
1) Rich model only:
   - Apply Z_ENT filters consistently (not only ZLOGENTRY; also lookups, objects, images, join tables).
2) No SQLite FOREIGN KEY constraints exist; do NOT assume cascades.
   - Use LEFT JOIN; tolerate orphan references (especially rig links).
3) Primary key allocation:
   - For every INSERT into a CoreData table with Z_PK, allocate Z_PK by reading/updating Z_PRIMARYKEY.Z_MAX within a transaction.
4) Timestamp handling:
   - ZDATE / ZLASTMODIFIEDUTC are Apple Cocoa timestamps (seconds since 2001-01-01 UTC), not Unix.
   - If the old app stored "date only", preserve the same convention.
5) Safety:
   - Never mutate master. Only commit on feature/db-codex.
   - Add tests and/or a dry-run mode before writing to user DB.
   - Always recommend working on a copy of DB for dev.

First task (GAP REPORT):
- Inspect current codebase and output a checklist with:
  - current behavior
  - required behavior
  - files/classes involved
  - missing SQL queries / repositories
  - missing models/DTOs
  - missing timestamp converters
  - missing PK allocator / transactions
  - missing insert/update flows
  - missing rig linking (join table)
  - missing image handling
  - missing validation/tests
  - risks & migration notes

Second task (Implementation plan):
- Propose incremental steps (each step a small PR/commit).
- Start with READ layer (list + detail), then INSERT, then UPDATE, then extras.