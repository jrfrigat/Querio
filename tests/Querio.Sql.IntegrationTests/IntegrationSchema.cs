namespace Querio.Sql.IntegrationTests;

/// <summary>
/// A deliberately small schema - two tables - carrying exactly the cases where the dialects diverge:
/// a moment to truncate and compare relatively, a number to aggregate, a text to match part of, and
/// a foreign key that does not always find its row.
/// <para>
/// Small because every table has to be created three times in three DDL dialects, and a wider schema
/// would buy coverage of joins that already have text-level tests rather than coverage of the places
/// engines actually disagree.
/// </para>
/// </summary>
internal static class IntegrationSchema
{
    internal static Guid KeyAlpha { get; } = new("11111111-1111-1111-1111-111111111111");

    internal static Guid KeyBeta { get; } = new("22222222-2222-2222-2222-222222222222");

    /// <summary>A key no row in api_keys carries, so a left join through it finds nothing.</summary>
    internal static Guid KeyMissing { get; } = new("33333333-3333-3333-3333-333333333333");

    /// <summary>
    /// Logical names differ from the physical ones on purpose: that separation is the product claim,
    /// and a suite that named them alike would never notice it break.
    /// </summary>
    internal static QuerySchema Build() => new(
        [
            new QueryEntity("requests", "Requests",
            [
                new QueryField("id", "Id", QueryFieldType.Guid) { Column = "request_id" },
                new QueryField("route", "Route", QueryFieldType.Text) { Column = "route" },
                new QueryField("timestamp", "Timestamp", QueryFieldType.DateTime) { Column = "logged_at" },
                new QueryField("durationMs", "Duration, ms", QueryFieldType.Number) { Column = "duration_ms" },
                new QueryField("error", "Error", QueryFieldType.Boolean) { Column = "is_error" },
                new QueryField("status", "Status", QueryFieldType.Number) { Column = "status_code" },
                new QueryField("apiKeyId", "API key", QueryFieldType.Guid) { Column = "api_key_id", Nullable = true },
            ])
            { Source = "request_log", PrimaryKey = ["id"] },

            new QueryEntity("apiKeys", "API keys",
            [
                new QueryField("id", "Id", QueryFieldType.Guid) { Column = "api_key_id" },
                new QueryField("name", "Name", QueryFieldType.Text) { Column = "key_name" },
            ])
            { Source = "api_keys", PrimaryKey = ["id"] },
        ],
        [
            QueryRelation.Simple("request_apiKey", "requests", "apiKeyId", "apiKeys", "id"),
        ]);

    /// <summary>
    /// One seeded request. Timestamps are offsets from the moment the suite seeds, because a relative
    /// window is resolved by the server's own clock and a pinned date would drift out of the window
    /// the day after it was written.
    /// </summary>
    internal sealed record SeedRow(string Route, int DaysAgo, int DurationMs, bool Error, int Status, Guid ApiKeyId);

    /// <summary>
    /// Five rows chosen so every assertion has a distinct right answer: two share a day, one falls
    /// outside a thirty-day window, and one points at a key that does not exist.
    /// </summary>
    internal static IReadOnlyList<SeedRow> Requests { get; } =
    [
        new("/api/a", 1, 100, true, 500, KeyAlpha),
        new("/api/a", 2, 200, true, 500, KeyAlpha),
        new("/api/b", 3, 300, false, 200, KeyBeta),
        new("/api/b", 40, 400, true, 500, KeyBeta),
        new("/health", 1, 500, false, 200, KeyMissing),
    ];

    internal static IReadOnlyList<(Guid Id, string Name)> ApiKeys { get; } =
    [
        (KeyAlpha, "alpha"),
        (KeyBeta, "beta"),
    ];
}
