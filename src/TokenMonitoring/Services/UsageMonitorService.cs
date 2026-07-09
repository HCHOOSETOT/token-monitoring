using System.IO;
using TokenMonitoring.Models;

namespace TokenMonitoring.Services;

public sealed class UsageMonitorService : IAsyncDisposable
{
    private readonly SessionTokenUsageReader _sessionReader = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly string _codexHome = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex");
    private CodexAppServerClient? _client;
    private Task? _monitorTask;
    private UsageSnapshot _current = UsageSnapshot.Empty;

    public UsageSnapshot Current => _current;
    public event Action<UsageSnapshot>? SnapshotChanged;

    public void Start()
    {
        _monitorTask ??= MonitorLoopAsync(_lifetime.Token);
    }

    public async Task RefreshNowAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var localDate = DateOnly.FromDateTime(DateTime.Now);
            try
            {
                var client = await EnsureClientAsync(cancellationToken);
                var rateLimits = await client.GetRateLimitsAsync(cancellationToken);

                Publish(new UsageSnapshot(
                    rateLimits.Primary,
                    rateLimits.Secondary,
                    _current.TodayTokens,
                    DateTimeOffset.Now,
                    "Codex rate limits"));

                var sessionResult = await _sessionReader.ReadTodayAsync(_codexHome, localDate, cancellationToken);
                Publish(new UsageSnapshot(
                    rateLimits.Primary,
                    rateLimits.Secondary,
                    sessionResult.Usage,
                    DateTimeOffset.Now,
                    $"Local Codex sessions for {localDate:yyyy-MM-dd}"));
            }
            catch (Exception exception)
            {
                await ResetClientAsync();
                var sessionResult = await _sessionReader.ReadTodayAsync(_codexHome, localDate, cancellationToken);
                Publish(new UsageSnapshot(
                    _current.FiveHour ?? sessionResult.LatestRateLimits?.Primary,
                    _current.Week ?? sessionResult.LatestRateLimits?.Secondary,
                    sessionResult.Usage,
                    DateTimeOffset.Now,
                    "Local sessions",
                    IsStale: true,
                    Error: exception.Message));
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<CodexAppServerClient> EnsureClientAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            return _client;
        }

        var client = new CodexAppServerClient();
        client.RateLimitsUpdated += OnRateLimitsUpdated;
        try
        {
            await client.ConnectAsync(cancellationToken);
            _client = client;
            return client;
        }
        catch
        {
            client.RateLimitsUpdated -= OnRateLimitsUpdated;
            await client.DisposeAsync();
            throw;
        }
    }

    private void OnRateLimitsUpdated(RateLimitSnapshot update)
    {
        var snapshot = _current with
        {
            FiveHour = update.Primary ?? _current.FiveHour,
            Week = update.Secondary ?? _current.Week,
            UpdatedAt = DateTimeOffset.Now,
            DataSource = "Codex live update",
            IsStale = false,
            Error = null
        };
        Publish(snapshot);
        _ = RefreshLocalUsageAfterRateLimitUpdateAsync(update);
    }

    private async Task RefreshLocalUsageAfterRateLimitUpdateAsync(RateLimitSnapshot update)
    {
        if (!await _refreshLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var localDate = DateOnly.FromDateTime(DateTime.Now);
            var sessionResult = await _sessionReader.ReadTodayAsync(_codexHome, localDate, _lifetime.Token);
            Publish(new UsageSnapshot(
                update.Primary ?? _current.FiveHour,
                update.Secondary ?? _current.Week,
                sessionResult.Usage,
                DateTimeOffset.Now,
                $"Codex live update + local sessions for {localDate:yyyy-MM-dd}"));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Publish(_current with { IsStale = true, Error = exception.Message });
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RefreshNowAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Publish(_current with { IsStale = true, Error = exception.Message });
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void Publish(UsageSnapshot snapshot)
    {
        _current = snapshot;
        SnapshotChanged?.Invoke(snapshot);
    }

    private async Task ResetClientAsync()
    {
        if (_client is null)
        {
            return;
        }

        var client = _client;
        _client = null;
        client.RateLimitsUpdated -= OnRateLimitsUpdated;
        await client.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_monitorTask is not null)
        {
            try
            {
                await _monitorTask;
            }
            catch
            {
            }
        }

        await ResetClientAsync();
        _refreshLock.Dispose();
        _lifetime.Dispose();
    }
}
