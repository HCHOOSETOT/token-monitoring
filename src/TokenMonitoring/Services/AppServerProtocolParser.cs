using System.Globalization;
using System.Text.Json;
using TokenMonitoring.Models;

namespace TokenMonitoring.Services;

public static class AppServerProtocolParser
{
    public static RateLimitSnapshot ParseRateLimitsResponse(JsonElement result)
    {
        var snapshot = result.TryGetProperty("rateLimitsByLimitId", out var byId)
            && byId.ValueKind == JsonValueKind.Object
            && byId.TryGetProperty("codex", out var codex)
                ? codex
                : result.GetProperty("rateLimits");

        return ParseRateLimitSnapshot(snapshot);
    }

    public static RateLimitSnapshot ParseRateLimitNotification(JsonElement parameters) =>
        ParseRateLimitSnapshot(parameters.GetProperty("rateLimits"));

    public static long? ParseDailyAccountUsage(JsonElement result, DateOnly localDate)
    {
        if (!result.TryGetProperty("dailyUsageBuckets", out var buckets)
            || buckets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var bucket in buckets.EnumerateArray())
        {
            if (!bucket.TryGetProperty("startDate", out var startDate)
                || !bucket.TryGetProperty("tokens", out var tokens))
            {
                continue;
            }

            if (DateOnly.TryParse(startDate.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && date == localDate)
            {
                return tokens.GetInt64();
            }
        }

        return null;
    }

    private static RateLimitSnapshot ParseRateLimitSnapshot(JsonElement snapshot)
    {
        return new RateLimitSnapshot(
            ParseWindow(snapshot, "primary"),
            ParseWindow(snapshot, "secondary"),
            GetOptionalString(snapshot, "planType"),
            GetOptionalString(snapshot, "limitId"));
    }

    private static RateLimitWindowSnapshot? ParseWindow(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new RateLimitWindowSnapshot(
            window.GetProperty("usedPercent").GetInt32(),
            GetOptionalInt64(window, "resetsAt"),
            GetOptionalInt64(window, "windowDurationMins"));
    }

    private static long? GetOptionalInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;

    private static string? GetOptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
