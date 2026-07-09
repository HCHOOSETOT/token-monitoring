using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using TokenMonitoring.Models;

namespace TokenMonitoring.Services;

public sealed class CodexAppServerClient : IAsyncDisposable
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _process;
    private Task? _readerTask;
    private long _nextRequestId;

    public event Action<RateLimitSnapshot>? RateLimitsUpdated;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        var launchCommand = CodexExecutableLocator.Find();
        var startInfo = new ProcessStartInfo
        {
            FileName = launchCommand.FileName,
            Arguments = launchCommand.Arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start 'codex app-server'.");
        _readerTask = ReadLoopAsync(_process, _lifetime.Token);

        var initializeResult = await SendRequestAsync(
            "initialize",
            new
            {
                clientInfo = new { name = "token-monitoring", title = "Token Monitoring", version = "1.0.0" },
                capabilities = new { experimentalApi = true }
            },
            cancellationToken);

        if (!initializeResult.TryGetProperty("codexHome", out _))
        {
            throw new InvalidDataException("Codex app-server returned an invalid initialize response.");
        }

        await SendNotificationAsync("initialized", null, cancellationToken);
    }

    public async Task<RateLimitSnapshot> GetRateLimitsAsync(CancellationToken cancellationToken)
    {
        var result = await SendRequestAsync("account/rateLimits/read", null, cancellationToken);
        return AppServerProtocolParser.ParseRateLimitsResponse(result);
    }

    public async Task<long?> GetTodayAccountUsageAsync(DateOnly localDate, CancellationToken cancellationToken)
    {
        var result = await SendRequestAsync("account/usage/read", null, cancellationToken);
        return AppServerProtocolParser.ParseDailyAccountUsage(result, localDate);
    }

    private async Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        try
        {
            await WriteMessageAsync(new { id, method, @params = parameters }, cancellationToken);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken) =>
        WriteMessageAsync(parameters is null ? new { method } : new { method, @params = parameters }, cancellationToken);

    private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
    {
        var process = _process ?? throw new InvalidOperationException("Codex app-server is not connected.");
        var json = JsonSerializer.Serialize(message);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                line = line.TrimStart('\uFEFF', '\u00EF', '\u00BB', '\u00BF');
                var jsonStart = line.IndexOf('{');
                if (jsonStart > 0)
                {
                    line = line[jsonStart..];
                }

                if (line.Length == 0 || jsonStart < 0)
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;

                if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id))
                {
                    if (_pending.TryGetValue(id, out var completion))
                    {
                        if (root.TryGetProperty("error", out var error))
                        {
                            completion.TrySetException(new InvalidOperationException(error.ToString()));
                        }
                        else if (root.TryGetProperty("result", out var result))
                        {
                            completion.TrySetResult(result.Clone());
                        }
                    }

                    continue;
                }

                if (root.TryGetProperty("method", out var method)
                    && method.GetString() == "account/rateLimits/updated"
                    && root.TryGetProperty("params", out var parameters))
                {
                    try
                    {
                        RateLimitsUpdated?.Invoke(AppServerProtocolParser.ParseRateLimitNotification(parameters));
                    }
                    catch
                    {
                        // Ignore malformed push notifications; polling will refresh the canonical values.
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            FailPending(exception);
        }
        finally
        {
            FailPending(new EndOfStreamException("Codex app-server closed its output stream."));
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (var completion in _pending.Values)
        {
            completion.TrySetException(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();

        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
        }

        if (_readerTask is not null)
        {
            try
            {
                await _readerTask;
            }
            catch
            {
            }
        }

        _process?.Dispose();
        _lifetime.Dispose();
        _writeLock.Dispose();
    }
}
