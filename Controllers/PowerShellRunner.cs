using System.Diagnostics;

namespace VoicemeeterWindowsVolume.Controllers;

public static class PowerShellRunner
{
    private static readonly Dictionary<string, Process> _hosts = new();
    private static readonly Dictionary<string, System.Threading.Timer> _workers = new();

    public static void Run(string command, Action<string>? callback = null, bool logOutput = false)
    {
        string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command));

        var psi = new ProcessStartInfo("powershell.exe")
        {
            Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encoded}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var proc = new Process { StartInfo = psi };
        proc.Start();

        string output = proc.StandardOutput.ReadToEnd();
        string error = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        if (logOutput && !string.IsNullOrEmpty(output))
            System.Console.WriteLine(output);

        callback?.Invoke(output);
    }

    public static Process CreateHost(string label, Action<List<string>>? onResponse)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            Arguments = "-Mta -NoProfile",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Start();
        proc.StandardInput.AutoFlush = true;

        _hosts[label] = proc;
        System.Console.WriteLine($"Started PowerShell worker \"{label}\" PID: {proc.Id}");

        Task.Run(() =>
        {
            var streamed = new List<string>();
            bool capturing = false;
            var startPattern = "{{" + label + ":start}}";
            var endPattern = "{{" + label + ":end}}";

            while (!proc.StandardOutput.EndOfStream)
            {
                string? raw = proc.StandardOutput.ReadLine();
                if (raw == null) continue;
                string line = raw.Trim();
                if (line.Length == 0) continue;

                if (line == startPattern)
                {
                    capturing = true;
                    streamed.Clear();
                }
                else if (line == endPattern)
                {
                    capturing = false;
                    onResponse?.Invoke(new List<string>(streamed));
                    streamed.Clear();
                }
                else if (capturing)
                {
                    streamed.Add(line);
                }
            }
        });

        return proc;
    }

    public static void StartWorker(string label, string command, int intervalMs,
        Action<List<string>>? onResponse, string? setup = null)
    {
        if (_hosts.ContainsKey(label) || _workers.ContainsKey(label)) return;

        var proc = CreateHost(label, onResponse);

        if (!string.IsNullOrEmpty(setup))
        {
            proc.StandardInput.WriteLine(setup);
            proc.StandardInput.Flush();
        }

        string formattedCmd = FormatCommand(command);

        string startMarker = "echo \"{{" + label + ":start}}\"; ";
        string endMarker = "; echo \"{{" + label + ":end}}\"";
        string fullCmd = startMarker + formattedCmd + endMarker;

        int initialDelay = string.IsNullOrEmpty(setup) ? intervalMs : 5000;

        var timer = new System.Threading.Timer(_ =>
        {
            try
            {
                if (!proc.HasExited)
                    proc.StandardInput.WriteLine(fullCmd);
            }
            catch (Exception ex) { System.Console.WriteLine($"[PowerShellRunner] Failed to send command to worker \"{label}\": {ex.Message}"); }
        }, null, initialDelay, intervalMs);

        _workers[label] = timer;
    }

    public static void SendToWorker(string label, string command)
    {
        if (_hosts.TryGetValue(label, out var proc) && !proc.HasExited)
            proc.StandardInput.WriteLine(command);
    }

    public static void StopWorker(string label)
    {
        if (_workers.TryGetValue(label, out var timer))
        {
            timer.Dispose();
            _workers.Remove(label);
        }
        if (_hosts.TryGetValue(label, out var proc))
        {
            try { if (!proc.HasExited) proc.Kill(); } catch (Exception ex) { System.Console.WriteLine($"[PowerShellRunner] Failed to kill worker \"{label}\": {ex.Message}"); }
            _hosts.Remove(label);
            System.Console.WriteLine($"Killed PowerShell worker \"{label}\"");
        }
    }

    public static void StopAllWorkers()
    {
        foreach (var label in _hosts.Keys.ToList())
            StopWorker(label);
    }

    private static string FormatCommand(string cmd)
        => cmd.ReplaceLineEndings(" ").Trim();
}
