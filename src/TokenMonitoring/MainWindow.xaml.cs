using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TokenMonitoring.Models;
using TokenMonitoring.Services;
using MediaBrush = System.Windows.Media.Brush;

namespace TokenMonitoring;

public partial class MainWindow : System.Windows.Window
{
    private readonly UsageMonitorService _monitor;
    private readonly DispatcherTimer _displayTimer;
    private UsageSnapshot _snapshot = UsageSnapshot.Empty;
    private bool _showRemaining;
    private bool _forceClose;

    public event Action? HideRequested;

    public MainWindow(UsageMonitorService monitor, bool showRemaining)
    {
        InitializeComponent();
        Title = Localization.Text.AppName;
        _monitor = monitor;
        _showRemaining = showRemaining;
        _monitor.SnapshotChanged += OnSnapshotChanged;

        _displayTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => Render(), Dispatcher);
        _displayTimer.Start();
        Render();
    }

    public void SetShowRemaining(bool showRemaining)
    {
        _showRemaining = showRemaining;
        Render();
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _displayTimer.Stop();
        _monitor.SnapshotChanged -= OnSnapshotChanged;
        base.OnClosed(e);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            Hide();
            HideRequested?.Invoke();
        }

        base.OnClosing(e);
    }

    private void OnSnapshotChanged(UsageSnapshot snapshot)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _snapshot = snapshot;
            Render();
        });
    }

    private void Render()
    {
        var ui = Localization.Text;
        UsageTitleText.Text = ui.UsageTitle;
        TodayLabel.Text = ui.Today;
        InputLabel.Text = ui.Input;
        OutputLabel.Text = ui.Output;
        CacheLabel.Text = ui.Cache;
        HitRateLabel.Text = ui.HitRate;
        FiveHourLabel.Text = _showRemaining ? ui.FiveHourRemaining : ui.FiveHourUsed;
        WeekLabel.Text = _showRemaining ? ui.WeekRemaining : ui.WeekUsed;
        RenderWindow(_snapshot.FiveHour, FiveHourBar, FiveHourText, showCountdown: true);
        RenderWindow(_snapshot.Week, WeekBar, WeekText, showCountdown: false);
        var tokenUsage = _snapshot.TodayTokens;
        TodayText.Text = $"{FormatTokens(tokenUsage.TotalTokens)} {ui.Tokens}";
        InputTokensText.Text = FormatTokens(tokenUsage.InputTokens);
        OutputTokensText.Text = FormatTokens(tokenUsage.OutputTokens);
        CachedTokensText.Text = FormatTokens(tokenUsage.CachedInputTokens);
        CacheHitRateText.Text = tokenUsage.CacheHitRate is null
            ? "--"
            : tokenUsage.CacheHitRate.Value.ToString("P1", Localization.Culture);
        InputTokensText.ToolTip = $"{ui.InputTotal}: {tokenUsage.InputTokens:N0}\n{ui.FreshInput}: {tokenUsage.UncachedInputTokens:N0}";
        OutputTokensText.ToolTip = $"{ui.OutputTotal}: {tokenUsage.OutputTokens:N0}\n{ui.ReasoningOutput}: {tokenUsage.ReasoningOutputTokens:N0}";
        CachedTokensText.ToolTip = $"{ui.CachedInput}: {tokenUsage.CachedInputTokens:N0}";
        CacheHitRateText.ToolTip = ui.CacheRateDescription;
        TodayText.ToolTip = string.Create(CultureInfo.InvariantCulture,
            $"{ui.Input}: {tokenUsage.InputTokens:N0}\n{ui.Cache}: {tokenUsage.CachedInputTokens:N0}\n{ui.Output}: {tokenUsage.OutputTokens:N0}\n{ui.ReasoningOutput}: {tokenUsage.ReasoningOutputTokens:N0}");

        var hasData = _snapshot.UpdatedAt != DateTimeOffset.MinValue;
        StatusText.Text = !hasData ? ui.Connecting : _snapshot.IsStale ? ui.Stale : ui.Live;
        StatusText.Foreground = !hasData
            ? Brush("#7F8BA3")
            : _snapshot.IsStale ? Brush("#E4A853") : Brush("#54D39B");
        UpdatedText.Text = hasData ? _snapshot.UpdatedAt.ToLocalTime().ToString("HH:mm:ss") : string.Empty;
        UpdatedText.ToolTip = string.Join("\n", new[] { _snapshot.DataSource, _snapshot.Error }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private void RenderWindow(
        RateLimitWindowSnapshot? window,
        System.Windows.Controls.ProgressBar bar,
        System.Windows.Controls.TextBlock text,
        bool showCountdown)
    {
        if (window is null)
        {
            bar.Value = 0;
            text.Text = "--";
            return;
        }

        var displayPercent = _showRemaining ? 100 - window.UsedPercent : window.UsedPercent;
        bar.Value = displayPercent;
        bar.Foreground = UsageBrush(window.UsedPercent);
        var resetDisplay = showCountdown
            ? FormatCountdown(window.ResetsAt)
            : FormatResetDate(window.ResetsAt);
        text.Text = $"{displayPercent}%  {resetDisplay}";
        var ui = Localization.Text;
        text.ToolTip = $"{(_showRemaining ? ui.Remaining : ui.Used)}: {displayPercent}%\n{ui.Resets}: {FormatFullResetTime(window.ResetsAt)}\n{ui.Countdown}: {FormatCountdown(window.ResetsAt)}";
    }

    private static string FormatResetDate(DateTimeOffset? resetsAt)
    {
        if (resetsAt is null)
        {
            return "--";
        }

        var local = resetsAt.Value.ToLocalTime();
        return local.ToString(Localization.IsChinese ? "M月d日" : "MMM d", Localization.Culture);
    }

    private static string FormatFullResetTime(DateTimeOffset? resetsAt) => resetsAt is null
        ? Localization.Text.Unknown
        : resetsAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", Localization.Culture);

    private static string FormatCountdown(DateTimeOffset? resetsAt)
    {
        if (resetsAt is null)
        {
            return Localization.Text.Unknown;
        }

        var remaining = resetsAt.Value - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return Localization.Text.Resetting;
        }

        return remaining.TotalDays >= 1
            ? $"{(int)remaining.TotalDays}d {remaining.Hours}h"
            : $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    private static string FormatTokens(long tokens) => tokens switch
    {
        >= 1_000_000_000 => $"{tokens / 1_000_000_000d:0.##}B",
        >= 1_000_000 => $"{tokens / 1_000_000d:0.##}M",
        >= 1_000 => $"{tokens / 1_000d:0.##}K",
        _ => tokens.ToString("N0", Localization.Culture)
    };

    private static MediaBrush UsageBrush(int percent) => percent switch
    {
        >= 90 => Brush("#F06C75"),
        >= 70 => Brush("#E9B44C"),
        _ => Brush("#59D4A4")
    };

    private static MediaBrush Brush(string color) => (MediaBrush)new BrushConverter().ConvertFromString(color)!;

    private void Panel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
