# 安全与隐私说明

最后审计日期：2026 年 6 月 15 日。

## 本次发布前审计结果

| 检查 | 结果 |
| --- | --- |
| Release 构建 | 通过，0 警告、0 错误 |
| 解析测试 | 4/4 通过，包含跨午夜和中文/空格任意路径 |
| 真实 Codex 集成 | 通过，可读取实时额度并与本地当天日志组合 |
| 异盘启动 | 通过，从 C 盘多层中文/空格目录启动并正常退出 |
| 源码网络 API 扫描 | 未发现 `HttpClient`、Socket、WebRequest 等直接联网 API |
| 动态 TCP 观察 | Token Monitoring 进程未观察到连接；短时观察中子进程树也未观察到连接 |
| 凭据与敏感字符串 | 未发现 API Key、Bearer Token、`auth.json`、OpenAI API endpoint 或开发机用户目录 |
| 发布包内容 | 不含 `.pdb`、本地设置、会话日志或认证文件 |

动态观察只能说明测试时间窗内没有连接，不能证明 Codex 子进程在所有情况下都不会联网。

## 数据访问清单

| 类型 | 位置或对象 | 用途 |
| --- | --- | --- |
| 读取 | `%USERPROFILE%\.codex\sessions\**\*.jsonl` | 解析 `token_count` 事件 |
| 读取 | `%USERPROFILE%\.codex\archived_sessions\**\*.jsonl` | 统计已归档会话的当天 Token |
| 读取/写入 | `%LOCALAPPDATA%\TokenMonitoring\settings.json` | 保存显示模式、窗口位置等非敏感设置 |
| 可选写入 | 当前用户注册表 `...\CurrentVersion\Run` | 用户主动启用开机启动时保存 EXE 路径 |
| 启动进程 | `codex app-server --stdio` | 获取 Codex 实时额度 |

程序不会读取 `.codex\auth.json`，不会复制登录凭据，也不会修改 Codex 会话日志。读取日志时使用共享只读方式，以免阻塞 Codex 写入。程序逐行检查日志，只解析包含 `token_count` 的 JSON 行，不保存对话内容。

## 联网边界

- Token Monitoring 源码没有直接网络客户端、服务器、遥测、崩溃上传、自动更新或广告代码。
- 源码中的 `http://schemas.microsoft.com/...` 是 WPF XAML 命名空间，不是网络请求地址。
- 程序和 `codex app-server` 仅通过本机标准输入输出通信。
- Codex 子进程可能连接 OpenAI。该连接属于已安装的 Codex，不由 Token Monitoring 自己实现。

因此，不应把“Token Monitoring 进程没有直连”理解为“运行期间整个进程树绝对离线”。实时额度依赖 Codex，Codex 本身可能联网。

## 信任边界与已知风险

- 程序会优先运行 `CODEX_CLI_PATH` 指向的文件，然后查找 `PATH` 和本机 Codex。请勿把该环境变量或 PATH 指向不可信程序。
- 当前未验证所找到 `codex.exe` 的数字签名。只应在可信电脑上安装官方 Codex。
- 当前 EXE 未代码签名，Windows SmartScreen 可能警告；这不等于恶意，但发布者身份无法由系统验证。
- 本地 Codex JSONL 日志本身可能包含对话内容。Token Monitoring 不上传这些文件，但任何拥有当前 Windows 用户权限的恶意程序都可能读取它们。
- 发布包不包含 `.pdb` 调试符号，避免无必要地公开开发机源码路径。
- 设置文件不包含 Codex 凭据，仅包含布尔设置、透明度和窗口坐标。

## 密钥检查

发布前静态扫描应确认源码和发布包中不存在 API Key、Bearer Token、客户端密钥、私人证书或 `.codex\auth.json`。仓库当前也没有内置任何 OpenAI API URL；实时额度通过 Codex 本地协议方法名获取。自包含 .NET 运行时的 ICU 区域名称表中存在以 `sk-` 开头的语言标识，它不是 OpenAI 密钥。

## 安全问题报告

公开仓库后，请使用仓库的 Security Advisory 私下报告可能导致凭据泄露、日志上传、任意代码执行或越权文件访问的问题，不要把真实 Token 或会话日志贴到公开 Issue。
