using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Querio;

namespace Querio.TrimmingProbe;

/// <summary>
/// Reads the corpus back from inside a trimmed, self-contained publish, by both of the routes a
/// consumer can take.
/// <para>
/// This is the one check that cannot be a unit test. The spec types are positional records, so a
/// serializer matches JSON to constructor parameters by name - and the trimmer strips parameter
/// names from anything nothing appears to reflect on. Querio ships an ILLink descriptor to stop
/// that, but a descriptor that stopped working would break no build, fail no test and pass every
/// Debug run: the failure appears only in a published application, at run time, as
/// <c>ConstructorContainsNullParameterNames</c>, in somebody else's product.
/// </para>
/// <para>
/// Both routes are exercised because both are real. Reflection is what a consumer gets by writing
/// the obvious code, and needs the descriptor to survive. The source generator is what a consumer
/// reaches for when trimming warnings matter, and needs the shape to be describable without
/// reflection at all. A model that only worked one way would be a model with a deployment footnote.
/// </para>
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions Reflection = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions Generated = new(CorpusContext.Default.Options)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        TypeInfoResolver = CorpusContext.Default,
    };

    private static int Main()
    {
        var documents = Corpus();
        if (documents.Count == 0)
        {
            Console.Error.WriteLine("No corpus documents were embedded, so this proved nothing.");
            return 2;
        }

        var failures = Check("reflection", documents, Reflection) + Check("source generator", documents, Generated);

        if (failures > 0)
        {
            Console.Error.WriteLine(
                $"{failures} checks failed inside a trimmed publish. The ILLink descriptor in "
                + "src/Querio is what keeps the constructor parameter names a serializer matches on; "
                + "check it still ships and still names this assembly.");
            return 1;
        }

        Console.WriteLine(
            $"All {documents.Count} corpus documents round-tripped inside a trimmed publish, "
            + "by reflection and by the source generator.");
        return 0;
    }

    private static int Check(string route, IReadOnlyDictionary<string, string> documents, JsonSerializerOptions options)
    {
        var failures = 0;
        foreach (var (name, stored) in documents)
        {
            try
            {
                var restored = JsonSerializer.Deserialize<QuerySpec>(stored, options)
                    ?? throw new InvalidOperationException("deserialized to null");

                var written = JsonSerializer.Serialize(restored, options)
                    .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";

                if (!string.Equals(stored, written, StringComparison.Ordinal))
                {
                    failures++;
                    Console.Error.WriteLine($"[{route}] {name}: survived trimming but came back different.");
                    continue;
                }

                Console.WriteLine($"[{route}] {name}: ok");
            }
            catch (Exception error)
            {
                failures++;
                Console.Error.WriteLine($"[{route}] {name}: {error.GetType().Name}: {error.Message}");
            }
        }

        return failures;
    }

    private static IReadOnlyDictionary<string, string> Corpus()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var documents = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith("corpus.", StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            documents[name] = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        return documents;
    }
}

/// <summary>
/// The reflection-free description of the model, which the source generator builds at compile time.
/// That it compiles at all is half the point: a shape needing runtime reflection to describe could
/// not be generated, and would leave a trimmed consumer with no route at all.
/// </summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(QuerySpec))]
internal sealed partial class CorpusContext : JsonSerializerContext;
