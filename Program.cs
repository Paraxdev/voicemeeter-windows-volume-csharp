using VoicemeeterWindowsVolume.Controllers;
using VoicemeeterWindowsVolume.Models;
using VoicemeeterWindowsVolume.Views;

namespace VoicemeeterWindowsVolume;

internal static class Program
{
    private static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VMWV");

    private static readonly string SettingsPath =
        Path.Combine(DataDir, "settings.json");

    private static readonly string LogPath =
        Path.Combine(DataDir, "vmwv-crash.log");

    private static readonly string AppLogPath =
        Path.Combine(DataDir, "vmwv.log");

    [STAThread]
    static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogFatal(e.ExceptionObject?.ToString() ?? "Unknown error");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogFatal($"Unobserved task exception: {e.Exception}");
            e.SetObserved();
        };

        try
        {
            Directory.CreateDirectory(DataDir);

            var logWriter = new TimestampedFileWriter(AppLogPath);
            Console.SetOut(logWriter);

            ApplicationConfiguration.Initialize();

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
            {
                LogFatal($"UI thread exception: {e.Exception}");
                MessageBox.Show(e.Exception.Message, AppStrings.FriendlyName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            System.Console.WriteLine(
                $"Voicemeeter Windows Volume started, Process ID: {Environment.ProcessId}");

            string iconColor = GetSystemColor();

            TrayViewController.Instance.Initialize(iconColor);

            SettingsController.Instance.LoadSettings(
                settingsPath: SettingsPath,
                defaults: new AppSettings(),
                callback: () =>
                {
                    TrayViewController.Instance.ApplySavedToggles();
                    System.Console.WriteLine("Starting audio synchronization");
                    AudioSyncController.Instance.StartAudioSync();
                }
            );

            var ctx = new TrayApplicationContext();
            Application.Run(ctx);
        }
        catch (Exception ex)
        {
            LogFatal(ex.ToString());
            MessageBox.Show(ex.Message, AppStrings.FriendlyName + " - Fatal Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void LogFatal(string message)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
        System.Console.WriteLine(message);
    }

    private static string GetSystemColor()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int val)
                return val == 0 ? "dark" : "light";
        }
        catch (Exception ex) { System.Console.WriteLine($"[Program] Failed to read system theme from registry: {ex.Message}"); }
        return "default";
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    public TrayApplicationContext()
    {
        Application.ApplicationExit += (_, _) => Cleanup();
    }

    private static void Cleanup()
    {
        try
        {
            AudioSyncController.Instance.Disconnect();
            PowerShellRunner.StopAllWorkers();
            TrayViewController.Instance.Dispose();
            System.Console.WriteLine("clean exit");
        }
        catch (Exception ex) { System.Console.WriteLine($"[Program] Cleanup error: {ex.Message}"); }
    }
}

internal sealed class TimestampedFileWriter : TextWriter
{
    private readonly StreamWriter _writer;

    public TimestampedFileWriter(string path)
    {
        _writer = new StreamWriter(path, append: false, encoding: System.Text.Encoding.UTF8)
        {
            AutoFlush = true,
        };
    }

    public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

    public override void WriteLine(string? value)
        => _writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {value}");

    public override void Write(string? value)
        => _writer.Write(value);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _writer.Dispose();
        base.Dispose(disposing);
    }
}
