# Contributing to Querio

Thanks for your interest. This covers what you need to get a change merged.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/) (the .NET 8 and 9 runtimes too, to run every TFM)
- A recent IDE - Visual Studio 2022/2026, Rider, or VS Code with the C# extension

## Build & test

```sh
dotnet restore Querio.slnx
dotnet build   Querio.slnx -c Release
dotnet test    Querio.slnx -c Release
```

Everything builds from one solution, and there is nothing to run: Querio has no sample app, because
the product is a library that produces objects. The tests are the demonstration - `tests/` is worth
reading as documentation of what each target does with a given query.

## Workflow

1. Fork and create a short-lived branch off `main` (`feat/mysql-dialect`, `fix/percentile-grouping`).
2. Make the change, with tests.
3. Make sure the build and the tests pass and CI is green on your PR.
4. Open a pull request against `main`. PRs are squash-merged, so the title becomes the commit -
   please use [Conventional Commits](https://www.conventionalcommits.org/) style
   (`feat(sql): ...`, `fix(onec): ...`, `docs: ...`).

## Conventions

**Public API is documented.** `CS1591` is an error for the shipped packages, so a missing
`/// <summary>` fails the build. Write summaries that say what a member is for and how it behaves -
"Gets or sets the alias" tells a reader nothing they could not see.

**ASCII only** in code, comments and XML docs. No arrows, em-dashes, ellipses or emoji. The
Cyrillic that `Querio.OneC` emits is output data, not source prose, and is exempt.

**Comments explain why.** The code already says what it does.

## What the design will not trade away

These are the load-bearing decisions. A change that breaks one of them needs to argue for itself.

- **The model is semantic, not SQL-shaped.** Support for the 1C query language is what keeps it
  honest, since 1C disagrees with SQL about nearly everything. Do not reshape the model toward ANSI
  SQL.
- **Values travel as data.** A condition holds a value, never a fragment of query text.
  Parameterisation belongs to the renderer, and nothing is ever concatenated into a query.
- **A target declares what it cannot do** through `IQueryCapabilities`, and throws
  `QueryRenderException` when asked for it. It never approximates - answering a narrower question
  silently is worse than failing.
- **No package takes a third-party dependency.** `QuerioIndependenceTests` enforces this for every
  package. If you add a package, add it there too.

## Adding a target

A new target implements `QueryRenderer<TExpression>` in the core, supplying only what a field, a
literal, a call and a comparison mean for it. `TExpression` is open on purpose: `string` for text,
`Expression` for LINQ, whatever suits. If you find yourself re-writing the walk, the shared one is
missing something - fix it there instead.

Declare the target's `IQueryCapabilities` honestly. A gap that fails loudly is a feature.

## Adding a two-way target

If your target reads back what it wrote, test it with a **text-stable round trip**:

```csharp
var written = Render(spec);
var read    = Parse(written);
Assert.Equal(written, Render(read));
```

Not tree equality - reading legitimately folds away nesting the text never carried. Add a
cross-target check as well: render the query to SQL before and after the round trip and require the
two to be identical. That is what proves nothing was lost, rather than that the same lossy
transformation ran twice.

## Releasing

Maintainers only. A release is a tag: MinVer reads `vX.Y.Z` and packs that version.

```sh
# after updating CHANGELOG.md and CHANGELOG.ru.md
git tag v0.1.0
git push origin v0.1.0
```

`release.yml` packs, publishes to NuGet through Trusted Publishing, and creates the GitHub release.
While on 0.x, a breaking change is a minor bump, never a patch.

## Code of conduct

Participation is covered by the [Code of Conduct](CODE_OF_CONDUCT.md).
