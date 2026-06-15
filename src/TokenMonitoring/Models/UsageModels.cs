namespace TokenMonitoring.Models;

public sealed record RateLimitWindowSnapshot(
    int UsedPercent,
    long? ResetsAtUnixSeconds,
    long? WindowDurationMinutes)
{
    public DateTimeOffset? ResetsAt => ResetsAtUnixSeconds is null
        ? null
        : DateTimeOffset.FromUnixTimeSeconds(ResetsAtUnixSeconds.Value);
}

public sealed record RateLimitSnapshot(
    RateLimitWindowSnapshot? Primary,
    RateLimitWindowSnapshot? Secondary,
    string? PlanType = null,
    string? LimitId = null);

public sealed record TokenUsageBreakdown(
    long TotalTokens,
    long InputTokens = 0,
    long CachedInputTokens = 0,
    long OutputTokens = 0,
    long ReasoningOutputTokens = 0)
{
    public double? CacheHitRate => InputTokens > 0
        ? Math.Clamp((double)CachedInputTokens / InputTokens, 0, 1)
        : null;

    public long UncachedInputTokens => Math.Max(0, InputTokens - CachedInputTokens);

    public static TokenUsageBreakdown operator +(TokenUsageBreakdown left, TokenUsageBreakdown right) =>
        new(
            left.TotalTokens + right.TotalTokens,
            left.InputTokens + right.InputTokens,
            left.CachedInputTokens + right.CachedInputTokens,
            left.OutputTokens + right.OutputTokens,
            left.ReasoningOutputTokens + right.ReasoningOutputTokens);
}

public sealed record UsageSnapshot(
    RateLimitWindowSnapshot? FiveHour,
    RateLimitWindowSnapshot? Week,
    TokenUsageBreakdown TodayTokens,
    DateTimeOffset UpdatedAt,
    string DataSource,
    bool IsStale = false,
    string? Error = null)
{
    public static UsageSnapshot Empty { get; } = new(
        null,
        null,
        new TokenUsageBreakdown(0),
        DateTimeOffset.MinValue,
        "Waiting for Codex");
}
