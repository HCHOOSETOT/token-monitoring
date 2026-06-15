using System.Text.Json;
using TokenMonitoring.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Rate-limit response parsing", TestRateLimitParsing),
    ("Daily account usage parsing", TestAccountUsageParsing),
    ("Session usage across midnight", TestSessionUsageAcrossMidnight),
    ("Session parsing from arbitrary Unicode path", TestSessionUsageFromArbitraryPath)
};

var runIntegration = args.Contains("--integration", StringComparer.OrdinalIgnoreCase);

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.WriteLine($"FAIL  {test.Name}\n      {exception.Message}");
    }
}

if (runIntegration)
{
    try
    {
        await using var client = new CodexAppServerClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await client.ConnectAsync(timeout.Token);
        var limits = await client.GetRateLimitsAsync(timeout.Token);
        var daily = await client.GetTodayAccountUsageAsync(DateOnly.FromDateTime(DateTime.Now), timeout.Token);
        Console.WriteLine($"PASS  Live Codex app-server ({limits.Primary?.UsedPercent}% / {limits.Secondary?.UsedPercent}%, today {daily?.ToString() ?? "n/a"})");

        var codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var localDate = DateOnly.FromDateTime(DateTime.Now);
        var localUsage = await new SessionTokenUsageReader().ReadTodayAsync(codexHome, localDate, timeout.Token);
        Console.WriteLine($"PASS  Local system-date usage ({localDate:yyyy-MM-dd}: {localUsage.Usage.TotalTokens})");

        await using var monitor = new UsageMonitorService();
        await monitor.RefreshNowAsync(timeout.Token);
        if (monitor.Current.Error is not null)
        {
            throw new InvalidOperationException($"monitor refresh failed: {monitor.Current.Error}");
        }
        Equal(localUsage.Usage, monitor.Current.TodayTokens, "monitor must use local system-date session usage");
        Console.WriteLine($"PASS  Combined usage monitor (today {monitor.Current.TodayTokens.TotalTokens})");
    }
    catch (Exception exception)
    {
        failures.Add($"Live Codex app-server: {exception.Message}");
        Console.WriteLine($"FAIL  Live Codex app-server\n      {exception}");
    }
}

if (failures.Count > 0)
{
    Environment.ExitCode = 1;
    Console.WriteLine($"\n{failures.Count} test(s) failed.");
}
else
{
    Console.WriteLine($"\nAll {tests.Length} tests passed.");
}

return;

static Task TestRateLimitParsing()
{
    using var document = JsonDocument.Parse("""
        {
          "rateLimits": {
            "limitId": "codex",
            "planType": "plus",
            "primary": { "usedPercent": 13, "resetsAt": 1781357706, "windowDurationMins": 300 },
            "secondary": { "usedPercent": 31, "resetsAt": 1781858592, "windowDurationMins": 10080 }
          }
        }
        """);

    var result = AppServerProtocolParser.ParseRateLimitsResponse(document.RootElement);
    Equal(13, result.Primary?.UsedPercent, "primary percent");
    Equal(300L, result.Primary?.WindowDurationMinutes, "primary duration");
    Equal(31, result.Secondary?.UsedPercent, "secondary percent");
    Equal("plus", result.PlanType, "plan type");
    return Task.CompletedTask;
}

static Task TestAccountUsageParsing()
{
    using var document = JsonDocument.Parse("""
        {
          "summary": { "lifetimeTokens": 9000 },
          "dailyUsageBuckets": [
            { "startDate": "2026-06-12", "tokens": 1200 },
            { "startDate": "2026-06-13", "tokens": 3456 }
          ]
        }
        """);

    var result = AppServerProtocolParser.ParseDailyAccountUsage(document.RootElement, new DateOnly(2026, 6, 13));
    Equal(3456L, result, "daily token bucket");
    return Task.CompletedTask;
}

