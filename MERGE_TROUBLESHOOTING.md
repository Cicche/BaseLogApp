# Merge troubleshooting

## Duplicate member errors in `Core/Data/JumpsReader.cs`

If your local build reports duplicate members (for example duplicate `GetJumpTypeNamesAsync` or `ResolveDbPath`) but the branch history shows only one definition, the file was likely duplicated during a merge conflict resolution.

Use the following recovery flow to restore only this file from `origin/master` while keeping other local work:

```bash
git stash -u
git checkout origin/master -- Core/Data/JumpsReader.cs
```

Then rebuild and re-apply any intentional edits to `JumpsReader.cs` manually.
