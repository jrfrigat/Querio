# Querio.OneC

Renders a [Querio](https://www.nuget.org/packages/Querio/) query into the 1C query language.

```csharp
var result = OneCRenderer.Render(spec, schema);
result.Query;        // Cyrillic keywords
result.Parameters;   // &p0 style
```

1C is the target that keeps the whole model honest: its query language is not SQL-shaped, which is
why Querio describes meaning rather than syntax.

It declares what it cannot express - percentiles, `OFFSET`, cross joins, table functions - and
throws `QueryRenderException` rather than answering a narrower question. 1C cannot quote an
identifier at all, so an invalid name is refused rather than escaped: there is no escaping to do.

Relative time windows are resolved to a parameter at render time, since 1C has no `DATEADD`
equivalent. The query itself still stores the offset, so it stays relative everywhere else.

**This target is tested differently from the SQL ones, and it is worth knowing how.** The generated
1C text is asserted against expected output; it has never been executed. The SQL dialects are also
run against real SQL Server, PostgreSQL and SQLite instances, which catches text that reads
correctly and is nevertheless rejected - or accepted and read differently. Covering 1C that way
needs a 1C instance, which CI has no way to provide, so the confidence here is honestly lower than
for SQL. Treat a first run against your own configuration as part of adopting it.

Targets `netstandard2.0`, `net8.0`, `net9.0`, `net10.0`. Depends only on `Querio`.

[Documentation](https://github.com/jrfrigat/Querio) - [MIT](https://github.com/jrfrigat/Querio/blob/main/LICENSE)
