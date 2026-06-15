# Security and Privacy

Last audited: June 15, 2026.

## Pre-release audit results

| Check | Result |
| --- | --- |
| Release build | Passed with 0 warnings and 0 errors |
| Parser tests | 4/4 passed, including midnight rollover and arbitrary Unicode/space paths |
| Live Codex integration | Passed; live allowances and local daily logs were combined successfully |
| Cross-drive launch | Passed from a deep C-drive directory containing Unicode and spaces |
| Source network API scan | No direct `HttpClient`, socket, or WebRequest implementation found |
| Dynamic TCP observation | No connection was observed for Token Monitoring; none was observed for its process tree during the short test window |
| Credential and string scan | No API key, bearer token, `auth.json`, OpenAI API endpoint, or developer user directory found |
| Package contents | No `.pdb`, local settings, session logs, or authentication files included |

Dynamic observation describes only the test window and does not prove that the Codex child process will remain offline in every situation.

## Access inventory

| Type | Location or object | Purpose |
| --- | --- | --- |
| Read | `%USERPROFILE%\.codex\sessions\**\*.jsonl` | Parse `token_count` events |
| Read | `%USERPROFILE%\.codex\archived_sessions\**\*.jsonl` | Include archived sessions in today's total |
| Read/write | `%LOCALAPPDATA%\TokenMonitoring\settings.json` | Store non-sensitive display settings and window position |
| Optional write | Current-user `...\CurrentVersion\Run` registry key | Store the EXE path when startup is enabled |
| Child process | `codex app-server --stdio` | Obtain live Codex allowances |

The application does not read `.codex\auth.json`, copy sign-in credentials, or modify Codex session logs. Logs are opened read-only with file sharing so Codex can continue writing. Lines without `token_count` are skipped, and conversation content is not persisted.

## Network boundary

- Token Monitoring contains no direct network client, server, telemetry, crash upload, automatic update, or advertising code.
- `http://schemas.microsoft.com/...` strings in XAML are WPF XML namespace identifiers, not request endpoints.
- The application communicates with `codex app-server` only through local standard input/output.
- The Codex child process may contact OpenAI. That behavior belongs to the installed Codex application, not to a network implementation in Token Monitoring.

Do not interpret "no direct connection by Token Monitoring" as "the entire process tree is offline." Live allowances depend on Codex, which may use the network.

## Trust boundaries and known risks

- The application first honors `CODEX_CLI_PATH`, then searches `PATH` and local Codex installations. Do not point these locations to untrusted executables.
- The discovered `codex.exe` digital signature is not currently validated. Install official Codex only on a trusted computer.
- The Token Monitoring executable is unsigned, so Windows SmartScreen may warn and cannot verify the publisher.
- Local Codex JSONL logs may contain conversation content. Token Monitoring does not upload them, but any malicious process running as the same Windows user may be able to read them.
- Release packages exclude `.pdb` symbols to avoid unnecessarily disclosing build-machine source paths.
- The settings file contains no Codex credentials, only booleans, opacity, and window coordinates.

## Secret checks

Before release, static scans should confirm that source and packages contain no API keys, bearer tokens, client secrets, private certificates, or `.codex\auth.json`. The current repository embeds no OpenAI API URL; live allowances are obtained through local Codex protocol method names. The self-contained .NET runtime includes ICU locale identifiers beginning with `sk-`; those strings are language data, not OpenAI keys.

## Reporting a security issue

After the repository is public, use a private GitHub Security Advisory for suspected credential disclosure, log upload, arbitrary code execution, or excessive file access. Never attach real tokens or session logs to a public issue.
