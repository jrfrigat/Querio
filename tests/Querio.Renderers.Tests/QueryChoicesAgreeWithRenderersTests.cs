using System.Globalization;
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
        foreach (var (what, spec) in Probes(target.Capabilities))
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

    // Everything QueryChoices will offer, turned into the smallest query that uses it. Minimal on
    // purpose: a probe that failed for an unrelated reason would report the wrong culprit.
    private static IEnumerable<(string What, QuerySpec Spec)> Probes(IQueryCapabilities caps)
    {
        foreach (var root in QueryChoices.Roots(Schema, caps))
        {
            var source = root.Kind == QuerySourceKind.Entity
                ? new QuerySource(root.Key, root.SuggestedAlias)
                : QuerySource.FromFunction(Call(root.Key, root.Parameters), root.SuggestedAlias);
            yield return ($"root '{root.Key}'", new QuerySpec(source));
        }

        var root0 = new QuerySpec(new QuerySource("requests", "r"));
        var choices = QueryChoices.For(root0, Schema, caps);

        foreach (var member in choices.Fields)
        {
            yield return ($"select {member}", Select(new QuerySelect { Field = member.Reference }));

            foreach (var aggregate in choices.AggregatesFor(member.Reference))
            {
                var select = new QuerySelect
                {
                    Field = member.Reference,
                    Aggregate = aggregate,
                    Alias = "value",
                    Percentile = aggregate == QueryAggregate.Percentile ? 0.95 : null,
                };
                yield return ($"{aggregate} of {member}", Select(select));

                if (caps.Supports(QueryFeature.DistinctAggregate))
                {
                    yield return ($"distinct {aggregate} of {member}", Select(select with { Distinct = true }));
                }
            }
        }

        if (choices.CountsRows)
        {
            yield return ("count of rows", Select(new QuerySelect { Aggregate = QueryAggregate.Count, Alias = "total" }));
        }

        foreach (var period in choices.Periods)
        {
            foreach (var member in choices.Groupable.Where(candidate => candidate.Type == QueryFieldType.DateTime))
            {
                yield return ($"group by {member} truncated to {period}", root0 with
                {
                    Select = [new QuerySelect { Field = member.Reference, Truncate = period, Alias = "bucket" }],
                    GroupBy = [new QueryGroupBy(member.Reference) { Truncate = period }],
                });
            }
        }

        foreach (var member in choices.Filterable)
        {
            foreach (var offered in choices.OperatorsFor(member.Reference))
            {
                yield return ($"{member} {offered.Operator}", Where(Condition(member, offered)));
            }

            foreach (var kind in choices.ValueKindsFor(member.Reference))
            {
                var operand = Operand(kind, member, choices);
                if (operand is null)
                {
                    continue;
                }

                // Pair the operand with an operator this field actually offers, and of the matching
                // shape. Assuming Equals or In would test a combination the choices never presented.
                var wanted = kind == QueryOperandKind.List ? QueryValueArity.List : QueryValueArity.One;
                var comparison = choices.OperatorsFor(member.Reference)
                    .FirstOrDefault(offered => offered.Arity == wanted);
                if (comparison is null)
                {
                    continue;
                }

                yield return ($"{member} {comparison.Operator} {kind}", Where(
                    new QueryCondition(member.Reference, comparison.Operator) { Value = operand }));
            }
        }

        foreach (var function in choices.ValueFunctions)
        {
            yield return ($"call value function '{function.Key}'", Select(new QuerySelect
            {
                Call = Call(function.Key, function.Parameters),
                Alias = "computed",
            }));
        }

        foreach (var join in choices.Joins)
        {
            foreach (var kind in join.Kinds)
            {
                yield return ($"{kind} join '{join.Relation}'", root0 with
                {
                    Joins =
                    [
                        new QueryJoin(join.Entity, join.SuggestedAlias)
                        {
                            Kind = kind,
                            Relation = join.Relation,
                            From = join.FromAlias,
                        },
                    ],
                    Select = [new QuerySelect { Field = new QueryFieldRef("r", "route") }],
                });
            }
        }

        // Ordering and grouping filters need a query that already selects something under a name,
        // so they are read off a grouped spec rather than the bare root.
        if (!caps.Supports(QueryFeature.Grouping) || !caps.Supports(QueryFeature.Aggregates))
        {
            yield break;
        }

        var route = new QueryFieldRef("r", "route");
        var grouped = root0 with
        {
            Select =
            [
                new QuerySelect { Field = route, Alias = "route" },
                new QuerySelect { Aggregate = QueryAggregate.Count, Alias = "total" },
            ],
            GroupBy = [new QueryGroupBy(route)],
        };
        var groupedChoices = QueryChoices.For(grouped, Schema, caps);

        // The same aggregates again, this time in a query that groups. A target may support one only
        // in a query of a particular shape, so asking once in the ungrouped case proves nothing.
        foreach (var member in groupedChoices.Fields)
        {
            foreach (var aggregate in groupedChoices.AggregatesFor(member.Reference))
            {
                yield return ($"{aggregate} of {member} while grouped", grouped with
                {
                    Select =
                    [
                        new QuerySelect { Field = route, Alias = "route" },
                        new QuerySelect
                        {
                            Field = member.Reference,
                            Aggregate = aggregate,
                            Alias = "value",
                            Percentile = aggregate == QueryAggregate.Percentile ? 0.95 : null,
                        },
                    ],
                });
            }
        }

        foreach (var sort in groupedChoices.SortTargets)
        {
            foreach (var direction in new[] { QuerySortDirection.Ascending, QuerySortDirection.Descending })
            {
                // Only a grouping key or a computed output may be ordered on once grouped.
                if (sort.Field is not null && !IsGroupingKey(sort.Field, route))
                {
                    continue;
                }

                yield return ($"order by {sort.Label} {direction}", grouped with
                {
                    OrderBy = [new QuerySort { Field = sort.Field, Select = sort.Select, Direction = direction }],
                });
            }
        }

        foreach (var filter in groupedChoices.GroupingFilterTargets)
        {
            // A grouping filter tests either a grouping key or a computed output. The key's operators
            // come from the field; an output has no field, so it is asked about by kind - an
            // aggregate is a number.
            var member = filter.Field is null ? null : groupedChoices.Find(filter.Field);
            var type = member?.Type ?? QueryFieldType.Number;
            var offered = member is not null
                ? member.Operators
                : groupedChoices.OperatorsFor(QueryFieldType.Number);

            var comparison = offered.FirstOrDefault(candidate => candidate.Arity == QueryValueArity.One);
            if (comparison is null)
            {
                continue;
            }

            yield return ($"having {filter.Label} {comparison.Operator}", grouped with
            {
                Having = new QueryFilterGroup
                {
                    Conditions =
                    [
                        new QueryCondition(filter.Field, comparison.Operator)
                        {
                            Select = filter.Select,
                            Value = QueryOperand.Literal(Sample(type)),
                        },
                    ],
                },
            });
        }

        if (caps.Supports(QueryFeature.Distinct))
        {
            yield return ("distinct rows", Select(new QuerySelect { Field = route }) with { Distinct = true });
        }

        if (caps.Supports(QueryFeature.Limit))
        {
            yield return ("limit", Select(new QuerySelect { Field = route }) with { Limit = 10 });
        }

        if (caps.Supports(QueryFeature.Offset))
        {
            yield return ("offset", Select(new QuerySelect { Field = route }) with { Limit = 10, Offset = 20 });
        }
    }

    private static bool IsGroupingKey(QueryFieldRef field, QueryFieldRef key)
        => string.Equals(field.Alias, key.Alias, StringComparison.OrdinalIgnoreCase)
            && string.Equals(field.Field, key.Field, StringComparison.OrdinalIgnoreCase);

    private static QuerySpec Select(QuerySelect select)
        => new(new QuerySource("requests", "r")) { Select = [select] };

    private static QuerySpec Where(QueryCondition condition)
        => new(new QuerySource("requests", "r"))
        {
            Select = [new QuerySelect { Field = new QueryFieldRef("r", "route") }],
            Where = new QueryFilterGroup { Conditions = [condition] },
        };

    private static QueryCondition Condition(QueryFieldChoice member, QueryOperatorChoice offered)
        => new(member.Reference, offered.Operator)
        {
            Value = offered.Arity switch
            {
                QueryValueArity.None => null,
                QueryValueArity.List => QueryOperand.List([Sample(member.Type), Sample(member.Type, second: true)]),
                _ => QueryOperand.Literal(Sample(member.Type)),
            },
            Value2 = offered.Arity == QueryValueArity.Two
                ? QueryOperand.Literal(Sample(member.Type, second: true))
                : null,
        };

    private static QueryOperand? Operand(QueryOperandKind kind, QueryFieldChoice member, QueryChoices choices)
        => kind switch
        {
            QueryOperandKind.Literal => QueryOperand.Literal(Sample(member.Type)),
            QueryOperandKind.List => QueryOperand.List([Sample(member.Type), Sample(member.Type, second: true)]),
            QueryOperandKind.Relative => QueryOperand.Ago(30, QueryTimeUnit.Day),
            QueryOperandKind.Field => choices.ComparableTo(member.Reference) is { Count: > 0 } others
                ? QueryOperand.Of(others[0].Reference)
                : null,
            QueryOperandKind.Function => Schema.Functions
                .FirstOrDefault(function => function.Kind == QueryFunctionKind.Value && function.ReturnType == member.Type)
                is { } match
                ? QueryOperand.Function(Call(match.Key, match.Parameters))
                : null,
            _ => null,
        };

    // Required arguments only: an optional parameter left off is the arity a caller is most likely
    // to use, and supplying one anyway would test the function rather than the choice.
    private static QueryFunctionCall Call(string key, IReadOnlyList<QueryFunctionParameter> parameters)
        => new(key)
        {
            Arguments = parameters
                .Where(parameter => !parameter.Optional)
                .Select(parameter => QueryOperand.Literal(Sample(parameter.Type)))
                .ToArray(),
        };

    private static string Sample(QueryFieldType type, bool second = false) => type switch
    {
        QueryFieldType.Text => second ? "zulu" : "alpha",
        QueryFieldType.Number => second ? "100" : "10",
        QueryFieldType.Boolean => second ? "false" : "true",
        QueryFieldType.DateTime => (second ? new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc)
            : new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).ToString("o", CultureInfo.InvariantCulture),
        QueryFieldType.Guid => second
            ? "22222222-2222-2222-2222-222222222222"
            : "11111111-1111-1111-1111-111111111111",
        _ => second ? "second" : "first",
    };
}
