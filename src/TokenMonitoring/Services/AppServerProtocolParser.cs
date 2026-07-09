using System.Globalization;
using System.Text.Json;
using TokenMonitoring.Models;

namespace TokenMonitoring.Services;

public static class AppServerProtocolParser
{
    public static RateLimitSnapshot ParseRateLimitsResponse(JsonElement result)
    {
        return TryGetRateLimitSnapshot(result, out var snapshot)
            ? ParseRateLimitSnapshot(snapshot)
            : new RateLimitSnapshot(null, null);
    }

    public static RateLimitSnapshot ParseRateLimitNotification(JsonElement parameters) =>
        TryGetRateLimitSnapshot(parameters, out var snapshot)
            ? ParseRateLimitSnapshot(snapshot)
            : new RateLimitSnapshot(null, null);

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

        if (!TryGetInt32(window, out var usedPercent, "usedPercent", "used_percent"))
        {
            return null;
        }

        return new RateLimitWindowSnapshot(
            usedPercent,
            GetOptionalInt64(window, "resetsAt", "resets_at"),
            GetOptionalInt64(window, "windowDurationMins", "windowMinutes", "window_minutes"));
    }

    private static bool TryGetRateLimitSnapshot(JsonElement element, out JsonElement snapshot)
    {
        if (element.TryGetProperty("rateLimitsByLimitId", out var byId)
            && byId.ValueKind == JsonValueKind.Object)
        {
            if (byId.TryGetProperty("codex", out snapshot))
            {
                return true;
            }

            foreach (var limit in byId.EnumerateObject())
            {
                snapshot = limit.Value;
                return true;
            }
        }

        if (element.TryGetProperty("rateLimits", out snapshot))
        {
            return true;
        }

        snapshot = default;
        return false;
    }

    private static bool TryGetInt32(JsonElement element, out int result, params string[] names)
    {
        if (TryGetNumber(element, out var value, names))
        {
            if (value.TryGetInt32(out result))
            {
                return true;
            }

            result = checked((int)Math.Round(value.GetDouble(), MidpointRounding.AwayFromZero));
            return true;
        }

        result = 0;
        return false;
    }

    private static long? GetOptionalInt64(JsonElement element, params string[] names)
    {
        if (!TryGetNumber(element, out var value, names))
        {
            return null;
        }

        return value.TryGetInt64(out var integer)
            ? integer
            : checked((long)Math.Round(value.GetDouble(), MidpointRounding.AwayFromZero));
    }

    private static bool TryGetNumber(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Number)
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetOptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
