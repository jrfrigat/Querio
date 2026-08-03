# Querio.Language

Reads and writes a [Querio](https://www.nuget.org/packages/Querio/) query as SQL-shaped text, with
completion for an editor.

```sql
select [r].[route], [r].[apiKeyId].[name] as [key], count(*) as [total]
from [dbo].[RequestLog] as [r]
where [r].[timestamp] >= now - 30 day and [r].[error] = true
group by [r].[route], [r].[apiKeyId].[name]
order by [total] desc
limit 20
```

It looks like SQL because that is what people already know, and it is not SQL: `[r].[apiKeyId].[name]`
walks a **foreign key**, to any depth. Each hop becomes a join, which the target then renders however
suits it. The joins are outer, because travelling a key that happens to be empty must not make the
row disappear.

Nothing stops at the first mistake. Every problem comes back with the exact span it occupies, plus
whatever query could still be built from the rest - which is what an editor needs while somebody is
halfway through a line:

```csharp
var result = QueryLanguage.Read(text, schema);
result.Spec;         // the partial query
result.Diagnostics;  // every problem, each with Start/Length/Message

QueryCompletion.Suggest(text, caret, schema);   // what may be written next
```

Entity and field names are accepted both logically and physically, and the spec stores the logical
one, so the text stays portable.

Targets `netstandard2.0`, `net8.0`, `net9.0`, `net10.0`. Depends only on `Querio`.

[Documentation](https://github.com/jrfrigat/Querio) - [MIT](https://github.com/jrfrigat/Querio/blob/main/LICENSE)
