# Gap Checklist – Rich Model DB

## A. Data Access
- [ ] SQLite connection / provider abstraction
- [ ] Repository for ZLOGENTRY (rich only)
- [ ] Lookup repositories: ZJUMPTYPE, ZDEPLOYMENTTYPE, ZBRAKESETTING, ZPILOTCHUTETYPE, ZSLIDERTYPE
- [ ] Object repository: ZOBJECT (+ images)
- [ ] Rig repository: ZRIG + ZRIGCOMPONENT
- [ ] Join table handling: Z_5RIGS (logentry <-> rigs)
- [ ] LogEntryImage repository: ZLOGENTRYIMAGE (0..N images per entry)

## B. Domain Models / DTO
- [ ] JumpEntry domain model (with resolved lookup names)
- [ ] Object/ExitPoint model (with region/lat/long/height)
- [ ] Rig model (+ components)
- [ ] Images model (log entry images, object images)
- [ ] Handling of orphan refs (nullable rig/object/lookup)

## C. Time
- [ ] Cocoa timestamp converter (2001 epoch) bidirectional
- [ ] "date-only" policy (store at noon local or midnight UTC) – confirm behavior

## D. Writes (Core Data safe)
- [ ] PK allocator using Z_PRIMARYKEY.Z_MAX per table/entity
- [ ] Transaction wrapper
- [ ] INSERT JumpEntry rich
- [ ] UPDATE JumpEntry rich
- [ ] INSERT/DELETE rig links in Z_5RIGS
- [ ] INSERT/DELETE images in ZLOGENTRYIMAGE
- [ ] Optional: prevent duplicates via ZUNIQUEID (if used)

## E. Validation & Tests
- [ ] Schema sanity checks at startup
- [ ] Read regression tests (existing DB sample)
- [ ] Write tests on a copy DB
- [ ] Orphan references tests
- [ ] Backup guidance & tooling

## F. UI/Features
- [ ] List jumps sorted by date/number
- [ ] Detail page with resolved lookups
- [ ] Add new jump
- [ ] Edit existing jump
- [ ] Attach rig(s)
- [ ] Attach images