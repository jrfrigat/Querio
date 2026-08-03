# Changelog

All notable changes to Querio are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/). While on 0.x a breaking change is a minor bump, never a
patch.

The version comes from the git tag: MinVer reads `vX.Y.Z` and packs that. Tagging is the release.

## [Unreleased]

## [0.0.1] - 2026-08-03

First public release. The model and every target are complete and tested; the version stays 0.x
because the serialized shape of `QuerySpec` has not been through a real consumer yet, and a saved
query is a contract worth changing while changing it is still cheap.

### Added

**`Querio`** - the core.
- `QuerySchema`: entities, fields, foreign keys and declared functions (value and table kinds), with
  logical names separated from the physical ones so a query outlives a rename.
- `QuerySpec`: a serializable query - sources, joins with mandatory aliases, aggregates including
  percentiles, grouping with date truncation, grouping filters, sorting, paging.
- `QueryBuilder`: a fluent way to compose one.
- `QueryValidator`: dialect-free checking. A join may only attach to a participant declared before
  it, so a self-relation can never vouch for itself.
- `QueryChoices`: what the query may be given next - fields, joins, operators, aggregates, sort
  targets - narrowed by `IQueryCapabilities` so nothing is offered that would only fail once
  rendered.
- `QueryRenderer<TExpression>`: the shared walk every target renders through, with `TExpression`
  open so a target need not produce text at all.
- `QueryFeature` / `IQueryCapabilities` / `QueryRenderException`: a target declares what it cannot
  express and fails loudly instead of approximating.

**`Querio.Sql`** - parameterized SQL for SQL Server, PostgreSQL and SQLite. Each dialect declares
its own limits: SQLite has no percentile, SQL Server refuses a grouped one rather than pretending.
Names are escaped, LIKE patterns are escaped, and no value is ever spliced into the text.

**`Querio.OneC`** - the 1C query language. Cyrillic keywords, `&p0` parameters, and identifiers
refused rather than escaped, because 1C cannot quote a name.

**`Querio.Linq`** - real expression trees. `QueryPredicate.For<T>` hands EF Core something it can
translate; `QueryExecutor.Execute` runs the whole query over objects. Right and full outer joins
are declared unsupported rather than approximated.

**`Querio.Http`** - a query string, both directions. Round-trips exactly, so a query survives a
link, a bookmark or a request from a client.

**`Querio.Text`** - a query as a sentence, both directions, with every connective swappable so a
description can be written and read back in another language.

**`Querio.Language`** - SQL-shaped text with foreign-key navigation (`[r].[apiKeyId].[ownerId].[name]`),
which no SQL can write. Reports every problem with its exact span rather than stopping at the first,
and returns the partial query alongside, so an editor stays useful while the text is still wrong.
`QueryCompletion` answers what may be typed next.

### Notes

- No shipped package takes a third-party dependency; a test enforces it for every one of them.
- Targets `netstandard2.0`, `net8.0`, `net9.0`, `net10.0`.
- The assembly ships an ILLink descriptor: the spec types are positional records, so a trimmed
  publish would otherwise strip the constructor parameter names a serializer matches on, and a
  saved query would stop being readable - a failure that only appears in a Release publish.

[Unreleased]: https://github.com/jrfrigat/Querio/compare/v0.0.1...HEAD
[0.0.1]: https://github.com/jrfrigat/Querio/releases/tag/v0.0.1
