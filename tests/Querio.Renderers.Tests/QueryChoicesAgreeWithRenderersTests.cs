using Querio.Tests;

namespace Querio.Renderers.Tests;

/// <summary>
/// <see cref="QueryChoices"/> narrows what a builder may offer next by reading the target's
/// <see cref="IQueryCapabilities"/>, so that a designer never suggests something that will throw
/// once rendered. Nothing checked that the narrowing and the renderers actually agree.
/// <para>
/// This does: for every target, everything the choices offer is applied and rendered. A feature a
/// renderer refuses while the choices still offer it is a broken designer - the user picks it, and
/// the failure arrives at render time, which is exactly the discovery-in-production the capability
/// model exists to prevent.
/// </para>
/// <para>
/// The direction matters. This proves the offers are not too generous; it says nothing about them
/// being too stingy, which is a poorer designer but not a broken one.
/// </para>
/// </summary>
public sealed class QueryChoicesAgreeWithRenderersTests
{
    private static readonly QuerySchema Schema = TestSchema.Build();

    public static TheoryData<string> TargetNames()
    {
        var data = new TheoryData<string>();
        foreach (var target in RenderTargets.All)
        {
            data.Add(target.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(TargetNames))]
    public void NothingTheChoicesOfferIsRefusedAtRenderTime(string name)
    {
        var target = RenderTargets.All.Single(candidate => candidate.Name == name);

        var refused = new List<string>();
        var probes = 0;
        foreach (var (what, spec) in QueryProbes.For(target.Capabilities))
        {
            probes++;
            try
            {
                target.Render(spec, Schema);
            }
            catch (Exception error) when (error is QueryRenderException or QueryValidationException)
            {
                refused.Add($"{what}: {error.GetType().Name}: {error.Message}");
            }
        }

        // A probe list that silently emptied would pass while testing nothing.
        Assert.True(probes > 50, $"Only {probes} probes were generated for {name}; the generator has stopped covering the model.");
        Assert.True(refused.Count == 0, $"{name} refuses {refused.Count} of {probes} offered choices:\n  " + string.Join("\n  ", refused));
    }

    /// <summary>
    /// The other direction. A target that refines a coarse feature - SQLite withdrawing the quarter,
    /// SQL Server withdrawing a grouped percentile - has to be telling the truth about what it
    /// withdrew: rendering it must genuinely fail.
    /// <para>
    /// Without this, a refinement is a free way to make the agreement test pass. Withdrawing
    /// something that in fact works costs the user a feature nobody ever notices is missing, which is
    /// a quieter failure than the render error it was meant to prevent.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(TargetNames))]
    public void AWithdrawnChoiceIsOneTheTargetReallyRefuses(string name)
    {
        var target = RenderTargets.All.Single(candidate => candidate.Name == name);
        var timestamp = new QueryFieldRef("r", "timestamp");
        var duration = new QueryFieldRef("r", "durationMs");
        var withdrawn = 0;

        if (target.Capabilities is IQueryPeriodCapabilities periods)
        {
            foreach (var period in Enum.GetValues<QueryDateTruncation>().Where(period => !periods.SupportsPeriod(period)))
            {
                withdrawn++;
                var spec = new QuerySpec(new QuerySource("requests", "r"))
                {
                    Select = [new QuerySelect { Field = timestamp, Truncate = period, Alias = "bucket" }],
                    GroupBy = [new QueryGroupBy(timestamp) { Truncate = period }],
                };
                Assert.Throws<QueryRenderException>(() => target.Render(spec, Schema));
            }
        }

        if (target.Capabilities is IQueryAggregateCapabilities aggregates)
        {
            foreach (var aggregate in Enum.GetValues<QueryAggregate>())
            {
                foreach (var grouped in new[] { false, true })
                {
                    if (aggregates.SupportsAggregate(aggregate, grouped))
                    {
                        continue;
                    }

                    withdrawn++;
                    Assert.Throws<QueryRenderException>(() => target.Render(Aggregated(aggregate, duration, grouped), Schema));
                }
            }
        }

        // Not every target refines anything; those simply have nothing to answer for here.
        Assert.True(withdrawn >= 0);
    }

    private static QuerySpec Aggregated(QueryAggregate aggregate, QueryFieldRef field, bool grouped)
    {
        var route = new QueryFieldRef("r", "route");
        var computed = new QuerySelect
        {
            Field = field,
            Aggregate = aggregate,
            Alias = "value",
            Percentile = aggregate == QueryAggregate.Percentile ? 0.95 : null,
        };

        return grouped
            ? new QuerySpec(new QuerySource("requests", "r"))
            {
                Select = [new QuerySelect { Field = route, Alias = "route" }, computed],
                GroupBy = [new QueryGroupBy(route)],
            }
            : new QuerySpec(new QuerySource("requests", "r")) { Select = [computed] };
    }
}
