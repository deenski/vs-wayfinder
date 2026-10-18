## Ticket

`KAN-##`

https://jakevendegna.atlassian.net/browse/KAN-##

## Summary

<!-- 1 to 3 sentences on what changed and why. -->

## Tests

- [ ] `dotnet test` passes from the repo root
- [ ] `dotnet build -c Release` passes (if the mod project was touched)
- [ ] Headless server or in-game load checked (if runtime behavior changed)
- [ ] New or changed behavior has tests
- [ ] CI green

## Safety

- [ ] No game files, game DLLs, server archives, or secrets are added or referenced outside `$(VINTAGE_STORY)`
- [ ] Shared setup touched (layout, global.json/.NET version, Directory.Build.*, the solution file, .github/workflows): Senior Engineer is reviewing.

## Jira summary (paste as the Done comment)

```
What changed:
Key decisions:
PR:
Merge commit:
Tests:
Follow-ups:
```
