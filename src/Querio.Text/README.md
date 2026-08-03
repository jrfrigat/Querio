# Querio.Text

Describes a [Querio](https://www.nuget.org/packages/Querio/) query in words, and reads the sentence
back into the query.

```csharp
QueryDescriber.Describe(spec, schema);
// From Requests (r), showing Route and the number of rows called total,
// where (Timestamp is at least the last 30 days and Error is "true"), grouped by Route, first 20
```

Useful for a report title, an audit entry, an accessible summary, or a confirmation step before
something runs. It reads the labels a person chose rather than the names a store uses.

Every connective is swappable, so a query can be described - and read back - in another language:

```csharp
var labels = QueryDescriptionLabels.Default with { From = "from", Showing = "showing" };
```

The description carries the whole query rather than a summary of it: aliases, joins and output names
are all there, and a value is quoted in the exact form the query stores it in. A description that
could not be read back would be a summary, not a translation.

Targets `netstandard2.0`, `net8.0`, `net9.0`, `net10.0`. Depends only on `Querio`.

[Documentation](https://github.com/jrfrigat/Querio) - [MIT](https://github.com/jrfrigat/Querio/blob/main/LICENSE)
