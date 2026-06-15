using System.Buffers;
using System.IO;
using System.Text.Json;
using TokenMonitoring.Models;

namespace TokenMonitoring.Services;

public sealed record SessionUsageResult(TokenUsageBreakdown Usage, RateLimitSnapshot? LatestRateLimits);

public sealed class SessionTokenUsageReader
{
    private const int ReadBufferSize = 64 * 1024;
    private const int MaxCandidateLineBytes = 4 * 1024 * 1024;

    public async Task<SessionUsageResult> ReadTodayAsync(
        string codexHome,
        DateOnly localDate,
        CancellationToken cancellationToken = default)
    {
        var files = EnumerateUniqueSessionFiles(codexHome, localDate);
        var total = new TokenUsageBreakdown(0);
        RateLimitSnapshot? latestRateLimits = null;
        DateTimeOffset latestRateTimestamp = DateTimeOffset.MinValue;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ReadFileAsync(file, localDate, cancellationToken);
            total += result.Usage;

            if (result.LatestRateLimits is not null && result.LatestRateTimestamp > latestRateTimestamp)
            {
                latestRateLimits = result.LatestRateLimits;
                latestRateTimestamp = result.LatestRateTimestamp;
            }
        }

        return new SessionUsageResult(total, latestRateLimits);
    }

    private static IReadOnlyList<string> EnumerateUniqueSessionFiles(string codexHome, DateOnly localDate)
    {
        var candidates = new List<string>();
        foreach (var directoryName in new[] { "sessions", "archived_sessions" })
        {
            var directory = Path.Combine(codexHome, directoryName);
            if (Directory.Exists(directory))
            {
                candidates.AddRange(Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.AllDirectories));
            }
        }

        return candidates
            .Where(path => DateOnly.FromDateTime(File.GetLastWriteTime(path)) >= localDate)
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(path => File.GetLastWriteTimeUtc(path)).First())
            .ToArray();
    }

    private static async Task<FileUsageResult> ReadFileAsync(
        string path,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        TokenUsageBreakdown? previous = null;
        var daily = new TokenUsageBreakdown(0);
        RateLimitSnapshot? latestRateLimits = null;
        DateTimeOffset latestRateTimestamp = DateTimeOffset.MinValue;

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await ReadTokenCountLinesAsync(stream, line =>
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!TryGetTokenCount(root, out var timestamp, out var current, out var rateLimits))
                {
                    return;
                }

                if (rateLimits is not null && timestamp > latestRateTimestamp)
                {
                    latestRateLimits = rateLimits;
                    latestRateTimestamp = timestamp;
                }

                if (DateOnly.FromDateTime(timestamp.LocalDateTime) == localDate)
                {
                    daily += Difference(current, previous);
                }

                previous = current;
            }
            catch (JsonException)
            {
                // A session can be read while Codex is appending its final line.
            }
        }, cancellationToken);

        return new FileUsageResult(daily, latestRateLimits, latestRateTimestamp);
    }

    private static async Task ReadTokenCountLinesAsync(
        FileStream stream,
        Action<ReadOnlyMemory<byte>> processLine,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
        using var partialLine = new MemoryStream();
        var lineTooLong = false;

        try
        {
            while (await stream.ReadAsync(buffer, cancellationToken) is var bytesRead && bytesRead > 0)
            {
                var offset = 0;
                while (offset < bytesRead)
                {
                    var remaining = buffer.AsSpan(offset, bytesRead - offset);
                    var newline = remaining.IndexOf((byte)'\n');
                    var segmentLength = newline >= 0 ? newline : remaining.Length;
                    var segment = buffer.AsMemory(offset, segmentLength);

                    if (partialLine.Length == 0 && !lineTooLong && newline >= 0)
                    {
                        ProcessIfTokenCountLine(segment, processLine);
                    }
                    else if (!lineTooLong)
                    {
                        if (partialLine.Length + segmentLength <= MaxCandidateLineBytes)
                        {
                            partialLine.Write(segment.Span);
                        }
                        else
                        {
                            lineTooLong = true;
                            partialLine.SetLength(0);
                        }
                    }

                    if (newline >= 0)
                    {
                        if (!lineTooLong && partialLine.Length > 0)
                        {
                            ProcessIfTokenCountLine(
                                partialLine.GetBuffer().AsMemory(0, checked((int)partialLine.Length)),
                                processLine);
                        }

                        partialLine.SetLength(0);
                        lineTooLong = false;
                        offset += segmentLength + 1;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (!lineTooLong && partialLine.Length > 0)
            {
                ProcessIfTokenCountLine(
                    partialLine.GetBuffer().AsMemory(0, checked((int)partialLine.Length)),
                    processLine);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void ProcessIfTokenCountLine(
        ReadOnlyMemory<byte> line,
        Action<ReadOnlyMemory<byte>> processLine)
    {
        if (line.Span.IndexOf("token_count"u8) >= 0)
        {
            processLine(line);
        }
    }

    private static bool TryGetTokenCount(
        JsonElement root,
        out DateTimeOffset timestamp,
        out TokenUsageBreakdown current,
        out RateLimitSnapshot? rateLimits)
    {
        timestamp = default;
        current = new TokenUsageBreakdown(0);
        rateLimits = null;

        if (!root.TryGetProperty("type", out var outerType)
            || outerType.GetString() != "event_msg"
            || !root.TryGetProperty("payload", out var payload)
            || !payload.TryGetProperty("type", out var payloadType)
            || payloadType.GetString() != "token_count"
            || !root.TryGetProperty("timestamp", out var timestampElement)
            || !DateTimeOffset.TryParse(timestampElement.GetString(), out timestamp))
        {
            return false;
        }

        if (payload.TryGetProperty("info", out var info)
            && info.ValueKind == JsonValueKind.Object
            && info.TryGetProperty("total_token_usage", out var usage))
        {
            current = new TokenUsageBreakdown(
                GetInt64(usage, "total_tokens"),
                GetInt64(usage, "input_tokens"),
                GetInt64(usage, "cached_input_tokens"),
                GetInt64(usage, "output_tokens"),
                GetInt64(usage, "reasoning_output_tokens"));
        }

        if (payload.TryGetProperty("rate_limits", out var limits) && limits.ValueKind == JsonValueKind.Object)
        {
            rateLimits = ParseLegacyRateLimits(limits);
        }

        return true;
    }

    private static RateLimitSnapshot ParseLegacyRateLimits(JsonElement limits) =>
        new(
            ParseLegacyWindow(limits, "primary"),
            ParseLegacyWindow(limits, "secondary"),
            GetString(limits, "plan_type"),
            GetString(limits, "limit_id"));

    private static RateLimitWindowSnapshot? ParseLegacyWindow(JsonElement limits, string name)
    {
        if (!limits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new RateLimitWindowSnapshot(
            (int)GetInt64(window, "used_percent"),
            GetNullableInt64(window, "resets_at"),
            GetNullableInt64(window, "window_minutes"));
    }

    private static TokenUsageBreakdown Difference(TokenUsageBreakdown current, TokenUsageBreakdown? previous)
    {
        if (previous is null || current.TotalTokens < previous.TotalTokens)
        {
            return current;
        }

        return new TokenUsageBreakdown(
            Math.Max(0, current.TotalTokens - previous.TotalTokens),
            Math.Max(0, current.InputTokens - previous.InputTokens),
            Math.Max(0, current.CachedInputTokens - previous.CachedInputTokens),
            Math.Max(0, current.OutputTokens - previous.OutputTokens),
            Math.Max(0, current.ReasoningOutputTokens - previous.ReasoningOutputTokens));
    }

    private static long GetInt64(JsonElement element, string name) =>
        GetNullableInt64(element, name) ?? 0;

    private static long? GetNullableInt64(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        if (value.TryGetInt64(out var integer))
        {
            return integer;
        }

        return checked((long)Math.Round(value.GetDouble(), MidpointRounding.AwayFromZero));
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record FileUsageResult(
        TokenUsageBreakdown Usage,
        RateLimitSnapshot? LatestRateLimits,
        DateTimeOffset LatestRateTimestamp);
}
