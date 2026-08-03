# Querio.Sql

Renders a [Querio](https://www.nuget.org/packages/Querio/) query into parameterized SQL.

```csharp
var result = SqlRenderer.Render(spec, schema, SqlServerDialect.Instance);
result.Sql;          // SELECT TOP (20) [r].[route], COUNT(*) AS [total] FROM [dbo].[RequestLog] ...
result.Parameters;   // @p0 = True
```

Three dialects - `SqlServerDialect`, `PostgreSqlDialect`, `SqliteDialect` - each declaring what it
cannot do rather than approximating it. SQLite has no percentile; SQL Server refuses a grouped one,
because `PERCENTILE_CONT` there is a window function and cannot be combined with `GROUP BY`.

No value ever reaches the SQL text. Identifiers and LIKE patterns are escaped per dialect.

It opens no connection and references no driver: pair it with Dapper, ADO.NET, or whatever you
already use.

Targets `netstandard2.0`, `net8.0`, `net9.0`, `net10.0`. Depends only on `Querio`.

[Documentation](https://github.com/jrfrigat/Querio) - [MIT](https://github.com/jrfrigat/Querio/blob/main/LICENSE)
