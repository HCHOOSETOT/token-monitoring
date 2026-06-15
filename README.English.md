# Token Monitoring

[简体中文](README.md) | **English**

Token Monitoring is a Windows x64 companion panel for Codex. It provides quick access to the 5-hour allowance, weekly allowance, and today's local token consumption without repeatedly opening Codex settings.

## Interface Preview

![Token Monitoring running interface](docs/images/token-monitoring-zh-CN.png)

The floating panel combines remaining allowances, reset times, today's token breakdown, and cache hit rate. The Chinese and English packages use the same layout.

## Features

- Remaining 5-hour allowance with a reset countdown updated every second.
- Remaining weekly allowance and its reset date in local time.
- Today's total, input, output, cached-input tokens, and overall cache hit rate.
- Colored progress represents remaining allowance; gray represents used allowance.
- Freely movable panel that remembers its position.
- By default, the panel appears only while Codex is the foreground application.
- Tray controls, optional startup with Windows, manual refresh, and used/remaining display modes.

The visible countdown updates every second. Allowance and token sources are reread every 30 seconds by default.

## Requirements

- Windows 10 22H2 or Windows 11, 64-bit x64.
- Codex desktop or Codex CLI installed and signed in.
- `codex.exe` discoverable through `PATH`, a running Codex process, a common installation directory, or `CODEX_CLI_PATH`.
- The release is self-contained; Python, Node.js, Visual Studio, and a separate .NET installation are not required.

32-bit Windows, macOS, and Linux are not supported. Windows ARM64 is not a native target. The project was tested on June 15, 2026 with `codex-cli 0.139.0`; future Codex protocol changes may require an update.

## Install and use

1. Extract `TokenMonitoring-win-x64-English.zip`.
2. Place the complete folder on any local drive and in any directory, including paths containing spaces or Unicode characters.
3. Run `TokenMonitoring.exe`.
4. Right-click the tray icon to change display settings, enable startup, reset the panel position, refresh, or exit.

The installation directory is not used to locate Codex data. Session logs are read from `%USERPROFILE%\.codex\sessions` and `%USERPROFILE%\.codex\archived_sessions`. Settings are written to `%LOCALAPPDATA%\TokenMonitoring\settings.json`.

Startup registration stores the current EXE path under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Re-enable startup after moving the application.

## Data sources and accuracy

- The 5-hour and weekly allowances come from the local Codex `app-server` through `account/rateLimits/read` and `account/rateLimits/updated` messages.
- Today's tokens are calculated by local system date from `token_count` events in local Codex JSONL session logs.
- Allowance resets do not clear today's token count; a new daily total starts at local midnight.
- Only logs retained on this computer are included. Deleted logs, usage on another computer, ChatGPT web usage, normal OpenAI API usage, and other GPT clients are excluded.
- Cache hit rate is calculated as `cached input tokens / input tokens`.

## Privacy and networking

Token Monitoring contains no `HttpClient`, socket, WebRequest, telemetry, update-check, or upload implementation. It embeds no API endpoint, API key, or access token, and it does not read `%USERPROFILE%\.codex\auth.json`.

For live allowance data, the application starts the trusted local `codex app-server --stdio` and exchanges JSON over redirected standard input/output. The Token Monitoring process does not connect directly to the network, but the Codex child process may contact OpenAI according to Codex configuration. Local token-log analysis does not upload those logs.

See [SECURITY.md](SECURITY.md) for the complete access inventory, network boundary, risks, and audit notes.

## Build and package

```powershell
dotnet build TokenMonitoring.slnx -c Release
dotnet run --project tests\TokenMonitoring.Tests\TokenMonitoring.Tests.csproj -c Release
dotnet run --project tests\TokenMonitoring.Tests\TokenMonitoring.Tests.csproj -c Release -- --integration
.\scripts\publish.ps1
```

Packages are written to `artifacts\TokenMonitoring-win-x64-Chinese.zip` and `artifacts\TokenMonitoring-win-x64-English.zip`.

## Before public distribution

- The executable is currently unsigned, so Windows SmartScreen may warn on another computer.
- The current application icon is derived from a user-supplied third-party image and is not automatically covered by the MIT license. Confirm distribution rights before release; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
