// Program.cs — Entry point for BOTH "Command Prompt.exe" AND "Windows PowerShell.exe"
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
                bool adminMode = args.Any(a => a.Equals("--admin", StringComparison.OrdinalIgnoreCase));
                ShellType shellType = DetectShellTypeFromExeName();
                string earlyTitle = shellType == ShellType.PowerShell
                    ? (adminMode ? "Administrator: Windows PowerShell" : "Windows PowerShell")
                    : (adminMode ? "Administrator: Command Prompt" : "Command Prompt");
                try { SetConsoleTitle(earlyTitle); } catch { }

                string? titleImage = null; string? iconPath = null; string? configPath = null; string? pipeName = null; Regex? censorRegex = null;

                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i].Equals("--pipe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) { pipeName = args[++i]; }
                    else if (args[i].Equals("--icon", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) { iconPath = args[++i]; }
                    else if (args[i].Equals("--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) { configPath = args[++i]; }
                    else if (args[i].Equals("--shell", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        shellType = args[++i].ToLowerInvariant() switch { "ps" => ShellType.PowerShell, "powershell" => ShellType.PowerShell, _ => ShellType.Cmd, };
                    }
                    else if (args[i].Equals("--banned", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        string pattern = args[++i];
                        if (!string.IsNullOrWhiteSpace(pattern))
                        {
                            // Each banned name is separated by |, but may contain paths with backslashes
                            // that are NOT regex escapes. Escape each part individually.
                            var parts = pattern.Split('|', StringSplitOptions.RemoveEmptyEntries);
                            var escaped = parts.Select(p => Regex.Escape(p.Trim()));
                            string joined = string.Join("|", escaped);
                            if (!string.IsNullOrEmpty(joined))
                                censorRegex = new Regex("(" + joined + ")", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                        }
                    }
                    else if (!args[i].StartsWith("--") && string.IsNullOrEmpty(titleImage)) { titleImage = args[i]; }
                }

                if (censorRegex == null) censorRegex = LoadCensorFromConfig(configPath);
                var terminal = new FakeTerminal(adminMode, shellType, titleImage, censorRegex, iconPath, pipeName);
                return terminal.Run();
            }
            catch (Exception ex) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"Fatal error: {ex.Message}"); Console.ResetColor(); Console.WriteLine("\nPress any key to exit..."); Console.ReadKey(true); return 1; }
        }

        static ShellType DetectShellTypeFromExeName()
        {
            try { string? exePath = Environment.ProcessPath; if (string.IsNullOrEmpty(exePath)) exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return ShellType.Cmd;
                string exeName = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
                if (exeName.Contains("powershell") || exeName.Contains("pwsh")) return ShellType.PowerShell;
            } catch { } return ShellType.Cmd;
        }

        static Regex? LoadCensorFromConfig(string? explicitConfigPath)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(explicitConfigPath)) candidates.Add(explicitConfigPath);
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DreamLand", "config.json"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Seiware", "config.json"));
            try { string usersDir = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? @"C:\Users";
                foreach (var userDir in Directory.GetDirectories(usersDir)) { string candidate = Path.Combine(userDir, "AppData", "Roaming", "DreamLand", "config.json"); if (!candidates.Contains(candidate)) candidates.Add(candidate); }
            } catch { }
            foreach (var path in candidates)
            {
                try { if (!File.Exists(path)) continue; using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    if (doc.RootElement.TryGetProperty("BannedNames", out var b) && b.ValueKind == JsonValueKind.Array)
                    { var names = new List<string>(); foreach (var item in b.EnumerateArray()) { string? v = item.GetString(); if (!string.IsNullOrWhiteSpace(v)) names.Add(Regex.Escape(v)); }
                        if (names.Count > 0) return new Regex("(" + string.Join("|", names) + ")", RegexOptions.IgnoreCase); }
                } catch { }
            }
            return null;
        }
    }
}
