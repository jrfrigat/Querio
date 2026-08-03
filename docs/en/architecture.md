# Architecture

Querio has one idea behind it: **a query is a thing, not a string**. Everything else follows.

## The shape

```
                    Querio  (core)
                      |
   schema + spec + builder + validator + QueryChoices + QueryRenderer<T>
                      |
   +--------+---------+---------+---------+---------+---------+
   |        |         |         |         |         |         |
 .Sql     .OneC     .Text     .Linq     .Http   .Language
```

A star, not a chain. Every target depends on the core and on nothing else - not on each other, and
not on anything outside the base class library. That is checked by a test rather than left to
discipline, because the property is easy to lose one convenient reference at a time.

## Why the model is semantic, not SQL-shaped

Most query builders are SQL builders. They assume a join looks like `JOIN`, paging looks like
`LIMIT ... OFFSET`, a percentile is a function call. That holds until a backend disagrees.

The 1C query language disagrees about nearly all of it - different keywords, a different way to
reach a related field, no `OFFSET` at all. Supporting it was not a nice-to-have; it is the
constraint that forced the model to describe **meaning** instead of **syntax**. Four consequences,
each of which you can see in the types:

### Values travel as data

`QueryCondition.Value` is a `QueryOperand`, and a literal operand holds a string in
invariant-culture form - never a fragment of query text. Renderers turn that into a parameter.

```csharp
// what the model holds
new QueryCondition(new QueryFieldRef("r", "status"), QueryOperator.Equals)
{
    Value = QueryOperand.Literal("500")
}

// what SQL Server gets
// ... WHERE [r].[status] = @p0     with @p0 = 500
```

Nothing concatenates a value into a query, anywhere, in any target. That is not a security feature
bolted on; it is a consequence of the value never being text in the first place.

### Names are logical

`QueryEntity.Source`, `QueryField.Column` and `QueryFunction.Source` carry the physical names. A
query refers to `requests` and `apiKeyId`; the renderer emits `dbo.RequestLog` and `api_key_id`.
Point the same saved query at a store that named things differently and it still means what it
meant.

### Aggregates and periods are enums

`QueryAggregate.Percentile`, `QueryDateTruncation.Day`. Not `"PERCENTILE_CONT"`, not
`"date_trunc('day', ...)"`. Each target maps the enum onto whatever it actually has - and if it has
nothing, it says so rather than inventing something close.

### Relative time stays relative

`QueryOperand.Ago(30, QueryTimeUnit.Day)` stores the offset, not the moment it resolved to. A saved
"last 30 days" still means the last 30 days a year from now. SQL renders it as engine arithmetic
(`DATEADD`, `now() + INTERVAL`); 1C resolves it to a parameter at render time, because 1C has no
equivalent. Same query, two honest answers.

## Capabilities: failing loudly

`IQueryCapabilities` is how a target says what it cannot express. `QueryCapabilities.All.Without(...)`
builds one:

```csharp
public static IQueryCapabilities Capabilities { get; } = QueryCapabilities.All.Without(
    QueryFeature.Percentile,      // SQLite has none
    QueryFeature.TableFunctions);
```

Ask for one anyway and you get a `QueryRenderException` naming the feature. **This is the most
important design decision in the project.** An abstraction that quietly approximates - computing a
percentile as an average, dropping an `OFFSET` it cannot express - is worse than no abstraction,
because the difference surfaces in production rather than at the call site.

The same capabilities are read *forwards* by `QueryChoices`, so a builder never offers what would
only fail later:

```csharp
var choices = QueryChoices.For(spec, schema, SqliteDialect.Instance);
choices.AggregatesFor(durationField);   // no Percentile in the list
```

## The shared walk

`QueryRenderer<TExpression>` holds everything that is the same for every target: resolving
participants and field types, working out which side of a join a relation attaches to, composing
the condition tree, checking capabilities. A target supplies only what a node *means* for it:

```csharp
protected abstract TExpression Field(string alias, QueryField field);
protected abstract TExpression Literal(object? value, QueryFieldType type);
protected abstract TExpression Relative(QueryRelativeValue offset);
protected abstract TExpression Call(QueryFunction function, IReadOnlyList<TExpression> arguments);
protected abstract TExpression Comparison(TExpression left, QueryOperator op, QueryFieldType type,
                                          TExpression? right, TExpression? upper);
protected abstract TExpression Membership(TExpression left, QueryOperator op, IReadOnlyList<TExpression> values);
protected abstract TExpression Combine(bool or, IReadOnlyList<TExpression> parts);
```

`TExpression` is open deliberately. `Querio.Text` uses `string`. `Querio.Linq` uses
`System.Linq.Expressions.Expression`. A document-store target would use its own node type. Nothing
in the base assumes rendering produces text - which is the difference between a query model and a
SQL generator with extra steps.

## Validation is dialect-free

`QueryValidator` checks that a query is coherent against its schema: aliases resolve, fields exist,
a grouped query does not return an ungrouped column, a condition on a computed aggregate lives in
`HAVING` rather than `WHERE`. It knows nothing about any target - what a *particular* backend cannot
do is the capability check's job, and the two are kept apart on purpose.

One rule worth calling out, because it is subtle: joins are validated **one at a time**, growing the
set of reachable participants as it goes. A join may only attach to something declared before it.
Checking against every participant at once would let a self-relation vouch for itself -
`from requests join users on user_manager` would have validated, and meant nothing.

## Two-way targets

`Querio.Http` and `Querio.Text` read back what they wrote. That is what makes them safe to store a
query in - a link, a saved report, a sentence somebody approved.

The asymmetry is load-bearing:

- **Writing is total.** Every query can be written down.
- **Reading is partial.** Text can say things the model has no room for.

So a reader either recovers exactly what was written or **refuses and says where**, with a position
into the text. It never salvages the half it understood, because a parser that quietly dropped what
it did not understand would hand back a query that means something else.

`Querio.Language` reads but does not round-trip its own sugar: a foreign-key path
(`[r].[apiKeyId].[name]`) desugars into joins, and nothing records that a join was typed as a dot.
Writing that query back out shows the joins. Same query, different characters.

## Serialization

`QuerySpec` and everything under it are positional records with kind-tagged unions rather than type
hierarchies, so any serializer can round-trip them without custom converters:

```csharp
var json = JsonSerializer.Serialize(spec, new JsonSerializerOptions
{
    Converters = { new JsonStringEnumConverter() },
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
});
```

One consequence to know about: because the types are positional records, a serializer matches JSON
to **constructor parameter names**, and trimming strips those. The core ships an ILLink descriptor
keeping itself whole, plus tests that the descriptor is really embedded and that every constructor
parameter still lines up with the property it fills. Without it a trimmed publish throws
`ConstructorContainsNullParameterNames` - and only in Release, at run time, long after every test
has passed.
