using System.Globalization;
using Querio.Http;
using Querio.Language;
using Querio.Sql;
using Querio.Text;
using Querio.Tests;

namespace Querio.Renderers.Tests;

/// <summary>
/// What a two-way target loses between writing a query and reading it back.
/// <para>
/// The existing round-trip tests assert text stability - <c>Write(Read(Write(x))) == Write(x)</c> -
/// which is the right invariant and is not sufficient by itself. A reader that drops a member
/// entirely still satisfies it, because the writer then has nothing to write back and the second
/// pass agrees with the first. The query quietly became a different query, and every test stayed
/// green.
/// </para>
/// <para>
/// So this compares meaning instead: the spec that went in against the spec that came back, as a
/// flat set of facts. Nesting is deliberately not a fact - a reader is allowed to fold away a group
/// the text never carried - but no condition, select, join, sort or bound may go missing, and none
/// may change its operator, aggregate or connector. Anything a target does lose has to be declared
/// below and is asserted to be exactly that, so a loss is a decision on the record rather than a
/// discovery.
/// </para>
/// </summary>
public sealed class TwoWayFidelityTests
{
    private static readonly QuerySchema Schema = TestSchema.Build();

    private sealed record TwoWayTarget(
        string Name,
        IQueryCapabilities Capabilities,
        Func<QuerySpec, string> Write,
        Func<string, QuerySpec> Read,
        IReadOnlyList<string> MayLose);

    private static IReadOnlyList<TwoWayTarget> Targets { get; } =
    [
        new("HTTP", QueryHttp.Capabilities,
            spec => QueryHttp.Render(spec, Schema),
            text => QueryHttp.Parse(text, Schema),
            []),

        new("Language", QueryLanguage.Capabilities,
            spec => QueryLanguage.Write(spec, Schema),
            text => QueryLanguage.Parse(text, Schema),
            []),

        new("Text", QueryDescriber.Capabilities,
            spec => QueryDescriber.Describe(spec, Schema),
            text => QueryDescriber.Parse(text, Schema),
            []),
    ];

    public static TheoryData<string> TargetNames()
    {
        var data = new TheoryData<string>();
        foreach (var target in Targets)
        {
            data.Add(target.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(TargetNames))]
    public void ReadingGivesBackTheQueryThatWasWritten(string name)
    {
        var target = Targets.Single(candidate => candidate.Name == name);

        var lost = new SortedSet<string>(StringComparer.Ordinal);
        var checkedQueries = 0;
        foreach (var (_, spec) in QueryProbes.For(target.Capabilities))
        {
            string text;
            QuerySpec restored;
            try
            {
                text = target.Write(spec);
                restored = target.Read(text);
            }
            catch (Exception error) when (error is QueryParseException or QueryValidationException or QueryRenderException)
            {
                lost.Add($"round trip failed: {error.GetType().Name}: {Trim(error.Message)}");
                continue;
            }

            checkedQueries++;
            foreach (var difference in Differences(Facts(spec), Facts(restored)))
            {
                lost.Add(difference);
            }
        }

        Assert.True(checkedQueries > 50, $"Only {checkedQueries} queries round-tripped through {name}; the probe set has stopped covering the model.");

        var undeclared = lost.Where(item => !target.MayLose.Any(item.StartsWith)).ToList();
        Assert.True(
            undeclared.Count == 0,
            $"{name} changes the query in ways it does not declare:\n  " + string.Join("\n  ", undeclared));
    }

    /// <summary>
    /// Every loss a target declares has to be a loss it really has. A declaration nothing exercises
    /// is a permission that outlived its reason, and it would hide the next regression that fits it.
    /// </summary>
    [Theory]
    [MemberData(nameof(TargetNames))]
    public void EveryDeclaredLossIsOneTheTargetReallyHas(string name)
    {
        var target = Targets.Single(candidate => candidate.Name == name);
        if (target.MayLose.Count == 0)
        {
            return;
        }

        var seen = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (_, spec) in QueryProbes.For(target.Capabilities))
        {
            try
            {
                foreach (var difference in Differences(Facts(spec), Facts(target.Read(target.Write(spec)))))
                {
                    seen.Add(difference);
                }
            }
            catch (Exception error) when (error is QueryParseException or QueryValidationException or QueryRenderException)
            {
                seen.Add($"round trip failed: {error.GetType().Name}: {Trim(error.Message)}");
            }
        }

        var unused = target.MayLose.Where(allowed => !seen.Any(item => item.StartsWith(allowed, StringComparison.Ordinal))).ToList();
        Assert.True(
            unused.Count == 0,
            $"{name} declares losses it does not have: {string.Join(", ", unused)}");
    }

