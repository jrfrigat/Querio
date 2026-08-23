using System.Globalization;
using Querio.Tests;

namespace Querio.Renderers.Tests;

/// <summary>
/// Every query <see cref="QueryChoices"/> would let a builder produce for a given target, each as
/// the smallest spec that uses the thing being offered.
/// <para>
/// Shared because two different questions are asked of the same set. One is whether a renderer
/// accepts everything offered; the other is whether a two-way target can read back what it wrote.
/// Generating the corpus twice would let the two drift, and the second question is only interesting
/// over queries somebody could actually have built.
/// </para>
/// </summary>
internal static class QueryProbes
{
    private static readonly QuerySchema Schema = TestSchema.Build();

    // Everything QueryChoices will offer, turned into the smallest query that uses it. Minimal on
    // purpose: a probe that failed for an unrelated reason would report the wrong culprit.
    internal static IEnumerable<(string What, QuerySpec Spec)> For(IQueryCapabilities caps)
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
