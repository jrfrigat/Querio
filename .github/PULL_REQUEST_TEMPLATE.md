## What this changes

<!-- One or two sentences. What behaviour is different afterwards? -->

## Why

<!-- The problem, not the patch. If it fixes an issue, link it: Fixes #123 -->

## Checklist

- [ ] `dotnet build Querio.slnx -c Release` is clean
- [ ] `dotnet test Querio.slnx -c Release` passes
- [ ] Public API carries XML documentation (the build enforces this, but read it back - a summary
      should say what the member is for, not restate its name)
- [ ] A new package, if any, is listed in `QuerioIndependenceTests`
- [ ] A change to what a target can express is reflected in its `IQueryCapabilities`
- [ ] `CHANGELOG.md` and `CHANGELOG.ru.md` updated if this is user-visible
