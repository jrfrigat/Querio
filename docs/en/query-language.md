# The query language

`Querio.Language` reads a query written as text. It looks like SQL because that is what people
already know - and it is not SQL, which is the entire point.

```sql
select [r].[route], [r].[apiKeyId].[name] as [key], count(*) as [total]
from [dbo].[RequestLog] as [r]
where [r].[timestamp] >= now - 30 day and [r].[error] = true
group by [r].[route], [r].[apiKeyId].[name]
order by [total] desc
limit 20
```

## Walking a foreign key

`[r].[apiKeyId].[name]` is the difference. No SQL can write that: every name but the last is a key
to travel.

```csharp
var spec = QueryLanguage.Parse("select [r].[apiKeyId].[ownerId].[name] from [requests] as [r]", schema);
// spec.Joins -> apiKeys, then users hanging off it
```

Chains go as deep as you write them. Rules that matter:

- **Each hop becomes a join**, which the target renders however suits it - an explicit `JOIN` in
  SQL, a dotted reference in 1C.
- **The joins are outer**, deliberately. Travelling a key that happens to be empty must not make the
  row vanish, or the query quietly answers a narrower question than the one asked.
- **The same hop written twice produces one join.** Mention `[r].[apiKeyId].[name]` three times and
  there is still a single join.
- **A hop names either the field holding the key or the relation itself.** Naming the relation is
  the only way to travel a composite key, which has no single field to point at.

The sugar is spent once it is read: nothing records that a join arrived as a dot, so writing the
query back out shows the joins. Same query, different characters.

## Names

Entity and field names are accepted **both logically and physically**:

```sql
from [dbo].[RequestLog] as [r]     -- what you can see in the database
from [requests] as [r]             -- what the schema calls it
```

Either way the spec stores the logical name, so the text stays portable. Brackets are optional
around anything that is not a keyword; `]]` is a literal `]`, as in T-SQL.

## Grammar

```sql
select [distinct] <item> [, <item>]... | *
from <entity|function(...)> [as] <alias>
[<kind>] join <entity|function(...)> [as] <alias> (through <relation> | on <a.x> = <b.y> [and ...])
where <condition>
group by <value> [, <value>]...
having <condition>
order by <value|outputName> [asc|desc] [, ...]
limit <n> offset <n>
```

| | |
| --- | --- |
| Aggregates | `count(*)`, `count(distinct x)`, `sum(x)`, `avg(x)`, `min(x)`, `max(x)`, `percentile(x, 0.95)` |
| Periods | `trunc(x, day)` - also minute, hour, week, month, quarter, year |
| Relative time | `now - 30 day`, `now + 2 week` |
| Comparison | `=`, `<>`, `!=`, `>`, `>=`, `<`, `<=` |
| Text | `contains`, `startswith`, `endswith` |
| Sets and ranges | `in (...)`, `not in (...)`, `between a and b`, `not between a and b` |
| Null | `is null`, `is not null` |
| Join kinds | `inner`, `left`, `right`, `full`, `cross` |

A join with neither `through` nor `on` is inferred when exactly one declared relation reaches
something already in the query. Two candidates and it says so rather than guessing.

## Reading broken text

Text being typed is broken almost all of the time, so the reader never stops at the first fault:

```csharp
var result = QueryLanguage.Read(text, schema);

result.Spec;         // whatever query could be built - often not null even when the text is wrong
result.Diagnostics;  // every problem, each with Start, Length, Message, Severity
result.IsValid;
```

Each diagnostic carries the exact span it covers, which is what an editor underlines. The partial
spec matters just as much: it is what completion reads to keep making sense while the rest of the
line is still nonsense.

`QueryLanguage.Parse` is the strict door - a query or an exception - for callers that want one or
the other.

## Completion

```csharp
var candidates = QueryCompletion.Suggest(text, caret, schema);
```

Each candidate carries what to insert, what to show, what kind of thing it is, and the span it
replaces. Typing a dot after an alias offers both the fields reached so far and the keys that can be
travelled further:

```
id          field       Id
route       field       Route
apiKeyId    field       API key
apiKeyId    navigation  -> API keys
timestamp   field       Timestamp
```

Pass an `IQueryCapabilities` to narrow the answers to a target, the same way `QueryChoices` does.

## Writing text back

```csharp
var text = QueryLanguage.Write(spec, schema);
```

For opening a query built elsewhere - in code, or in a designer - in a text editor. Joins are
written out in full, including the ones a foreign-key path produced, for the reason above.