    /// <summary>
    /// The cross-target check: a query that has been through a two-way target has to render to the
    /// same SQL as the one that went in, parameters included.
    /// <para>
    /// This is the one that would catch a loss the fact comparison was not looking for. The facts are
    /// a list somebody wrote and can forget to extend; SQL is produced by a renderer that reads the
    /// whole spec, so a member that quietly stopped surviving the trip changes the text here whether
    /// or not anyone thought to compare it.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(TargetNames))]
    public void ARoundTripDoesNotChangeTheSqlTheQueryRendersTo(string name)
    {
        var target = Targets.Single(candidate => candidate.Name == name);
        var dialect = new PostgreSqlDialect();
        var compared = 0;

        foreach (var (what, spec) in QueryProbes.For(target.Capabilities))
        {
            SqlRenderResult before;
            try
            {
                before = SqlRenderer.Render(Settled(spec), Schema, dialect);
            }
            catch (Exception error) when (error is QueryRenderException or QueryValidationException)
            {
                // PostgreSQL cannot express it, so it has nothing to say about the round trip.
                continue;
            }

            var after = SqlRenderer.Render(Settled(target.Read(target.Write(spec))), Schema, dialect);
            compared++;

            Assert.Equal(before.Sql, after.Sql);
            Assert.Equal(
                before.Parameters.Select(parameter => $"{parameter.Name}={parameter.Value}"),
                after.Parameters.Select(parameter => $"{parameter.Name}={parameter.Value}"));
        }

        Assert.True(compared > 50, $"Only {compared} queries were compared for {name}; the probe set has stopped covering the model.");
    }

    /// <summary>
    /// Settles the one difference that is a wording and not a meaning: a sort or grouping filter that
    /// names a selected output whose value is a bare field is rewritten to name the field.
    /// <para>
    /// A sentence has no room for the distinction - "ordered by Requests: Route" is all there is to
    /// write - so a description reads back naming the field, and renders <c>ORDER BY "r"."route"</c>
    /// where the original rendered <c>ORDER BY "route"</c>. Both order by the same column. Applying
    /// this to both sides keeps the comparison exact about everything else rather than relaxing it.
    /// </para>
    /// </summary>
    private static QuerySpec Settled(QuerySpec spec)
    {
        var plain = spec.Select
            .Where(item => item.Field is not null && item.Aggregate is null && item.Truncate is null
                && item.Call is null && !string.IsNullOrEmpty(item.Alias))
            .GroupBy(item => item.Alias!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Field!, StringComparer.OrdinalIgnoreCase);

        if (plain.Count == 0)
        {
            return spec;
        }

        return spec with
        {
            OrderBy = [.. spec.OrderBy.Select(sort => sort.Select is not null && plain.TryGetValue(sort.Select, out var field)
                ? sort with { Select = null, Field = field }
                : sort)],
            Having = Settled(spec.Having, plain),
        };
    }

    private static QueryFilterGroup? Settled(QueryFilterGroup? group, IReadOnlyDictionary<string, QueryFieldRef> plain)
        => group is null ? null : group with
        {
            Conditions = [.. group.Conditions.Select(condition =>
                condition.Select is not null && plain.TryGetValue(condition.Select, out var field)
                    ? condition with { Select = null, Field = field }
                    : condition)],
            Groups = [.. group.Groups.Select(nested => Settled(nested, plain)!)],
        };

    private static IEnumerable<string> Differences(List<string> written, List<string> restored)
    {
        foreach (var fact in written.Except(restored, StringComparer.Ordinal))
        {
            yield return "dropped: " + fact;
        }

        foreach (var fact in restored.Except(written, StringComparer.Ordinal))
        {
            yield return "invented: " + fact;
        }
    }

