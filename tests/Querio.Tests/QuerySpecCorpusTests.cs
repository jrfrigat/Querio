using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Querio.Tests;

/// <summary>
/// The corpus: a set of <see cref="QuerySpec"/> documents checked into <c>Corpus/</c> and read back
/// from disk.
/// <para>
/// This is the test the other serialization tests cannot be. They build a spec in code, write it and
/// read it back, all on one build - so they prove the writer and the reader agree with each other,
/// which they would even if both had drifted together and stopped reading anything saved last year.
/// The documents here were written once and are never regenerated casually; a change that stops them
/// loading is a change that breaks somebody's stored reports, and it fails here instead of there.
/// </para>
/// <para>
/// To change the format deliberately, set <c>QUERIO_CORPUS_REGENERATE=1</c> and run the tests once:
/// the files are rewritten, and the resulting diff is the compatibility break, stated in full and
/// ready to be reviewed. Never regenerate to make a red test go green without reading that diff.
/// </para>
/// </summary>
public sealed class QuerySpecCorpusTests
{
    // The options a consumer needs for the shape, and no more: enums by name so a stored document
    // survives a reordering of the enum, and nulls omitted so an absent member is absent rather than
    // explicitly null. Indented because these files are reviewed as diffs.
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static TheoryData<string> DocumentNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in CorpusDocuments.All.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    /// <summary>
    /// The case that matters: a document written by an earlier build still loads, and means the same
    /// thing when written back out. Byte-for-byte, because "close enough" is how a member quietly
    /// stops being persisted.
    /// </summary>
    [Theory]
    [MemberData(nameof(DocumentNames))]
    public void ADocumentOnDiskStillLoadsAndSaysTheSame(string name)
    {
        var stored = ReadDocument(name);

        var restored = JsonSerializer.Deserialize<QuerySpec>(stored, Options);

        Assert.NotNull(restored);
        Assert.Equal(stored, Canonical(restored));
    }

    /// <summary>
    /// The corpus describes the model as it stands. When this fails, the model changed shape: read
    /// the diff, decide whether stored documents survive it, and regenerate deliberately.
    /// </summary>
    [Theory]
    [MemberData(nameof(DocumentNames))]
    public void TheCorpusMatchesTheModel(string name)
    {
        var current = Canonical(CorpusDocuments.All[name]);

        if (ShouldRegenerate)
        {
            File.WriteAllText(PathTo(name), current);
            return;
        }

        Assert.Equal(ReadDocument(name), current);
    }

