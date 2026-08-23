using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Querio;
using Querio.Language;
using Querio.Renderers.Tests;
using Querio.Tests;

namespace Querio.Benchmarks;

/// <summary>
/// What Querio costs. Rendering sits on the interactive path of anything built on this - an editor
/// re-reads its text on every keystroke - so "it feels fine" is not a number, and a regression that
/// nobody measured is one nobody notices until a designer starts to lag.
/// <para>
/// Run with <c>dotnet run -c Release --project tests/Querio.Benchmarks</c>. The numbers this repo
/// records live in <c>docs/benchmarks.md</c>; regenerate them there when the answer changes.
/// </para>
/// </summary>
internal static class Program
{
    private static void Main(string[] args)
    {
        // Off the source path rather than the working directory: BenchmarkDotNet otherwise drops a
        // results folder wherever it was invoked from, which for this repo means the root.
        var config = DefaultConfig.Instance.WithArtifactsPath(ArtifactsPath());
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }

    private static string ArtifactsPath([CallerFilePath] string caller = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(caller)!, "..", "..", "artifacts", "benchmarks"));
}

/// <summary>
/// A query shaped like one somebody would actually save: a join, a filter with a relative window,
/// a time series bucketed by day, and a bounded result.
/// </summary>
internal static class Representative
{
    internal static QuerySchema Schema { get; } = TestSchema.Build();

    /// <summary>
    /// Deliberately within what every target can express - no percentile, no table function, no
    /// outer join - so one spec can be timed against all of them and the numbers compare.
    /// </summary>
    internal static QuerySpec Spec { get; } = QueryBuilder.From(Schema, "requests", "r")
        .Join("apiKeys", "a")
        .SelectPeriod("r", "timestamp", QueryDateTruncation.Day, "day")
        .Select("a", "name", "key")
        .CountRows("total")
        .Sum("r", "durationMs", "totalMs")
        .Where(f => f
            .Since("r", "timestamp", 30, QueryTimeUnit.Day)
            .Equal("r", "error", true)
            .In("r", "status", new[] { 500, 502, 503 })
            .AnyOf(any => any.Contains("r", "route", "/api").StartsWith("r", "route", "/v1")))
        .GroupByPeriod("r", "timestamp", QueryDateTruncation.Day)
        .GroupBy("a", "name")
        .Having(h => h.SelectGreaterThan("total", 10))
        .OrderBySelectDescending("total")
        .Limit(100)
        .Build();

    internal static string LanguageText { get; } = QueryLanguage.Write(Spec, Schema);
}

/// <summary>Rendering one saved query, once per target.</summary>
[MemoryDiagnoser]
public class RenderBenchmarks
{
    private RenderTarget _target = null!;

    /// <summary>Every target the tests cover, so a new one is measured the day it is added.</summary>
    public static IEnumerable<string> TargetNames => RenderTargets.All.Select(target => target.Name);

    /// <summary>The target being measured.</summary>
    [ParamsSource(nameof(TargetNames))]
    public string Target { get; set; } = string.Empty;

    /// <summary>Resolves the target once, so the lookup is not part of the measurement.</summary>
    [GlobalSetup]
    public void Setup() => _target = RenderTargets.All.Single(target => target.Name == Target);

    /// <summary>Renders the representative query.</summary>
    [Benchmark]
    public void Render() => _target.Render(Representative.Spec, Representative.Schema);
}

/// <summary>The rest of the path a designer runs on every edit.</summary>
[MemoryDiagnoser]
public class ModelBenchmarks
{
    /// <summary>Checking a query is coherent, which every renderer does before it renders.</summary>
    [Benchmark]
    public bool Validate() => Representative.Spec.Validate(Representative.Schema).IsValid;

    /// <summary>Working out what the query may be given next - the answer an editor needs per keystroke.</summary>
    [Benchmark]
    public int Choices()
        => QueryChoices.For(Representative.Spec, Representative.Schema).Fields.Count;

    /// <summary>Reading the query language, which an editor does on every keystroke.</summary>
    [Benchmark]
    public QuerySpec ParseLanguage()
        => QueryLanguage.Parse(Representative.LanguageText, Representative.Schema);

    /// <summary>Writing it back out.</summary>
    [Benchmark]
    public string WriteLanguage()
        => QueryLanguage.Write(Representative.Spec, Representative.Schema);
}
