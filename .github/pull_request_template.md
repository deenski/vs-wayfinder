Process rules live only in Linear FDY-41 (was KAN-40) and this template. Only the assigned reviewer gives next steps on a PR.

Follow the [ponytail skill](https://github.com/DietrichGebert/ponytail): smallest complete change, never cut validation, security or tests. Reviews are ponytail reviews (real problems only, one pass, nits go to a Linear follow-up issue).

## Ticket

`FDY-##`

## Summary

<!-- 1 to 3 sentences on what changed and why. -->

## Author pre-review checklist

- [ ] No conflicts with main
- [ ] CI green (guard-and-unit-tests, build-and-load)
- [ ] Base branch merged in and CI green
- [ ] At least one "can this test fail?" probe done (break the code and watch the test fail)
- [ ] Every claimed fix checked in the code at the head sha

With two reviewers, both review the same head at the same time, and the author batches all fixes into one push.

## Tests

- [ ] `dotnet test` passes from the repo root
- [ ] `dotnet build -c Release` passes (if the mod project was touched)
- [ ] Headless server or in-game load checked (if runtime behavior changed)
- [ ] New or changed behavior has tests
- [ ] CI green

## Safety

- [ ] No game files, game DLLs, server archives, or secrets are added or referenced outside `$(VINTAGE_STORY)`
- [ ] Shared setup touched? If yes, Senior Engineer is the assigned reviewer.

## Merge

Merge rule: one assigned-reviewer approval + green CI, then squash-merge.

- [ ] Squash commit body includes `Approved-by: <reviewer> at <sha>`. The approval comes by agent message plus a note on the Linear issue.
