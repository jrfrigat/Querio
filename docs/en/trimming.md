# Publishing trimmed, and reading a saved query back

Querio is trim-friendly, and there is one thing you have to do about it. This page is short because
the answer is one property or one attribute - but skipping it fails at run time in a published
application, which is the worst place to find out.

## The situation

A `QuerySpec` is a data contract: consumers store saved reports as JSON and read them back later,
often on a newer build. The spec types are positional records, so a serializer matches JSON
properties to **constructor parameter names**. Two separate things in a trimmed publish get in the
way of that, and they are easy to confuse:

1. **The trimmer strips parameter names** from anything nothing appears to reflect on. Querio handles
   this itself: `src/Querio/ILLink.Descriptors.xml` ships inside the package and tells the trimmer to
   keep the assembly whole. You do not have to do anything about this one.
2. **`PublishTrimmed` turns off `System.Text.Json`'s reflection path entirely**, before it ever looks
   at a type. This one is yours to answer, because it is a property of your application, not of
   Querio.

Without an answer to the second, deserializing throws:

```
InvalidOperationException: Reflection-based serialization has been disabled for this application.
```

## Answer A: keep reflection on

One property in the application's project file:

```xml
<PropertyGroup>
  <PublishTrimmed>true</PublishTrimmed>
  <JsonSerializerIsReflectionEnabledByDefault>true</JsonSerializerIsReflectionEnabledByDefault>
</PropertyGroup>
```

Then the obvious code works unchanged:

```csharp
var options = new JsonSerializerOptions
{
    Converters = { new JsonStringEnumConverter() },
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

var spec = JsonSerializer.Deserialize<QuerySpec>(json, options);
```

This is the smaller change and the one to reach for first. It costs some of what trimming would have
saved, and it does not work under Native AOT.

## Answer B: use the source generator

Describe the model at compile time instead. This needs no reflection at all, produces no trim
warnings, and works under Native AOT:

```csharp
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(QuerySpec))]
internal sealed partial class QueryJson : JsonSerializerContext;
```

```csharp
var options = new JsonSerializerOptions(QueryJson.Default.Options)
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    TypeInfoResolver = QueryJson.Default,
};

var spec = JsonSerializer.Deserialize<QuerySpec>(json, options);
```

Declaring `QuerySpec` is enough; the generator walks the rest of the model from there.

## How this is kept true

`tests/Querio.TrimmingProbe` is a console application published trimmed and self-contained, carrying
the checked-in corpus from `tests/Querio.Tests/Corpus/` inside the binary. It deserializes every
document and writes it back out, comparing byte for byte, **both ways** - reflection and the source
generator - and returns a failing exit code if any document does not survive.

The `trimming` job in CI publishes and runs it on every push. Unit tests assert the ILLink descriptor
is present; this asserts it does its job, which is a different question and the one that matters.

## Enums, and why they are written by name

Both examples above configure enums as strings. That is not decoration: a stored query that wrote
`2` for an operator would change meaning the day somebody inserted a value into the middle of the
enum. Writing `GreaterThan` costs a few bytes and survives the model growing.
