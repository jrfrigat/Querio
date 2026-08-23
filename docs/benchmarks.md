# Benchmarks

Rendering sits on the interactive path of anything built on Querio - a designer re-reads its text on
every keystroke - so the cost is worth a number rather than an impression. These are recorded here so
a regression is visible as a diff instead of as a report that the editor "feels sluggish now".

Regenerate with:

```bash
dotnet run -c Release --project tests/Querio.Benchmarks
```

Results land in `artifacts/benchmarks/` (gitignored). Paste the two tables below when the answer
changes, and say what changed it.

## What is measured

One query, shaped like a saved report: a join, a filter carrying a relative window and a set test, a
time series bucketed by day, a grouping filter, and a row cap. It stays inside what every target can
express, so the same spec is timed against all of them and the numbers compare.

## Rendering, once per target

| Method | Target     | Mean         | Allocated |
|------- |----------- |-------------:|----------:|
| Render | HTTP       |     2.879 us |  11.12 KB |
| Render | Language   |     3.178 us |  13.15 KB |
| Render | Text       |     3.321 us |  15.07 KB |
| Render | 1C         |     3.480 us |  13.23 KB |
| Render | SQLite     |     4.190 us |  17.78 KB |
| Render | SQL Server |     4.213 us |  17.71 KB |
| Render | PostgreSQL |     4.228 us |  18.24 KB |
| Render | LINQ       | 1,189.646 us |   58.7 KB |

## The rest of the path a designer runs

| Method        | Mean       | Allocated |
|-------------- |-----------: |----------:|
| Validate      |   907.9 ns |   2.95 KB |
| Choices       | 1,235.3 ns |   7.45 KB |
| WriteLanguage | 3,129.1 ns |  13.15 KB |
| ParseLanguage | 5,132.9 ns |  24.89 KB |

## Reading these

**Every text target costs about the same, and that cost is microseconds.** Rendering a saved query to
SQL takes roughly four microseconds and allocates under twenty kilobytes. A designer that re-renders
on every keystroke spends about a quarter of a percent of a 60 Hz frame doing it, so rendering is not
what will make one feel slow.

**Parsing the query language costs more than writing it**, which is the expected direction: reading
has to tokenise, resolve names against the schema and report diagnostics, while writing already knows
what it holds. Five microseconds per keystroke is still far inside an editor's budget.

**LINQ is three orders of magnitude more expensive, and that is not a defect.** The other targets
produce text; this one builds an expression tree and compiles it, and compilation is the whole cost.
It is measured over empty sequences precisely so the number is the plan and not the data. The
practical reading is that a compiled plan is worth reusing across executions rather than rebuilt per
call - which is exactly what a caller holding an `IQueryable` ends up doing anyway.

**`QueryChoices` is about a microsecond**, so a builder may rebuild it after every edit without
caching. That was the design intent, and now it is a measurement.

## Environment

Numbers above were taken on:

```
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.400, .NET 10.0.11, X64 RyuJIT x86-64-v3
```

They are a baseline for spotting a change on comparable hardware, not a specification. Compare a
regression against a rerun on the same machine rather than against this file.