    /// <summary>
    /// Every enum the spec can reach has every one of its values somewhere in the corpus. This is
    /// what keeps the corpus honest as the model grows: adding a join kind, an operator or an
    /// aggregate and leaving it uncovered fails here rather than being discovered by the first
    /// consumer who saved one.
    /// </summary>
    [Fact]
    public void TheCorpusCoversEveryValueOfEveryEnumTheSpecCanReach()
    {
        var used = new HashSet<Enum>();
        foreach (var document in CorpusDocuments.All.Values)
        {
            Collect(document, used, []);
        }

        var reachable = ReachableEnums(typeof(QuerySpec), []);

        // A coverage guard that reaches nothing passes every time and guards nothing, so the walker
        // has to prove it still walks. These are the enums a stored query is made of; the floor
        // catches the walker breaking, and the model is still what supplies the actual requirement.
        Assert.Contains(typeof(QueryOperator), reachable);
        Assert.Contains(typeof(QueryOperandKind), reachable);
        Assert.Contains(typeof(QueryJoinKind), reachable);
        Assert.Contains(typeof(QueryAggregate), reachable);
        Assert.Contains(typeof(QueryDateTruncation), reachable);
        Assert.Contains(typeof(QueryTimeUnit), reachable);
        Assert.Contains(typeof(QuerySortDirection), reachable);
        Assert.Contains(typeof(QuerySourceKind), reachable);

        var missing = new List<string>();
        foreach (var type in reachable.OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            foreach (var value in Enum.GetValues(type).Cast<Enum>())
            {
                if (!used.Contains(value))
                {
                    missing.Add($"{type.Name}.{value}");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            $"The corpus never stores: {string.Join(", ", missing)}. Add a document that does.");
    }

    /// <summary>
    /// The forward-compatibility decision, stated as a test rather than left to whoever reads the
    /// docs: a document carrying a member this build does not know is <b>ignored</b>, which is what
    /// makes adding an optional member a safe change.
    /// <para>
    /// Querio cannot enforce this. It references no serializer - that is what keeps it dependency
    /// free - so the behaviour belongs to the consumer's options, and the second half of this test
    /// shows the consumer can choose the opposite. What Querio owns is the promise the shape is
    /// designed around, and that promise is: unknown members are ignored, existing ones are never
    /// repurposed.
    /// </para>
    /// </summary>
    [Fact]
    public void AnUnknownMemberIsIgnoredRatherThanFatal()
    {
        var stored = ReadDocument("minimal")
            .Replace("\"Distinct\": false", "\"Distinct\": false,\n  \"SomethingFromLater\": 42", StringComparison.Ordinal);
        Assert.Contains("SomethingFromLater", stored, StringComparison.Ordinal);

        var restored = JsonSerializer.Deserialize<QuerySpec>(stored, Options);

        Assert.NotNull(restored);
        Assert.Equal("r", restored.From.Alias);

        // The same document, refused - because a consumer that would rather fail loudly can say so.
        var strict = new JsonSerializerOptions(Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<QuerySpec>(stored, strict));
    }

    private static bool ShouldRegenerate
        => Environment.GetEnvironmentVariable("QUERIO_CORPUS_REGENERATE") == "1";

    private static string ReadDocument(string name) => Normalize(File.ReadAllText(PathTo(name)));

    // One canonical form, used for writing and for both comparisons, so a trailing newline cannot
    // become the difference between a document that matches and one that does not.
    private static string Canonical(QuerySpec spec)
        => Normalize(JsonSerializer.Serialize(spec, Options)) + "\n";

    // Off the source file rather than the output directory, so regeneration writes the checked-in
    // file instead of a copy under bin/ that nobody would notice was stale.
    private static string PathTo(string name, [CallerFilePath] string caller = "")
        => Path.Combine(Path.GetDirectoryName(caller)!, "Corpus", name + ".json");

    // git checks these out with the platform's line endings, so a byte-for-byte comparison has to
    // agree on which bytes those are.
    private static string Normalize(string json) => json.Replace("\r\n", "\n", StringComparison.Ordinal);

    // Walks the instance graph and records every enum value actually stored.
    private static void Collect(object? value, HashSet<Enum> used, HashSet<object> seen)
    {
        switch (value)
        {
            case null:
                return;
            case Enum stored:
                used.Add(stored);
                return;
            case string:
                return;
            case IEnumerable items:
                foreach (var item in items)
                {
                    Collect(item, used, seen);
                }

                return;
        }

        var type = value.GetType();
        if (type.Namespace != "Querio" || !seen.Add(value))
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length == 0)
            {
                Collect(property.GetValue(value), used, seen);
            }
        }
    }

    // Walks the type graph the same way, so the requirement is derived from the model rather than
    // from a list somebody has to remember to extend.
    private static HashSet<Type> ReachableEnums(Type type, HashSet<Type> seen)
    {
        var found = new HashSet<Type>();
        if (!seen.Add(type))
        {
            return found;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var member = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (member.IsEnum)
            {
                found.Add(member);
            }
            else if (member.Namespace == "Querio")
            {
                found.UnionWith(ReachableEnums(member, seen));
            }
            else if (member.IsGenericType && typeof(IEnumerable).IsAssignableFrom(member))
            {
                foreach (var argument in member.GetGenericArguments())
                {
                    var element = Nullable.GetUnderlyingType(argument) ?? argument;
                    if (element.IsEnum)
                    {
                        found.Add(element);
                    }
                    else if (element.Namespace == "Querio")
                    {
                        found.UnionWith(ReachableEnums(element, seen));
                    }
                }
            }
        }

        return found;
    }
}
