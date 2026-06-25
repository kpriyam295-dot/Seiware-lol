// Program.cs — Entry point for BOTH "Command Prompt.exe" AND "Windows PowerShell.exe"
//
// Auto-detects shell type from its own exe filename:
//   "Windows PowerShell.exe" or "powershell.exe" → PowerShell mode
//   Anything else → CMD mode
//
// Can be overridden with --shell ps/cmd argument.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CommandPrompt
{
    class Program
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool SetConsoleTitle(string title);

        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                bool adminMode = args.Any(a =>
                    a.Equals("--admin", StringComparison.OrdinalIgnoreCase));

                ShellType shellType = DetectShellTypeFromExeName();

                // Set the title IMMEDIATELY — before the console window renders
                // any default text. This eliminates the flash of the exe path.
                string earlyTitle = shellType == ShellType.PowerShell
                    ? (adminMode ? "Administrator: Windows PowerShell" : "Windows PowerShell")
                    : (adminMode ? "Administrator: Command Prompt"     : "Command Prompt");
                try { SetConsoleTitle(earlyTitle); } catch { }

                string? titleImage = null;
                string? iconPath   = null;
                string? configPath = null;
                string? pipeName   = null;
                Regex?  censorRegex = null;

                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i].Equals("--shell", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        string val = args[i + 1].Trim('"').ToLowerInvariant();
                        shellType = val switch
                        {
                            "ps" or "powershell" or "pwsh" => ShellType.PowerShell,
                            _ => ShellType.Cmd,
                        };
                        i++;
                    }
                    else if (args[i].Equals("--banned", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        string pattern = args[i + 1].Trim('"');
                        if (!string.IsNullOrWhiteSpace(pattern))
                            try { censorRegex = new Regex("(" + pattern + ")", RegexOptions.IgnoreCase); } catch { }
                        i++;
                    }
                    else if (args[i].Equals("--icon", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        iconPath = args[i + 1].Trim('"'); i++;
                    }
                    else if (args[i].Equals("--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        configPath = args[i + 1].Trim('"'); i++;
                    }
                    else if (args[i].Equals("--pipe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        pipeName = args[i + 1].Trim('"'); i++;
                    }
                    else if (!args[i].StartsWith("-") && !args[i].StartsWith("/"))
                    {
                        if (titleImage == null && (args[i].EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                   args[i].EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                                   args[i].EndsWith(".ico", StringComparison.OrdinalIgnoreCase)))
                            titleImage = args[i];
                    }
                }

                if (censorRegex == null)
                    censorRegex = LoadCensorFromConfig(configPath);

                using var terminal = new FakeTerminal(adminMode, shellType, titleImage, censorRegex, iconPath, pipeName);
                return terminal.Run();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Fatal error: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        static ShellType DetectShellTypeFromExeName()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                    exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                    return ShellType.Cmd;

                string exeName = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
                if (exeName.Contains("powershell") || exeName.Contains("pwsh"))
                    return ShellType.PowerShell;
            }
            catch { }
            return ShellType.Cmd;
        }

        static Regex? LoadCensorFromConfig(string? explicitConfigPath)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(explicitConfigPath)) candidates.Add(explicitConfigPath);
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Seiware", "config.json"));
            try
            {
                string usersDir = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? @"C:\Users";
                foreach (var userDir in Directory.GetDirectories(usersDir))
                {
                    string candidate = Path.Combine(userDir, "AppData", "Roaming", "Seiware", "config.json");
                    if (!candidates.Contains(candidate)) candidates.Add(candidate);
                }
            }
            catch { }
            foreach (var path in candidates)
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    if (doc.RootElement.TryGetProperty("BannedNames", out var b) && b.ValueKind == JsonValueKind.Array)
                    {
                        var names = new List<string>();
                        foreach (var item in b.EnumerateArray())
                        {
                            string? v = item.GetString();
                            if (!string.IsNullOrWhiteSpace(v)) names.Add(Regex.Escape(v));
                        }
                        if (names.Count > 0) return new Regex("(" + string.Join("|", names) + ")", RegexOptions.IgnoreCase);
                    }
                }
                catch { }
            }
            return null;
        }
    }
}
