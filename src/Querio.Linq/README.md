# Querio.Linq

Executes a [Querio](https://www.nuget.org/packages/Querio/) query as .NET code, by building real
expression trees rather than text.

```csharp
// Hand Entity Framework something it can translate
var predicate = QueryPredicate.For<RequestRow>(spec, schema);
var rows = dbContext.Requests.Where(predicate).ToList();

// Or run the whole query over objects
var result = QueryExecutor.Execute(spec, schema, new QuerySources().Add("requests", requests));
```

**No Entity Framework dependency, and none needed.** `System.Linq.Expressions` is part of the base
class library, and EF consumes those trees directly.

The trees are real operators rather than calls into an interpreter: a fixed value is narrowed to the
member's CLR type while the query is built, and set membership becomes a single `List<T>.Contains`
node instead of a chain of equality tests, so a provider can translate all of it.

Right and full outer joins are declared unsupported rather than approximated - a sequence of objects
has no natural shape for them. Aggregates over no rows yield null, matching what a store returns.

Functions a schema declares get their implementation from `QueryFunctionLibrary`; calling one nobody
implemented throws. A schema says a function exists, never what it does.

Targets `netstandard2.0`, `net8.0`, `net9.0`, `net10.0`. Depends only on `Querio`.

[Documentation](https://github.com/jrfrigat/Querio) - [MIT](https://github.com/jrfrigat/Querio/blob/main/LICENSE)
