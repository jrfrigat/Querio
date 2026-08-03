# Working with AI agents

Querio is a good fit for letting a model build queries, for one reason: **the model never writes
query text**. It picks from a closed set of choices, and what comes out is an object something else
validates and renders. There is no string to inject into.

## The shape that works

Do not ask a model for SQL. Ask it to choose, one step at a time, from what `QueryChoices` says is
available.

```csharp
var choices = QueryChoices.For(spec, schema, SqlServerDialect.Instance);

choices.Fields;                 // what may be selected
choices.Joins;                  // what may be brought in, and through which relation
choices.OperatorsFor(field);    // narrowed to what the target can do
choices.ValueKindsFor(field);   // literal, set, another field, a relative moment, a function call
choices.SortTargets;
```

Every list is already narrowed by the schema *and* by the target, so a model cannot pick something
that fails later. Each operator also reports its `QueryValueArity` - `None`, `One`, `Two`, `List` -
so a tool definition knows how many values to ask for without that knowledge being hard-coded.

## As a tool definition

`QueryChoices` maps onto tool parameters directly:

```csharp
// enum of what may be selected right now
var fields = choices.Fields.Select(f => f.ToString());          // "r.route", "r.timestamp", ...

// enum of operators valid for the field the model just picked
var operators = choices.OperatorsFor(field).Select(o => o.Operator.ToString());
```

Regenerate the tool schema after each step and the model is choosing from a menu that is correct by
construction. That is worth more than any amount of prompting about which columns exist.

## Letting a model write text

If you would rather have the model write the query language, read it with `QueryLanguage.Read` and
hand the diagnostics back:

```csharp
var result = QueryLanguage.Read(modelOutput, schema);
if (!result.IsValid)
{
    // Every problem at once, each with its exact span - a far better correction prompt than
    // "syntax error near line 3".
    var report = string.Join("\n", result.Diagnostics.Select(d =>
        $"{d.Message} (at {d.Start}..{d.End})"));
}
```

The reader deliberately returns every fault rather than the first, and returns the partial query
alongside them, so one correction round usually suffices.

## Showing a query for approval

Before running anything a model composed, put it in front of a person in words:

```csharp
QueryDescriber.Describe(spec, schema);
// From Requests (r), showing Route and the number of rows called total,
// where (Timestamp is at least the last 30 days and Error is "true"), grouped by Route, first 20
```

The description uses the labels the schema declares, not storage names, so it reads the way the
person thinks about their data. It also reads back, so an approval step can round-trip the exact
query that was shown.

## What Querio does not decide

**Authorisation.** Querio renders what the schema says. Which entities and fields a given user - or
a given agent - may query is the host application's job, and the natural place to enforce it is the
schema you hand to `QueryChoices`: build a narrowed `QuerySchema` per caller and the choices narrow
with it.

**Execution.** Querio opens no connections. Whatever runs the SQL decides on timeouts, row caps and
which credentials are used. A model choosing a query is not a model touching a database.