static async Task TestSessionUsageAcrossMidnight()
{
    var root = Path.Combine(Path.GetTempPath(), "TokenMonitoring.Tests", Guid.NewGuid().ToString("N"));
    var sessionDirectory = Path.Combine(root, "sessions", "2026", "06", "13");
    Directory.CreateDirectory(sessionDirectory);
    var file = Path.Combine(sessionDirectory, "rollout-test.jsonl");

    try
    {
        var localDate = DateOnly.FromDateTime(DateTime.Now);
        var todayStart = localDate.ToDateTime(TimeOnly.MinValue);
        var localOffset = TimeZoneInfo.Local.GetUtcOffset(todayStart);
        var yesterday = new DateTimeOffset(todayStart.AddMinutes(-1), localOffset);
        var todayFirst = new DateTimeOffset(todayStart.AddMinutes(1), localOffset);
        var todaySecond = new DateTimeOffset(todayStart.AddMinutes(2), localOffset);
        var lines = new[]
        {
            CreateTokenLine(yesterday, 100, 90, 50, 10, 2),
            CreateTokenLine(todayFirst, 160, 140, 80, 20, 4),
            CreateTokenLine(todaySecond, 250, 210, 120, 40, 8)
        };
        await File.WriteAllLinesAsync(file, lines);

        var reader = new SessionTokenUsageReader();
        var result = await reader.ReadTodayAsync(root, localDate);
        Equal(150L, result.Usage.TotalTokens, "today total delta");
        Equal(120L, result.Usage.InputTokens, "today input delta");
        Equal(70L, result.Usage.CachedInputTokens, "today cached input delta");
        Equal(30L, result.Usage.OutputTokens, "today output delta");
        Equal(6L, result.Usage.ReasoningOutputTokens, "today reasoning delta");
        Equal(50L, result.Usage.UncachedInputTokens, "today uncached input");
        Equal(70d / 120d, result.Usage.CacheHitRate, "weighted cache hit rate");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task TestSessionUsageFromArbitraryPath()
{
    var root = Path.Combine(
        Path.GetTempPath(),
        "Token Monitoring arbitrary location",
        "\u4e2d\u6587 \u8def\u5f84",
        Guid.NewGuid().ToString("N"));
    var sessionDirectory = Path.Combine(root, "sessions", "nested folder");
    Directory.CreateDirectory(sessionDirectory);
    var file = Path.Combine(sessionDirectory, "rollout-path-test.jsonl");

    try
    {
        var localDate = DateOnly.FromDateTime(DateTime.Now);
        var timestamp = new DateTimeOffset(
            localDate.ToDateTime(new TimeOnly(12, 0)),
            TimeZoneInfo.Local.GetUtcOffset(localDate.ToDateTime(new TimeOnly(12, 0))));
        await File.WriteAllTextAsync(file, CreateTokenLine(timestamp, 321, 250, 200, 71, 11));

        var result = await new SessionTokenUsageReader().ReadTodayAsync(root, localDate);
        Equal(321L, result.Usage.TotalTokens, "arbitrary-path total");
        Equal(250L, result.Usage.InputTokens, "arbitrary-path input");
        Equal(200L, result.Usage.CachedInputTokens, "arbitrary-path cached input");
        Equal(71L, result.Usage.OutputTokens, "arbitrary-path output");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static string CreateTokenLine(
    DateTimeOffset timestamp,
    long total,
    long input,
    long cached,
    long output,
    long reasoning) => JsonSerializer.Serialize(new
{
    timestamp,
    type = "event_msg",
    payload = new
    {
        type = "token_count",
        info = new
        {
            total_token_usage = new
            {
                total_tokens = total,
                input_tokens = input,
                cached_input_tokens = cached,
                output_tokens = output,
                reasoning_output_tokens = reasoning
            }
        },
        rate_limits = new
        {
            limit_id = "codex",
            primary = new { used_percent = 12, window_minutes = 300, resets_at = 1781357706 },
            secondary = new { used_percent = 30, window_minutes = 10080, resets_at = 1781858592 }
        }
    }
});

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }
}
