using VoicemeeterWindowsVolume.Controllers;
using VoicemeeterWindowsVolume.Models;
using VoicemeeterWindowsVolume.Workers;
using System.Runtime.InteropServices;

namespace VoicemeeterWindowsVolume.Views;

public class TrayViewController : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_SHOW = 5;

    private static TrayViewController? _instance;
    public static TrayViewController Instance => _instance ??= new TrayViewController();

    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _contextMenu;

    private Form? _syncForm;
    private readonly Dictionary<string, ToolStripMenuItem> _bindingItems = new();

    private readonly Dictionary<string, ToolStripMenuItem> _toggleItems = new();

    private ToolStripMenuItem? _vmNotDetectedItem;
    private ToolStripMenuItem? _limitToggleItem;

    private readonly SettingsController _settings = SettingsController.Instance;

    public IEnumerable<string> GetActiveBindings()
        => _bindingItems.Where(kv => kv.Value.Checked).Select(kv => kv.Key);

    public void UpdateBindingLabels()
    {
        if (_syncForm == null || !_syncForm.IsHandleCreated) return;
        _syncForm.BeginInvoke(RefreshBindingLabels);
    }

    public void ShowVmNotDetected()
    {
        if (_syncForm == null || !_syncForm.IsHandleCreated) return;
        _syncForm.BeginInvoke(() =>
        {
            if (_vmNotDetectedItem != null)
                _vmNotDetectedItem.Visible = true;
        });
    }

    public void HideVmNotDetected()
    {
        if (_syncForm == null || !_syncForm.IsHandleCreated) return;
        _syncForm.BeginInvoke(() =>
        {
            if (_vmNotDetectedItem != null)
                _vmNotDetectedItem.Visible = false;
        });
    }

    public void Initialize(string iconColor = "default")
    {
        _syncForm = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            Size = new System.Drawing.Size(1, 1),
            WindowState = FormWindowState.Minimized,
        };
        _syncForm.Load += (_, _) => _syncForm.Hide();
        _syncForm.Show(); // creates HWND

        _contextMenu = new ContextMenuStrip();
        BuildMenu(_contextMenu);
        AttachStayOpenHandlers(_contextMenu);

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(iconColor),
            Text = AppStrings.FriendlyName,
            ContextMenuStrip = _contextMenu,
            Visible = true,
        };
    }

    private static System.Drawing.Icon LoadIcon(string color)
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", $"app-{color}.ico");
        if (File.Exists(iconPath))
            return new System.Drawing.Icon(iconPath);
        return SystemIcons.Application;
    }

    private static void AttachStayOpenHandlers(ToolStripDropDown menu)
    {
        menu.Closing += (s, e) =>
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
                e.Cancel = true;
        };
        foreach (ToolStripItem item in menu.Items)
            if (item is ToolStripMenuItem mi && mi.HasDropDownItems)
                AttachStayOpenHandlers(mi.DropDown);
    }

    private void BuildMenu(ContextMenuStrip menu)
    {
        menu.Items.Add(new ToolStripMenuItem(
            $"{AppStrings.FriendlyName.ToUpperInvariant()}\t V{AppStrings.Version}")
        { Enabled = false });

        menu.Items.Add(new ToolStripSeparator());

        _vmNotDetectedItem = new ToolStripMenuItem("VoiceMeeter not detected.") { Enabled = false };
        menu.Items.Add(_vmNotDetectedItem);

        menu.Items.Add(BuildBindingsMenu());
        menu.Items.Add(BuildRestartsMenu());

        menu.Items.Add(BuildPatchesMenu());

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem(AppStrings.MenuItems.VmTitle) { Enabled = false });
        menu.Items.Add(ItemShowVoicemeeter());
        menu.Items.Add(ItemRestartVoicemeeter());
        menu.Items.Add(ItemRestartAudioEngine());

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem(AppStrings.MenuItems.SupportTitle) { Enabled = false });
        
        menu.Items.Add(new ToolStripMenuItem("Original Author") { Enabled = false });
        menu.Items.Add(ItemVisitGithub());
        menu.Items.Add(ItemDonate());
        
        menu.Items.Add(new ToolStripSeparator());
        
        menu.Items.Add(new ToolStripMenuItem("Fork Author") { Enabled = false });
        menu.Items.Add(ItemVisitGithubFork());
        menu.Items.Add(ItemDonateKofi());

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(ItemOpenApplicationFolder());
        menu.Items.Add(ItemExit());

        ApplySavedToggles();
    }

    private ToolStripMenuItem BuildBindingsMenu()
    {
        var sub = new ToolStripMenuItem(AppStrings.MenuItems.ListBindings);
        sub.DropDownItems.Add(new ToolStripMenuItem(AppStrings.MenuItems.TitleInputs) { Enabled = false });

        for (int i = 0; i <= 7; i++)
        {
            string sid = $"Strip_{i}";
            var item = new ToolStripMenuItem($"Input Strip {i}") { CheckOnClick = true, Checked = false };
            item.Click += (_, _) => OnToggleChanged(sid, item.Checked);
            _bindingItems[sid] = item;
            sub.DropDownItems.Add(item);
        }

        sub.DropDownItems.Add(new ToolStripSeparator());
        sub.DropDownItems.Add(new ToolStripMenuItem(AppStrings.MenuItems.TitleOutputs) { Enabled = false });

        for (int i = 0; i <= 7; i++)
        {
            string sid = $"Bus_{i}";
            var item = new ToolStripMenuItem($"Output Bus {i}") { CheckOnClick = true, Checked = false };
            item.Click += (_, _) => OnToggleChanged(sid, item.Checked);
            _bindingItems[sid] = item;
            sub.DropDownItems.Add(item);
        }

        return sub;
    }

    private ToolStripMenuItem BuildRestartsMenu()
    {
        var sub = new ToolStripMenuItem(AppStrings.MenuItems.ListRestarts);

        sub.DropDownItems.Add(ToggleItem(
            sid: "restart_audio_engine_on_device_change",
            title: AppStrings.Console.RestartReasons.DeviceChange,
            onActivate: checked_ =>
            {
                if (checked_) WindowsAudioScanner.Instance.StartAudioDeviceScanner();
                else WindowsAudioScanner.Instance.StopAudioDeviceScanner();
            }
        ));

        sub.DropDownItems.Add(ToggleItem(
            sid: "restart_audio_engine_on_any_device_change",
            title: AppStrings.Console.RestartReasons.AnyDeviceChange,
            onActivate: checked_ =>
            {
                if (checked_)
                {
                    WindowsAudioScanner.Instance.StopAudioDeviceScanner();
                    WindowsAudioScanner.Instance.StartAllDeviceScanner();
                }
                else
                {
                    if (_settings.IsToggleChecked("restart_audio_engine_on_device_change"))
                        WindowsAudioScanner.Instance.StartAudioDeviceScanner();
                    WindowsAudioScanner.Instance.StopAllDeviceScanner();
                }
            }
        ));

        sub.DropDownItems.Add(ToggleItem(
            sid: "restart_audio_engine_on_resume",
            title: AppStrings.Console.RestartReasons.Resume,
            onActivate: checked_ =>
            {
                if (checked_) Workers.WindowsEventScanner.Instance.StartWindowsEventScanner();
                else Workers.WindowsEventScanner.Instance.StopWindowsEventScanner();
            }
        ));

        sub.DropDownItems.Add(ToggleItem(
            sid: "restart_audio_engine_on_app_launch",
            title: AppStrings.Console.RestartReasons.AppLaunch
        ));

        WindowsAudioScanner.Instance.AudioDeviceChanged += (_, devices) =>
        {
            bool enabled = _settings.IsToggleChecked("restart_audio_engine_on_device_change") &&
                           !_settings.IsToggleChecked("restart_audio_engine_on_any_device_change");
            if (enabled && devices.New > 0)
            {
                Task.Delay(1000).ContinueWith(_ =>
                {
                    System.Console.WriteLine(string.Format(AppStrings.Console.RestartAudioEngine,
                        AppStrings.Console.RestartReasons.DeviceChange));
                    AudioSyncController.Instance.GetVoicemeeterConnection()?.SendCommand("Restart", 1);
                });
            }
        };

        WindowsAudioScanner.Instance.AnyDeviceChanged += (_, devices) =>
        {
            if (_settings.IsToggleChecked("restart_audio_engine_on_any_device_change") && devices.New > 0)
            {
                Task.Delay(1000).ContinueWith(_ =>
                {
                    System.Console.WriteLine(string.Format(AppStrings.Console.RestartAudioEngine,
                        AppStrings.Console.RestartReasons.AnyDeviceChange));
                    System.Console.WriteLine($"{AppStrings.Console.DeviceMessages.Added} {string.Join(", ", devices.Added)}");
                    System.Console.WriteLine($"{AppStrings.Console.DeviceMessages.Removed} {string.Join(", ", devices.Removed)}");
                    AudioSyncController.Instance.GetVoicemeeterConnection()?.SendCommand("Restart", 1);
                });
            }
        };

        Workers.WindowsEventScanner.Instance.Resume += (_, _) => RestartVmForReason(AppStrings.Console.RestartReasons.Resume);
        Workers.WindowsEventScanner.Instance.ModernResume += (_, _) => RestartVmForReason(AppStrings.Console.RestartReasons.ModernResume);
        Workers.WindowsEventScanner.Instance.MonitorResume += (_, _) => RestartVmForReason(AppStrings.Console.RestartReasons.MonitorResume);

        return sub;
    }

    private void RestartVmForReason(string reason)
    {
        Task.Delay(1000).ContinueWith(_ =>
        {
            System.Console.WriteLine(string.Format(AppStrings.Console.RestartAudioEngine, reason));
            AudioSyncController.Instance.GetVoicemeeterConnection()?.SendCommand("Restart", 1);

            if (_settings.IsToggleChecked("apply_crackle_fix"))
            {
                Task.Delay(3000).ContinueWith(__ => ApplyCrackleFix(true));
            }
        });
    }

    private ToolStripMenuItem BuildPatchesMenu()
    {
        var sub = new ToolStripMenuItem(AppStrings.MenuItems.ListPatches);

        sub.DropDownItems.Add(new ToolStripMenuItem(AppStrings.MenuItems.TitleSettings) { Enabled = false });

        sub.DropDownItems.Add(ToggleItem(
            sid: "start_with_windows",
            title: AppStrings.MenuItems.StartWithWindows,
            defaultChecked: AutoStartController.IsEnabled(),
            onActivate: checked_ =>
            {
                if (checked_) AutoStartController.EnableStartOnLaunch();
                else AutoStartController.DisableStartOnLaunch();
            },
            initIfChecked: false
        ));

        string FormatLimitLabel(float v) => string.Format(AppStrings.MenuItems.LimitDbGain, v.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));

        _limitToggleItem = ToggleItem(
            sid: "limit_db_gain_to_0",
            title: FormatLimitLabel(_settings.GetSettings().LimitDbGainValue),
            onActivate: checked_ =>
                System.Console.WriteLine(checked_ ? "Max gain limiting enabled" : "Max gain limiting disabled")
        );
        sub.DropDownItems.Add(_limitToggleItem);

        sub.DropDownItems.Add(ItemSetDbLimit(_limitToggleItem, FormatLimitLabel));

        sub.DropDownItems.Add(ToggleItem(
            sid: "linear_volume_scale",
            title: AppStrings.MenuItems.LinearVolumeScale,
            onActivate: checked_ =>
                System.Console.WriteLine(checked_ ? "Now using linear volume scaling" : "Now using logarithmic volume scaling")
        ));

        sub.DropDownItems.Add(ToggleItem(
            sid: "sync_mute",
            title: AppStrings.MenuItems.SyncMute,
            defaultChecked: true,
            onActivate: checked_ =>
                System.Console.WriteLine(checked_ ? "Syncing mute" : "No longer syncing mute")
        ));

        sub.DropDownItems.Add(new ToolStripSeparator());
        sub.DropDownItems.Add(new ToolStripMenuItem(AppStrings.MenuItems.TitleDriverWorkarounds) { Enabled = false });

        sub.DropDownItems.Add(ToggleItem(
            sid: "remember_volume",
            title: AppStrings.MenuItems.RestoreVolume,
            onActivate: checked_ =>
            {
                if (checked_) AudioSyncController.Instance.RememberCurrentVolume();
            }
        ));

        sub.DropDownItems.Add(ToggleItem(
            sid: "apply_volume_fix",
            title: AppStrings.MenuItems.PreventVolumeSpikes
        ));

        sub.DropDownItems.Add(ToggleItem(
            sid: "apply_crackle_fix",
            title: AppStrings.MenuItems.CrackleFix,
            onActivate: checked_ => ApplyCrackleFix(checked_),
            initIfChecked: true
        ));

        return sub;
    }

    private void ApplyCrackleFix(bool enabled)
    {
        var settings = _settings.GetSettings();
        int priority = settings.Audiodg?.Priority ?? ProcessController.Priorities.High;
        int affinity = settings.Audiodg?.Affinity ?? 2;

        if (enabled)
        {
            System.Console.WriteLine($"Setting audiodg.exe priority to {priority} and affinity to {affinity}");
            ProcessController.SetProcessPriority("audiodg", priority);
            ProcessController.SetProcessAffinity("audiodg", affinity);
        }
        else
        {
            System.Console.WriteLine($"Restoring audiodg.exe priority to {ProcessController.Priorities.Normal} and affinity to 255");
            ProcessController.SetProcessPriority("audiodg", ProcessController.Priorities.Normal);
            ProcessController.SetProcessAffinity("audiodg", 255);
        }
    }

    private ToolStripMenuItem ItemSetDbLimit(ToolStripMenuItem limitToggle, Func<float, string> formatLimitLabel)
    {
        var item = new ToolStripMenuItem(AppStrings.MenuItems.SetDbLimit);
        item.Click += (_, _) =>
        {
            var current = _settings.GetSettings();
            float? val = ShowInputDialog(current.LimitDbGainValue);
            if (val == null) return;
            current.LimitDbGainValue = val.Value;
            _settings.SetSettings(current);
            _settings.SaveSettings();
            limitToggle.Text = formatLimitLabel(val.Value);
            System.Console.WriteLine($"dB gain limit set to {val.Value} dB");
        };
        return item;
    }

    private static float? ShowInputDialog(float currentValue)
    {
        const int MinDb = -60;
        const int MaxDb = 12;

        using var form = new Form
        {
            Width = 380,
            Height = 210,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Text = "Set dB Gain Limit",
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            BackColor = Color.FromArgb(245, 245, 245),
        };

        int initialTick = Math.Clamp((int)Math.Round(currentValue), MinDb, MaxDb);

        var valueLabel = new Label
        {
            Left = 12, Top = 14, Width = 344, Height = 38,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
        };

        var slider = new TrackBar
        {
            Left = 6, Top = 58, Width = 356,
            Minimum = MinDb, Maximum = MaxDb,
            TickFrequency = 12, SmallChange = 1, LargeChange = 6,
            Value = initialTick,
        };

        var warning = new Label
        {
            Left = 12, Top = 118, Width = 348, Height = 20,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 8.25f),
            Text = "High gain levels may cause hearing damage",
        };

        var ok = new Button { Text = "OK", Left = 176, Top = 140, Width = 84, Height = 28,
            DialogResult = DialogResult.OK, FlatStyle = FlatStyle.System };
        var cancel = new Button { Text = "Cancel", Left = 271, Top = 140, Width = 84, Height = 28,
            DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.System };

        void Refresh(int db)
        {
            valueLabel.Text = $"{(db > 0 ? "+" : "")}{db} dB";
            if (db > 6)
            {
                valueLabel.ForeColor = Color.Firebrick;
                warning.ForeColor = Color.Firebrick;
                warning.Visible = true;
            }
            else if (db > 0)
            {
                valueLabel.ForeColor = Color.DarkOrange;
                warning.ForeColor = Color.DarkOrange;
                warning.Visible = true;
            }
            else
            {
                valueLabel.ForeColor = Color.FromArgb(20, 120, 20);
                warning.Visible = false;
            }
        }

        Refresh(initialTick);
        slider.ValueChanged += (_, _) => Refresh(slider.Value);

        form.Controls.AddRange(new Control[] { valueLabel, slider, warning, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog() == DialogResult.OK ? (float)slider.Value : null;
    }



    private static ToolStripMenuItem ItemShowVoicemeeter()
    {
        var item = new ToolStripMenuItem(AppStrings.MenuItems.ShowVoicemeeter);
        item.Click += (_, _) =>
        {
            string? procName = ProcessController.GetRunningProcess(@"voicemeeter(?!.*setup).*\.exe");
            if (procName != null)
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(
                    System.IO.Path.GetFileNameWithoutExtension(procName));
                
                if (processes.Length > 0)
                {
                    var process = processes[0];
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(process.MainWindowHandle, SW_SHOW);
                        SetForegroundWindow(process.MainWindowHandle);
                    }
                }
            }
        };
        return item;
    }

    private static ToolStripMenuItem ItemRestartVoicemeeter()
    {
        var item = new ToolStripMenuItem(AppStrings.MenuItems.RestartVoicemeeter);
        item.Click += (_, _) =>
        {
            string? proc = ProcessController.GetRunningProcess(@"voicemeeter(?!.*setup).*\.exe");
            if (proc != null) ProcessController.RestartProcess(proc);

            Task.Delay(7000).ContinueWith(_ =>
            {
                if (SettingsController.Instance.IsToggleChecked("restart_audio_engine_on_app_launch"))
                {
                    System.Console.WriteLine(string.Format(
                        AppStrings.Console.RestartAudioEngine,
                        AppStrings.Console.RestartReasons.AppLaunch));
                    AudioSyncController.Instance.GetVoicemeeterConnection()?.SendCommand("Restart", 1);
                }
            });
        };
        return item;
    }

    private static ToolStripMenuItem ItemRestartAudioEngine()
    {
        var item = new ToolStripMenuItem(AppStrings.MenuItems.RestartAudioEngine);
        item.Click += (_, _) =>
        {
            var vm = AudioSyncController.Instance.GetVoicemeeterConnection();
            if (vm != null)
            {
                System.Console.WriteLine(string.Format(
                    AppStrings.Console.RestartAudioEngine,
                    AppStrings.Console.RestartReasons.UserInput));
                vm.SendCommand("Restart", 1);
            }
        };
        return item;
    }

    private ToolStripMenuItem ItemDonate()
    {
        var item = new ToolStripMenuItem(AppStrings.MenuItems.Donate);
        item.Click += (_, _) =>
            PowerShellRunner.Run("Start-Process \"https://www.paypal.com/donate?hosted_button_id=JBDM2H96RNKH8\"");

        if (_settings.GetSettings().DisableDonate)
            item.Visible = false;

        return item;
    }

    private static ToolStripMenuItem ItemDonateKofi()
    {
        var item = new ToolStripMenuItem("Support on Ko-fi (Fork)");
        item.Click += (_, _) =>
            PowerShellRunner.Run("Start-Process \"https://ko-fi.com/paraxdev\"");
        return item;
    }

    private static ToolStripMenuItem ItemVisitGithub()
    {
        var item = new ToolStripMenuItem(AppStrings.MenuItems.VisitGithub);
        item.Click += (_, _) =>
            PowerShellRunner.Run("Start-Process \"https://github.com/Frosthaven/voicemeeter-windows-volume\"");
        return item;
    }

    private static ToolStripMenuItem ItemVisitGithubFork()
    {
        var item = new ToolStripMenuItem("Visit Github (Fork)");
        item.Click += (_, _) =>
            PowerShellRunner.Run("Start-Process \"https://github.com/Paraxdev/voicemeeter-windows-volume-csharp\"");
        return item;
    }

    private static ToolStripMenuItem ItemOpenApplicationFolder()
    {
        var item = new ToolStripMenuItem(AppStrings.MenuItems.OpenApplicationFolder);
        item.Click += (_, _) =>
            System.Diagnostics.Process.Start("explorer.exe", AppContext.BaseDirectory);
        return item;
    }

    private static ToolStripMenuItem ItemExit()
    {
        var item = new ToolStripMenuItem(AppStrings.MenuItems.Exit);
        item.Click += (_, _) => Application.Exit();
        return item;
    }

    private ToolStripMenuItem ToggleItem(
        string sid,
        string title,
        bool defaultChecked = false,
        Action<bool>? onActivate = null,
        bool initIfChecked = false)
    {
        var item = new ToolStripMenuItem(title) { CheckOnClick = true, Checked = defaultChecked };
        _toggleItems[sid] = item;

        item.Click += (_, _) =>
        {
            _settings.UpdateToggle(sid, item.Checked);
            onActivate?.Invoke(item.Checked);
        };

        if (initIfChecked)
        {
            bool saved = _settings.IsToggleChecked(sid);
            if (saved) onActivate?.Invoke(true);
        }

        return item;
    }

    private void OnToggleChanged(string sid, bool value)
    {
        _settings.UpdateToggle(sid, value);
    }

    public void ApplySavedToggles()
    {
        var settings = _settings.GetSettings();
        foreach (var toggle in settings.Toggles)
        {
            if (_toggleItems.TryGetValue(toggle.Setting, out var menuItem))
                menuItem.Checked = toggle.Value;
            if (_bindingItems.TryGetValue(toggle.Setting, out var bindItem))
                bindItem.Checked = toggle.Value;
        }

        if (_limitToggleItem != null)
            _limitToggleItem.Text = string.Format(AppStrings.MenuItems.LimitDbGain,
                settings.LimitDbGainValue.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
    }

    private System.Threading.Timer? _bindingDebounceTimer;

    private void RefreshBindingLabels()
    {
        _bindingDebounceTimer?.Dispose();
        _bindingDebounceTimer = new System.Threading.Timer(_ =>
        {
            var vm = AudioSyncController.Instance.GetVoicemeeterConnection();
            if (vm == null) return;

            var strips = AppStrings.VoicemeeterFriendlyNames.VoicemeeterStrips.GetValueOrDefault(vm.VmType);
            var buses = AppStrings.VoicemeeterFriendlyNames.VoicemeeterBuses.GetValueOrDefault(vm.VmType);

            if (strips == null || buses == null) return;

            _syncForm?.BeginInvoke(() =>
            {
                foreach (var (sid, item) in _bindingItems)
                {
                    var tokens = sid.Split('_');
                    if (tokens.Length != 2 || !int.TryParse(tokens[1], out int idx)) continue;

                    string type = tokens[0];
                    var names = type == "Strip" ? strips : buses;

                    if (idx < names.Count)
                    {
                        string label = vm.GetParameterString(type, idx, "Label");
                        string deviceName = vm.GetParameterString(type, idx, "device.name");
                        string friendlyName = string.IsNullOrEmpty(label) ? names[idx] : label;
                        string full = string.IsNullOrEmpty(deviceName)
                            ? friendlyName
                            : $"{friendlyName} : <{deviceName}>";

                        item.Text = full;
                        item.Visible = true;
                    }
                    else
                    {
                        item.Visible = false;
                    }
                }
            });
        }, null, 5000, Timeout.Infinite);
    }

    public void Dispose()
    {
        _notifyIcon?.Dispose();
        _contextMenu?.Dispose();
        _bindingDebounceTimer?.Dispose();
        _syncForm?.Dispose();
    }
}