    // The query as a flat set of statements about it. Order is not a fact either, beyond the order
    // a sort explicitly asks for, which is carried inside the sort's own fact.
    private static List<string> Facts(QuerySpec spec)
    {
        var facts = new List<string>
        {
            $"from {Participant(spec.From.Entity, spec.From.Call, spec.From.Alias)}",
            $"distinct {spec.Distinct}",
            $"limit {Show(spec.Limit)}",
            $"offset {Show(spec.Offset)}",
        };

        foreach (var join in spec.Joins)
        {
            facts.Add($"join {Participant(join.Entity, join.Call, join.Alias)} {join.Kind}"
                + $" on={(join.On is null ? "-" : string.Join("+", join.On.Select(c => $"{c.Left}={c.Right}")))}");
        }

        foreach (var select in spec.Select)
        {
            facts.Add($"select {Value(select.Field, select.Call)}"
                + $" agg={Show(select.Aggregate)} distinct={select.Distinct}"
                + $" pct={Show(select.Percentile)} trunc={Show(select.Truncate)} as={select.Alias ?? "-"}");
        }

        foreach (var group in spec.GroupBy)
        {
            facts.Add($"group {Value(group.Field, group.Call)} trunc={Show(group.Truncate)}");
        }

        // Naming a selected output and naming the field that output is are the same request, so a
        // target is free to write one and read back the other. That holds only while the output is a
        // bare field: once it aggregates or truncates, the output and the field stop agreeing, and a
        // swap between them would be a real change of meaning rather than a change of wording.
        var plain = spec.Select
            .Where(item => item.Field is not null && item.Aggregate is null && item.Truncate is null
                && item.Call is null && !string.IsNullOrEmpty(item.Alias))
            .GroupBy(item => item.Alias!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Field!, StringComparer.OrdinalIgnoreCase);

        foreach (var sort in spec.OrderBy)
        {
            facts.Add($"order {Target(sort.Field, sort.Call, sort.Select, plain)} {sort.Direction}");
        }

        Flatten(spec.Where, "where", facts, plain);
        Flatten(spec.Having, "having", facts, plain);

        facts.Sort(StringComparer.Ordinal);
        return facts;
    }

    // Conditions are collected without their nesting, but with the connector of the group holding
    // them: folding a redundant level away keeps the connector, while turning an OR into an AND
    // changes the answer and has to show up.
    private static void Flatten(
        QueryFilterGroup? group, string clause, List<string> facts, IReadOnlyDictionary<string, QueryFieldRef> plain)
    {
        if (group is null || group.IsEmpty)
        {
            return;
        }

        // With fewer than two children the connector says nothing, so it is normalised rather than
        // left to differ between a writer that emitted it and a reader that had no reason to.
        var children = group.Conditions.Count + group.Groups.Count;
        var connector = children > 1 && group.Or ? "or" : "and";

        foreach (var condition in group.Conditions)
        {
            facts.Add($"{clause} {connector} {Target(condition.Field, condition.Call, condition.Select, plain)}"
                + $" {condition.Operator} {Operand(condition.Value)} {Operand(condition.Value2)}");
        }

        foreach (var nested in group.Groups)
        {
            Flatten(nested, clause, facts, plain);
        }
    }

    // What a sort or a grouping filter points at, with a named output resolved to its field when the
    // two mean the same thing.
    private static string Target(
        QueryFieldRef? field, QueryFunctionCall? call, string? select, IReadOnlyDictionary<string, QueryFieldRef> plain)
    {
        if (select is not null && plain.TryGetValue(select, out var named))
        {
            return named.ToString();
        }

        return select is not null ? "sel:" + select : Value(field, call);
    }

    private static string Participant(string? entity, QueryFunctionCall? call, string alias)
        => $"{alias}:{entity ?? (call is null ? "-" : Call(call))}";

    private static string Value(QueryFieldRef? field, QueryFunctionCall? call)
        => field is not null ? field.ToString() : call is null ? "-" : Call(call);

    private static string Call(QueryFunctionCall call)
        => $"{call.Function}({string.Join(",", call.Arguments.Select(Operand))})";

    private static string Operand(QueryOperand? operand) => operand is null
        ? "-"
        : operand.Kind switch
        {
            QueryOperandKind.Literal => $"lit:{operand.Value}",
            QueryOperandKind.List => $"list:{string.Join("|", operand.Values ?? [])}",
            QueryOperandKind.Field => $"field:{operand.Field}",
            QueryOperandKind.Relative => $"rel:{operand.Relative?.Amount}{operand.Relative?.Unit}",
            QueryOperandKind.Function => $"fn:{(operand.Call is null ? "-" : Call(operand.Call))}",
            _ => "?",
        };

    private static string Show(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string Show(double? value)
        => value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "-";

    private static string Show<T>(T? value) where T : struct, Enum
        => value?.ToString() ?? "-";

    private static string Trim(string message)
        => message.Length <= 120 ? message : message[..120] + "...";
}
