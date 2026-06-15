using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Threading;
using TokenMonitoring.Services;

namespace TokenMonitoring;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private UsageMonitorService? _monitor;
    private NotifyIcon? _trayIcon;
    private MainWindow? _window;
    private DispatcherTimer? _positionSaveTimer;
    private DispatcherTimer? _foregroundTimer;
    private AppSettings _settings = new();
    private bool _userVisible = true;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, "TokenMonitoring.SingleInstance", out var ownsMutex);
        if (!ownsMutex)
        {
            Shutdown();
            return;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("windir")))
        {
            Environment.SetEnvironmentVariable(
                "windir",
                Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows");
        }

        _settings = AppSettingsStore.Load();
        AppSettingsStore.Save(_settings);
        _monitor = new UsageMonitorService();
        _window = new MainWindow(_monitor, _settings.ShowRemaining) { Opacity = _settings.Opacity };
        ApplySavedPosition();
        _window.HideRequested += () => SetUserVisibility(false);
        _window.LocationChanged += (_, _) => SchedulePositionSave();

        CreateTrayIcon();
        _monitor.Start();
        StartForegroundMonitoring();

        if (e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            timer.Tick += async (_, _) =>
            {
                timer.Stop();
                await ExitAsync();
            };
            timer.Start();
        }
    }

    private void ApplySavedPosition()
    {
        if (_window is null || _settings.WindowLeft is null || _settings.WindowTop is null)
        {
            return;
        }

        var left = _settings.WindowLeft.Value;
        var top = _settings.WindowTop.Value;
        var virtualLeft = System.Windows.SystemParameters.VirtualScreenLeft;
        var virtualTop = System.Windows.SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + System.Windows.SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + System.Windows.SystemParameters.VirtualScreenHeight;

        if (left + 80 >= virtualLeft && left <= virtualRight - 80
            && top + 40 >= virtualTop && top <= virtualBottom - 40)
        {
            _window.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
            _window.Left = left;
            _window.Top = top;
        }
    }

    private void CreateTrayIcon()
    {
        var text = Localization.Text;
        var menu = new ContextMenuStrip();
        menu.Items.Add(text.ShowHide, null, (_, _) => SetUserVisibility(!_userVisible));

        var remainingItem = new ToolStripMenuItem(text.ShowRemaining)
        {
            Checked = _settings.ShowRemaining,
            CheckOnClick = true
        };
        remainingItem.CheckedChanged += (_, _) =>
        {
            _settings = _settings with { ShowRemaining = remainingItem.Checked };
            _window?.SetShowRemaining(remainingItem.Checked);
            SaveSettings();
        };
        menu.Items.Add(remainingItem);

        var codexOnlyItem = new ToolStripMenuItem(text.ShowOnlyWhenCodexActive)
        {
            Checked = _settings.ShowOnlyWhenCodexActive,
            CheckOnClick = true
        };
        codexOnlyItem.CheckedChanged += (_, _) =>
        {
            _settings = _settings with { ShowOnlyWhenCodexActive = codexOnlyItem.Checked };
            SaveSettings();
            UpdateAutomaticVisibility();
        };
        menu.Items.Add(codexOnlyItem);

        var startupItem = new ToolStripMenuItem(text.StartWithWindows)
        {
            Checked = StartupRegistration.IsEnabled(),
            CheckOnClick = true
        };
        startupItem.CheckedChanged += (_, _) =>
        {
            StartupRegistration.SetEnabled(startupItem.Checked);
            _settings = _settings with { StartWithWindows = startupItem.Checked };
            SaveSettings();
        };
        menu.Items.Add(startupItem);
        menu.Items.Add(text.ResetPosition, null, (_, _) => ResetWindowPosition());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(text.RefreshNow, null, async (_, _) =>
        {
            if (_monitor is not null)
            {
                await _monitor.RefreshNowAsync();
            }
        });
        menu.Items.Add(text.Exit, null, async (_, _) => await ExitAsync());

        _trayIcon = new NotifyIcon
        {
            Icon = LoadApplicationIcon(),
            Text = text.AppName,
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => SetUserVisibility(!_userVisible);
    }

    private static Icon LoadApplicationIcon()
    {
        var resource = GetResourceStream(new Uri("pack://application:,,,/Assets/app-icon.ico"));
        if (resource?.Stream is null)
        {
            return (Icon)SystemIcons.Application.Clone();
        }

        using var stream = resource.Stream;
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    private void SetUserVisibility(bool visible)
    {
        _userVisible = visible;
        if (_window is null)
        {
            return;
        }

        if (visible)
        {
            _window.Show();
            _window.Activate();
        }
        else
        {
            _window.Hide();
        }
    }

    private void StartForegroundMonitoring()
    {
        _foregroundTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _foregroundTimer.Tick += (_, _) => UpdateAutomaticVisibility();
        _foregroundTimer.Start();
        UpdateAutomaticVisibility();
    }

    private void UpdateAutomaticVisibility()
    {
        if (_window is null)
        {
            return;
        }

        var shouldShow = _userVisible
            && (!_settings.ShowOnlyWhenCodexActive || ForegroundAppMonitor.IsCodexOrCurrentAppActive());

        if (shouldShow && !_window.IsVisible)
        {
            _window.ShowActivated = false;
            _window.Show();
            _window.ShowActivated = true;
        }
        else if (!shouldShow && _window.IsVisible)
        {
            _window.Hide();
        }
    }

    private void ResetWindowPosition()
    {
        if (_window is null)
        {
            return;
        }

        var workArea = System.Windows.SystemParameters.WorkArea;
        _window.Left = workArea.Left + Math.Max(0, (workArea.Width - _window.Width) / 2);
        _window.Top = workArea.Top + Math.Max(0, (workArea.Height - _window.Height) / 2);
        _settings = _settings with { WindowLeft = _window.Left, WindowTop = _window.Top };
        SaveSettings();
        SetUserVisibility(true);
    }

    private void SchedulePositionSave()
    {
        if (_window is null || !_window.IsLoaded)
        {
            return;
        }

        _positionSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _positionSaveTimer.Stop();
        _positionSaveTimer.Tick -= PositionSaveTimerOnTick;
        _positionSaveTimer.Tick += PositionSaveTimerOnTick;
        _positionSaveTimer.Start();
    }

    private void PositionSaveTimerOnTick(object? sender, EventArgs e)
    {
        _positionSaveTimer?.Stop();
        if (_window is null)
        {
            return;
        }

        _settings = _settings with { WindowLeft = _window.Left, WindowTop = _window.Top };
        SaveSettings();
    }

    private void SaveSettings() => AppSettingsStore.Save(_settings);

    private async Task ExitAsync()
    {
        _positionSaveTimer?.Stop();
        _foregroundTimer?.Stop();
        if (_monitor is not null)
        {
            await _monitor.DisposeAsync();
        }

        _trayIcon?.Dispose();
        _window?.ForceClose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Shutdown();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _positionSaveTimer?.Stop();
        base.OnExit(e);
    }
}
