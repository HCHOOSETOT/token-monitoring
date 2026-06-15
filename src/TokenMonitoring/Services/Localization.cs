using System.IO;
using System.Globalization;

namespace TokenMonitoring.Services;

public sealed record UiStrings(
    string AppName,
    string ShowHide,
    string ShowRemaining,
    string ShowOnlyWhenCodexActive,
    string StartWithWindows,
    string RefreshNow,
    string ResetPosition,
    string Exit,
    string UsageTitle,
    string Connecting,
    string Live,
    string Stale,
    string FiveHourRemaining,
    string FiveHourUsed,
    string WeekRemaining,
    string WeekUsed,
    string Today,
    string Input,
    string Output,
    string Cache,
    string HitRate,
    string Remaining,
    string Used,
    string Resets,
    string Countdown,
    string Resetting,
    string Unknown,
    string Tokens,
    string InputTotal,
    string FreshInput,
    string OutputTotal,
    string ReasoningOutput,
    string CachedInput,
    string CacheRateDescription);

public static class Localization
{
    public static bool IsChinese { get; } = ReadLanguage();
    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo(IsChinese ? "zh-CN" : "en-US");

    public static UiStrings Text { get; } = IsChinese
        ? new UiStrings(
            "Token 额度监控",
            "显示 / 隐藏",
            "显示剩余额度",
            "仅在 Codex 位于前台时显示",
            "开机自动启动",
            "立即刷新",
            "重置窗口位置",
            "退出",
            "CODEX 用量",
            "连接中",
            "实时",
            "数据过期",
            "5时剩余",
            "5时已用",
            "周剩余",
            "周已用",
            "今日",
            "输入",
            "输出",
            "缓存",
            "命中率",
            "剩余",
            "已用",
            "重置时间",
            "倒计时",
            "重置中",
            "未知",
            "Token",
            "输入总量",
            "未缓存输入",
            "输出总量",
            "推理输出",
            "缓存读取",
            "今日缓存读取 Token / 输入 Token 总量")
        : new UiStrings(
            "Token Monitoring",
            "Show / Hide",
            "Show remaining percentage",
            "Show only while Codex is active",
            "Start with Windows",
            "Refresh now",
            "Reset window position",
            "Exit",
            "CODEX USAGE",
            "CONNECTING",
            "LIVE",
            "STALE",
            "5h left",
            "5h used",
            "W left",
            "W used",
            "Today",
            "INPUT",
            "OUTPUT",
            "CACHE",
            "HIT RATE",
            "Remaining",
            "Used",
            "Resets",
            "Countdown",
            "resetting",
            "Unknown",
            "tokens",
            "Input total",
            "Fresh input",
            "Output total",
            "Reasoning output",
            "Cached input read",
            "Cached input / total input for today's local Codex sessions");

    private static bool ReadLanguage()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "language.txt");
            return !File.Exists(path)
                || !File.ReadAllText(path).Trim().StartsWith("en", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }
}
