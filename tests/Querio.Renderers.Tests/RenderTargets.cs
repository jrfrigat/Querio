using System.Collections;
using Querio.Http;
using Querio.Language;
using Querio.Linq;
using Querio.OneC;
using Querio.Sql;
using Querio.Text;
using Querio.Tests;

namespace Querio.Renderers.Tests;

/// <summary>
/// Every shipped target behind one shape: what it can do, and how to make it do it. Tests that must
/// hold for all of them - that nothing <see cref="QueryChoices"/> offers is refused at render time,
/// say - need the whole set rather than a list one target can quietly drop off.
/// </summary>
internal sealed record RenderTarget(string Name, IQueryCapabilities Capabilities, Action<QuerySpec, QuerySchema> Render)
{
    public override string ToString() => Name;
}

internal static class RenderTargets
{
    // Pinned so a relative window renders the same text every run.
    internal static DateTime Now { get; } = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Every target a query can be rendered to. When a package is added, it belongs here - a guard
    /// that a target can escape is not a guard.
    /// </summary>
    internal static IReadOnlyList<RenderTarget> All { get; } =
    [
        Sql("SQL Server", new SqlServerDialect()),
        Sql("PostgreSQL", new PostgreSqlDialect()),
        Sql("SQLite", new SqliteDialect()),

        new RenderTarget("1C", OneCRenderer.Capabilities,
            (spec, schema) => OneCRenderer.Render(spec, schema, Now)),

        new RenderTarget("Text", QueryDescriber.Capabilities,
            (spec, schema) => QueryDescriber.Describe(spec, schema)),

        new RenderTarget("HTTP", QueryHttp.Capabilities,
            (spec, schema) => QueryHttp.Render(spec, schema)),

        new RenderTarget("Language", QueryLanguage.Capabilities,
            (spec, schema) => QueryLanguage.Write(spec, schema)),

        // Executed rather than rendered to text, over empty sequences: the question here is whether
        // the plan can be built at all, and no row is needed to answer it.
        new RenderTarget("LINQ", QueryExecutor.Capabilities,
            (spec, schema) => QueryExecutor.Execute(spec, schema, EmptySources(), Functions(), Now)),
    ];

    private static RenderTarget Sql(string name, SqlDialect dialect)
        => new(name, dialect, (spec, schema) => SqlRenderer.Render(spec, schema, dialect));

    // Property names match the schema's logical field keys, which is how this target binds.
    private sealed class RequestRow
    {
        public Guid Id { get; init; }
        public string Route { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; }
        public int DurationMs { get; init; }
        public bool CacheHit { get; init; }
        public bool Error { get; init; }
        public int Status { get; init; }
        public Guid ApiKeyId { get; init; }
    }

    private sealed class ApiKeyRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public Guid OwnerId { get; init; }
    }

    private sealed class UserRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public Guid? ManagerId { get; init; }
        public string Secret { get; init; } = string.Empty;
    }

    private sealed class TransferRow
    {
        public Guid Id { get; init; }
        public Guid FromUserId { get; init; }
        public Guid ToUserId { get; init; }
        public int Amount { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    private sealed class OrderRow
    {
        public Guid TenantId { get; init; }
        public int Number { get; init; }
        public DateTime PlacedAt { get; init; }
    }

    private sealed class OrderLineRow
    {
        public Guid TenantId { get; init; }
        public int OrderNumber { get; init; }
        public string Sku { get; init; } = string.Empty;
        public int Quantity { get; init; }
    }

    private sealed class ActiveUserRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
    }

    // Every entity the schema declares, bound to nothing. An unbound entity is a render error in
    // this target, and that error would otherwise read as a capability failure.
    private static QuerySources EmptySources() => new QuerySources()
        .Add("requests", Array.Empty<RequestRow>())
        .Add("apiKeys", Array.Empty<ApiKeyRow>())
        .Add("users", Array.Empty<UserRow>())
        .Add("transfers", Array.Empty<TransferRow>())
        .Add("orders", Array.Empty<OrderRow>())
        .Add("orderLines", Array.Empty<OrderLineRow>());

    // Likewise every declared function, for the same reason. A library of its own rather than
    // QueryFunctionLibrary.Empty: registering is a mutation, and this is not the place to find out
    // whose library it landed in.
    private static QueryFunctionLibrary Functions() => new QueryFunctionLibrary()
        .Register<int, int>("calcTax", amount => amount * 2)
        .Register<string, string>("upper", text => text.ToUpperInvariant())
        .RegisterTable<ActiveUserRow>("activeUsers", _ => []);
}
