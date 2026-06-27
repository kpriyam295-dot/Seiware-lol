// Seiware.cs  —  Integrated Seiware + FakeTerminal controller
// PATCHED: ShellInterceptor intercepts BOTH cmd.exe and powershell.exe/pwsh.exe
//          HeadlessShellHost hosts cmd.exe OR powershell.exe on the backend
//          Unified pipe architecture for all 4 combos: CMD/PS × Normal/Admin

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Management;

enum ShellType { Cmd, PowerShell }

// ════════════════════════════════════════════════════════════════════════════
// SHELL INTERCEPTOR — watches for cmd.exe AND powershell.exe/pwsh.exe
// ════════════════════════════════════════════════════════════════════════════
class ShellInterceptor : IDisposable
{
    private const int KillDelayMs = 120;
    private ManagementEventWatcher? _watcher;
    private readonly int _ownPid;
    private bool _disposed, _enabled;
    private readonly HashSet<int> _exemptPids = new();
    private readonly object _exemptLock = new();
    private volatile int _suppressUntil = 0; // Environment.TickCount when suppression ends

    public Action<ShellType>? OnInterceptNormal { get; set; }
    public Action<ShellType>? OnInterceptAdmin  { get; set; }
    public Action? OnControlPanelBlocked { get; set; }
    public bool IsEnabled => _enabled;

    /// <summary>Suppress ALL interception for the next N milliseconds.
    /// Use before launching runas/cmd that may trigger the interceptor.</summary>
    public void SuppressFor(int ms) { _suppressUntil = Environment.TickCount + ms; }

    public ShellInterceptor() { _ownPid = Process.GetCurrentProcess().Id; }

    public void RegisterExemptPid(int pid)
    {
        lock (_exemptLock) {
            _exemptPids.Add(pid);
            _exemptPids.RemoveWhere(p => { try { Process.GetProcessById(p); return false; } catch { return true; } });
        }
    }

    public void Start()
    {
        if (_enabled || _disposed) return;
        try {
            // Watch for cmd.exe, powershell.exe, pwsh.exe, and direct log viewers.
            // IMPORTANT: do NOT watch generic mmc.exe — Device Manager and other
            // snap-ins must keep working normally.
            var query = new WqlEventQuery("__InstanceCreationEvent", TimeSpan.FromSeconds(0.1),
                "TargetInstance ISA 'Win32_Process' AND (" +
                "TargetInstance.Name = 'cmd.exe' OR " +
                "TargetInstance.Name = 'powershell.exe' OR " +
                "TargetInstance.Name = 'pwsh.exe' OR " +
                "TargetInstance.Name = 'eventvwr.exe' OR " +
                "TargetInstance.Name = 'perfmon.exe' OR " +
                "TargetInstance.Name = 'wercon.exe')");
            _watcher = new ManagementEventWatcher(query);
            _watcher.EventArrived += OnShellCreated;
            _watcher.Start();
            _enabled = true;
        } catch { _enabled = false; }
    }

    public void Stop() { if (!_enabled) return; _enabled = false; try { _watcher?.Stop(); } catch { } }

    private void OnShellCreated(object sender, EventArrivedEventArgs e)
    {
        try {
            // Check suppression window (prevents infinite loop when Seiware itself spawns cmd/ps)
            if (Environment.TickCount < _suppressUntil) return;

            var instance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            int newPid = Convert.ToInt32(instance["ProcessId"]);
            int parentPid = Convert.ToInt32(instance["ParentProcessId"]);
            string procName = (instance["Name"]?.ToString() ?? "").ToLowerInvariant();

            if (IsAncestorExempt(parentPid)) return;
            if (newPid <= 0) return;

            // Direct log/crash viewers: kill silently, don't open anything.
            // MMC itself is left alone so Device Manager / Disk Management / etc.
            // continue to work.
            if (procName == "eventvwr.exe" || procName == "perfmon.exe" || procName == "wercon.exe")
            {
                Thread.Sleep(50);
                try { var p = Process.GetProcessById(newPid); if (!p.HasExited) p.Kill(); } catch { }
                OnControlPanelBlocked?.Invoke();
                return;
            }

            // Determine shell type from process name
            ShellType shellType = procName switch
            {
                "powershell.exe" => ShellType.PowerShell,
                "pwsh.exe"       => ShellType.PowerShell,
                _                => ShellType.Cmd,
            };

            Thread.Sleep(50);
            bool isAdminLaunch = IsProcessElevated(newPid);
            if (!isAdminLaunch)
            {
                try {
                    var parent = Process.GetProcessById(parentPid);
                    string parentName = parent.ProcessName.ToLowerInvariant();
                    if (parentName != "explorer" && parentName != "cmd" &&
                        parentName != "powershell" && parentName != "pwsh" &&
                        parentName != "windowsterminal")
                        isAdminLaunch = true;
                } catch { }
            }

            // Kill the real shell
            try {
                var proc = Process.GetProcessById(newPid);
                if (!proc.HasExited) {
                    int waited = 0;
                    while (proc.MainWindowHandle == IntPtr.Zero && waited < KillDelayMs)
                    { Thread.Sleep(10); waited += 10; proc.Refresh(); }
                    if (proc.MainWindowHandle != IntPtr.Zero)
                        ShowWindow(proc.MainWindowHandle, SW_MINIMIZE);
                    Thread.Sleep(80);
                    if (!proc.HasExited) proc.Kill();
                }
            } catch { }

            if (isAdminLaunch)
                OnInterceptAdmin?.Invoke(shellType);
            else
                OnInterceptNormal?.Invoke(shellType);
        } catch { }
    }

    private bool IsAncestorExempt(int pid)
    {
        Dictionary<int, int> parentMap;
        try {
            parentMap = new();
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId FROM Win32_Process");
            foreach (ManagementObject obj in searcher.Get())
                parentMap[Convert.ToInt32(obj["ProcessId"])] = Convert.ToInt32(obj["ParentProcessId"]);
        } catch { return false; }
        HashSet<int> exempt; lock (_exemptLock) { exempt = new HashSet<int>(_exemptPids); } exempt.Add(_ownPid);
        int cur = pid; int depth = 0;
        while (cur > 0 && depth++ < 16) { if (exempt.Contains(cur)) return true; if (!parentMap.TryGetValue(cur, out int parent) || parent == cur) break; cur = parent; }
        return false;
    }

    private static bool IsProcessElevated(int pid)
    {
        IntPtr hProcess = IntPtr.Zero, hToken = IntPtr.Zero;
        try {
            hProcess = OpenProcess(0x0400 | 0x0010, false, pid); if (hProcess == IntPtr.Zero) return false;
            if (!OpenProcessToken(hProcess, 0x0008, out hToken)) return false;
            GetTokenInformation(hToken, 25, IntPtr.Zero, 0, out uint size); if (size == 0) return false;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try {
                if (!GetTokenInformation(hToken, 25, buf, size, out _)) return false;
                IntPtr pSid = Marshal.ReadIntPtr(buf);
                int subCount = Marshal.ReadByte(GetSidSubAuthorityCount(pSid));
                uint rid = (uint)Marshal.ReadInt32(GetSidSubAuthority(pSid, (uint)(subCount - 1)));
                return rid >= 0x3000;
            } finally { Marshal.FreeHGlobal(buf); }
        } catch { return false; }
        finally { if (hToken != IntPtr.Zero) CloseHandle(hToken); if (hProcess != IntPtr.Zero) CloseHandle(hProcess); }
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    private const int SW_HIDE = 0, SW_SHOW = 5, SW_MINIMIZE = 6, SW_RESTORE = 9;

    /// <summary>Show or hide a process's main window by pid.
    /// Used so Seiware can hide the fake terminal while it starts up,
    /// then reveal it only after the pipe is connected.</summary>
    static void ShowProcessWindow(Process? p, bool show)
    {
        if (p == null) return;
        try
        {
            if (p.HasExited) return;
            // Give the process a moment to create its main window
            for (int i = 0; i < 8; i++)
            {
                if (p.MainWindowHandle != IntPtr.Zero) break;
                p.Refresh();
                Thread.Sleep(40);
            }
            if (p.MainWindowHandle == IntPtr.Zero) return;
            ShowWindow(p.MainWindowHandle, show ? SW_SHOW : SW_HIDE);
            if (show) { ShowWindow(p.MainWindowHandle, SW_RESTORE); SetForegroundWindow(p.MainWindowHandle); }
        }
        catch { }
    }
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr hProcess, uint access, out IntPtr hToken);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr hToken, int infoClass, IntPtr tokenInfo, uint tokenInfoLen, out uint returnLen);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthorityCount(IntPtr pSid);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthority(IntPtr pSid, uint subAuthority);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    public void Dispose() { if (_disposed) return; _disposed = true; Stop(); try { _watcher?.Dispose(); } catch { } _watcher = null; }
}

// ════════════════════════════════════════════════════════════════════════════
// ENTRY POINT
// ════════════════════════════════════════════════════════════════════════════
static class Program
{
    static Mutex? singleInstanceMutex;
    [STAThread]
    static void Main(string[] args)
    {
        bool createdNew;
        singleInstanceMutex = new Mutex(true, "Global\\SeiwareSingleInstance", out createdNew);
        if (!createdNew) { MessageBox.Show("Seiware is already running.\nCheck the system tray.", "Already Running", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        bool silent = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));
        PsEmbedder.WriteAll();
        if (!StorageUtil.FileExists(AppConfig.ConfigPath)) { var wizard = new SetupWizard(); if (wizard.ShowDialog() != DialogResult.OK) { singleInstanceMutex.ReleaseMutex(); return; } }
        Application.Run(new MainForm(silent));
        singleInstanceMutex.ReleaseMutex();
    }
}

// ════════════════════════════════════════════════════════════════════════════
static class NativeWin32
{
    [DllImport("kernel32.dll")] public static extern bool AttachConsole(uint dwProcessId);
    [DllImport("kernel32.dll")] public static extern bool FreeConsole();
    [DllImport("kernel32.dll")] public static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);
    [DllImport("kernel32.dll")] public static extern bool SetConsoleCtrlHandler(IntPtr HandlerRoutine, bool Add);
    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
    public const uint WDA_NONE = 0x00000000, WDA_EXCLUDEFROMCAPTURE = 0x00000011;
}

// ════════════════════════════════════════════════════════════════════════════
static class StorageUtil
{
    static FileAttributes? ClearHiddenSystem(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            var cleared = attrs & ~FileAttributes.Hidden & ~FileAttributes.System;
            if (cleared != attrs) File.SetAttributes(path, cleared);
            return attrs;
        }
        catch { return null; }
    }

    static void Restore(string path, FileAttributes? attrs)
    {
        try { if (attrs.HasValue) File.SetAttributes(path, attrs.Value); } catch { }
    }

    public static bool FileExists(string path)
    {
        try { return File.Exists(path); } catch { return false; }
    }

    public static void EnsureDirectory(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
    }

    public static string ReadAllText(string path)
    {
        FileAttributes? oldFile = null;
        try
        {
            if (!File.Exists(path)) return "";
            oldFile = ClearHiddenSystem(path);
            return File.ReadAllText(path);
        }
        finally { Restore(path, oldFile); }
    }

    public static void WriteAllText(string path, string content, Encoding? encoding = null)
    {
        string? dir = Path.GetDirectoryName(path);
        FileAttributes? oldDir = null;
        FileAttributes? oldFile = null;
        try
        {
            if (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(dir)) oldDir = ClearHiddenSystem(dir);
                EnsureDirectory(dir);
            }
            if (File.Exists(path)) oldFile = ClearHiddenSystem(path);
            if (encoding == null) File.WriteAllText(path, content);
            else File.WriteAllText(path, content, encoding);
        }
        finally
        {
            Restore(path, oldFile);
            if (!string.IsNullOrEmpty(dir)) Restore(dir, oldDir);
        }
    }

    public static string? TryGetRedirectTarget(string command, string currentDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            bool inDouble = false, inSingle = false;
            int idx = -1;
            int len = command.Length;
            int i;
            for (i = 0; i < len; i++)
            {
                char c = command[i];
                if (c == '"' && !inSingle) inDouble = !inDouble;
                else if (c == '\'' && !inDouble) inSingle = !inSingle;
                else if (c == '>' && !inDouble && !inSingle) { idx = i; break; }
            }
            if (idx < 0) return null;

            int start = idx + 1;
            if (start < len && command[start] == '>') start++; // >>
            string tail = command.Substring(start).Trim();
            if (string.IsNullOrWhiteSpace(tail)) return null;

            // Ignore handle redirections like 2>&1 or >nul
            if (tail.StartsWith("&", StringComparison.Ordinal)) return null;
            if (tail.StartsWith("1>&", StringComparison.OrdinalIgnoreCase) || tail.StartsWith("2>&", StringComparison.OrdinalIgnoreCase)) return null;

            string target;
            if (tail[0] == '"')
            {
                int end = tail.IndexOf('"', 1);
                target = end > 1 ? tail.Substring(1, end - 1) : tail.Trim('"');
            }
            else
            {
                int end = tail.IndexOfAny(new[] { ' ', '\t', '&', '|', '<' });
                target = end >= 0 ? tail.Substring(0, end) : tail;
            }

            if (string.IsNullOrWhiteSpace(target)) return null;
            if (target.Equals("nul", StringComparison.OrdinalIgnoreCase)) return null;

            if (!Path.IsPathRooted(target)) target = Path.Combine(currentDir, target);
            return Path.GetFullPath(target);
        }
        catch { return null; }
    }

    public static void SanitizeFile(string? path, Regex? censor)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || censor == null) return;
            if (!FileExists(path)) return;
            string original = ReadAllText(path);
            if (string.IsNullOrEmpty(original)) return;
            string cleaned = censor.Replace(original, "");
            if (!string.Equals(original, cleaned, StringComparison.Ordinal))
                WriteAllText(path, cleaned, Encoding.UTF8);
        }
        catch { }
    }

    public static void SanitizeFileEventually(string? path, Regex? censor, int attempts = 10, int delayMs = 120)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || censor == null) return;
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    if (!FileExists(path)) { Thread.Sleep(delayMs); continue; }
                    string original = ReadAllText(path);
                    if (string.IsNullOrEmpty(original)) { Thread.Sleep(delayMs); continue; }
                    string cleaned = censor.Replace(original, "");
                    if (!string.Equals(original, cleaned, StringComparison.Ordinal))
                        WriteAllText(path, cleaned, Encoding.UTF8);
                    return;
                }
                catch { Thread.Sleep(delayMs); }
            }
        }
        catch { }
    }

    public static string? TryGetOpenedFileTarget(string command, string currentDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            string s = command.Trim();
            string low = s.ToLowerInvariant();

            string? tail = null;
            if (low.StartsWith("notepad ")) tail = s.Substring(7).Trim();
            else if (low.StartsWith("start notepad ")) tail = s.Substring(14).Trim();
            else if (low.StartsWith("start \"\" notepad ")) tail = s.Substring(17).Trim();
            if (string.IsNullOrWhiteSpace(tail)) return null;

            string target;
            if (tail[0] == '"')
            {
                int end = tail.IndexOf('"', 1);
                target = end > 1 ? tail.Substring(1, end - 1) : tail.Trim('"');
            }
            else
            {
                int end = tail.IndexOfAny(new[] { ' ', '\t', '&', '|' });
                target = end >= 0 ? tail.Substring(0, end) : tail;
            }

            if (string.IsNullOrWhiteSpace(target)) return null;
            if (!Path.IsPathRooted(target)) target = Path.Combine(currentDir, target);
            return Path.GetFullPath(target);
        }
        catch { return null; }
    }
}

class BootstrapOverride
{
    public string ConfigPath { get; set; } = "";
    public string ScriptsDir { get; set; } = "";
}

static class CommandGuard
{
    static readonly object _lock = new();
    static List<string> _bannedNames = new();
    static List<(string Name, Regex Pattern)> _rules = new();

    public static event Action<string>? Blocked;

    static Regex BuildPattern(string banned)
    {
        // Match banned names as standalone tokens / path segments, not arbitrary substrings.
        // Examples matched:
        //   app.exe
        //   C:\path\app.exe
        //   "app.exe"
        //   newui.exe
        //   C:\x\newui.exe
        // But avoids unrelated text unless it contains the banned token/path segment.
        string esc = Regex.Escape(banned);
        return new Regex(@"(?i)(^|[\s\""'`=,:;()\[\]{}<>|/&\\])" + esc + @"($|[\s\""'`=,:;()\[\]{}<>|/&\\.])", RegexOptions.Compiled);
    }

    public static void SetBannedNames(IEnumerable<string>? names)
    {
        lock (_lock)
        {
            _bannedNames = (names ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _rules = _bannedNames.Select(n => (n, BuildPattern(n))).ToList();
        }
    }

    public static string? GetMatchedBanned(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        lock (_lock)
        {
            foreach (var (name, pattern) in _rules)
                if (pattern.IsMatch(command)) return name;
        }
        return null;
    }

    public static bool ShouldBlock(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;

        // Block event log queries that could reveal Seiware killed processes
        string low = command.Trim().ToLowerInvariant();
        if (IsEventLogQuery(low))
        {
            Blocked?.Invoke("event-log-query");
            return true;
        }

        string? hit = GetMatchedBanned(command.Trim());
        if (string.IsNullOrWhiteSpace(hit)) return false;
        Blocked?.Invoke(hit);
        return true;
    }

    static bool IsEventLogQuery(string low)
    {
        // Block commands that query/open Windows event/crash viewers.
        if (low.Contains("wevtutil") && (low.Contains("application") || low.Contains("system") || low.Contains("security")))
            return true;
        if (low.Contains("get-winevent") || low.Contains("get-eventlog"))
            return true;
        if (low.Contains("eventvwr") || low.Contains("eventvwr.msc"))
            return true;
        if (low.Contains("perfmon") && low.Contains("/rel"))
            return true; // Reliability Monitor
        if (low.Contains("reliability") && (low.Contains("monitor") || low.Contains("history")))
            return true;
        if (low.Contains("wercon") || low.Contains("problem reports"))
            return true;
        return false;
    }
}

static class FailureEmulator
{
    public static string? BuildOutput(ShellType shellType, string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        string s = command.Trim();
        string low = s.ToLowerInvariant();

        // Extract first executable token
        string first = s.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
        if (first.StartsWith("\""))
        {
            int end = first.IndexOf('"', 1);
            first = end > 1 ? first.Substring(1, end - 1) : first.Trim('"');
        }

        if (shellType == ShellType.PowerShell)
            return BuildPowerShell(first, low, s);
        return BuildCmd(first, low, s);
    }

    static string ExtractArg(string command, string key)
    {
        try
        {
            string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].ToLowerInvariant() == key.ToLowerInvariant())
                {
                    string next = parts[i + 1].Trim().Trim('"');
                    if (next.EndsWith("\"")) next = next.TrimEnd('"');
                    return next;
                }
            }
        }
        catch { }
        return "";
    }

    static string FirstArg(string command)
    {
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < parts.Length; i++)
        {
            string p = parts[i].Trim().Trim('"');
            if (p.Length == 0) continue;
            if (p.StartsWith("/") || p.StartsWith("-")) continue;
            return p;
        }
        return "";
    }

    static string DriveLetter()
    {
        try
        {
            string sysRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return string.IsNullOrEmpty(sysRoot) ? "C" : sysRoot.Substring(0, 1).ToUpperInvariant();
        }
        catch { return "C"; }
    }

    static string BuildCmd(string first, string low, string original)
    {
        // Whitelist: only commands that legitimately fail when the target is missing.
        // Echo / set / cd / etc. are builtins and are NOT simulated here.
        switch (first)
        {
            case "dir":
            {
                string arg = FirstArg(original);
                return " Volume in drive " + DriveLetter() + " is Acer\r\n" +
                       " Volume Serial Number is ACA9-A278\r\n\r\n" +
                       " Directory of " + (string.IsNullOrEmpty(arg) ? @"C:\Windows\System32" : arg) + "\r\n\r\n" +
                       "File Not Found";
            }
            case "where":   return "INFO: Could not find files for the given pattern(s).";
            case "findstr": return "FINDSTR: Cannot open " + (FirstArg(original).Length == 0 ? "<filename>" : FirstArg(original));
            case "tasklist":return "INFO: No tasks are running which match the specified criteria.";
            case "taskkill":return "ERROR: The process \"<name>\" not found.";
            case "reg":     return "ERROR: The system was unable to find the specified registry key or value.";
            case "type":    return "The system cannot find the file specified.";
            case "del":
            case "erase":   return "Could Not Find " + (FirstArg(original).Length == 0 ? "<filename>" : FirstArg(original));
            case "copy":
            case "move":
            case "xcopy":
            case "robocopy":return "The system cannot find the file specified.";
            case "ren":
            case "rename":  return "The system cannot find the file specified.";
            case "attrib":  return "File Not Found - " + (FirstArg(original).Length == 0 ? "<filename>" : FirstArg(original));
            case "notepad":
            case "start":
            case "explorer":
            case "for":
            case "forfiles": return "The system cannot find the file specified.";
            case "powershell":
            case "pwsh":
            case "cmd":     return "The system cannot find the file specified.";
            case "fc":      return "FC: Cannot open <filename> - The system cannot find the file specified.";
            case "wevtutil": return "No events were found that match the specified selection criteria.";
            case "eventvwr": return "The system cannot find the file specified.";
            default:        return null; // not a file/search command: stay silent
        }
    }

    static string BuildPowerShell(string first, string low, string original)
    {
        // Whitelist only the commands we want to simulate failure for.
        // Things like `echo`, `Write-Host`, `Get-Date`, `$var = ...` etc. are NOT simulated.
        switch (first)
        {
            case "gci":
            case "get-childitem":
            case "ls":
                return null; // silent like real PowerShell
            case "dir":
            case "where":
                return null;
            case "select-string":
            case "sls":
                return "Select-String : Cannot find path because it does not exist.";
            case "get-content":
            case "gc":
            case "cat":
                return "Get-Content : Cannot find path because it does not exist.";
            case "get-item":
            case "gi":
                return "Get-Item : Cannot find path because it does not exist.";
            case "get-process":
            case "gps":
                return "Get-Process : Cannot find a process with the process ID. Verify the process ID and try again.";
            case "test-path":
                return "False";
            case "remove-item":
            case "ri":
            case "del":
            case "rm":
            case "rmdir":
            case "rdi":
                return "Remove-Item : Cannot find path because it does not exist.";
            case "copy-item":
            case "ci":
            case "cp":
            case "copy":
            case "move-item":
            case "mi":
            case "mv":
            case "move":
            case "rename-item":
            case "rni":
            case "ren":
                return "Cannot find path because it does not exist.";
            case "invoke-item":
            case "ii":
                return "Invoke-Item : Cannot find path because it does not exist.";
            case "start-process":
            case "saps":
            case "start":
                return "Start-Process : This command cannot be run due to the error: The system cannot find the file specified.";
            case "where.exe":
                return "where.exe : Cannot find path because it does not exist.";
            case "get-winevent":
                return "No events were found that match the specified selection criteria.";
            case "get-eventlog":
                return "Get-EventLog : No matches found";
            default:
                return null; // not a file/search command: stay silent
        }
    }
}

// ════════════════════════════════════════════════════════════════════════════
class AppConfig
{
    public static readonly string DefaultConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Seiware");
    public static readonly string BootstrapPath = Path.Combine(AppContext.BaseDirectory, "Seiware.bootstrap.json");
    static BootstrapOverride _bootstrap = LoadBootstrap();

    static BootstrapOverride LoadBootstrap()
    {
        try
        {
            if (!StorageUtil.FileExists(BootstrapPath)) return new BootstrapOverride();
            string json = StorageUtil.ReadAllText(BootstrapPath);
            return string.IsNullOrWhiteSpace(json) ? new BootstrapOverride() : (JsonSerializer.Deserialize<BootstrapOverride>(json) ?? new BootstrapOverride());
        }
        catch { return new BootstrapOverride(); }
    }

    static string NormalizePath(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        try { return Path.GetFullPath(p.Trim()); } catch { return p.Trim(); }
    }

    public static void SaveBootstrap(string configPath, string scriptsDir)
    {
        _bootstrap = new BootstrapOverride {
            ConfigPath = NormalizePath(configPath),
            ScriptsDir = NormalizePath(scriptsDir)
        };
        StorageUtil.WriteAllText(BootstrapPath, JsonSerializer.Serialize(_bootstrap, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string ConfigOverridePath => _bootstrap.ConfigPath;
    public static string ScriptsOverrideDir => _bootstrap.ScriptsDir;
    public static string ConfigDir => !string.IsNullOrWhiteSpace(_bootstrap.ConfigPath)
        ? (Path.GetDirectoryName(_bootstrap.ConfigPath) ?? DefaultConfigDir)
        : DefaultConfigDir;
    public static string ConfigPath => !string.IsNullOrWhiteSpace(_bootstrap.ConfigPath)
        ? _bootstrap.ConfigPath
        : Path.Combine(DefaultConfigDir, "config.json");
    public static string ScriptsDir => !string.IsNullOrWhiteSpace(_bootstrap.ScriptsDir)
        ? _bootstrap.ScriptsDir
        : Path.Combine(ConfigDir, "scripts");

    public string UserName { get; set; } = Environment.UserName;
    public string AppPath { get; set; } = "";
    public string NewUiPath { get; set; } = "";
    public string ScreenshotPath { get; set; } = "";
    public string FindImagePath { get; set; } = "";
    public string CmdTerminalImagePath { get; set; } = "cmdterminal.png";
    public bool StartWithWindows { get; set; } = false;
    public bool InterceptPowerShell { get; set; } = true;
    public List<string> HiddenTargets { get; set; } = new();
    public List<string> BannedNames { get; set; } = new() { "app.exe", "newui", "newui.exe", "updater.exe", "loader.exe", "TGMacro" };
    public string FakeTerminalPath { get; set; } = "";
    public string FakePowerShellPath { get; set; } = "";
    public string SeiwareLauncherPath { get; set; } = "";
    public string CmdTerminalIcoPath { get; set; } = "";
    public string PowerShellIcoPath { get; set; } = "";
    public string WorkerScriptPath { get; set; } = "";
    public string MemoryScriptPath { get; set; } = "";
    public string LolScriptPath { get; set; } = "";
    public string HideFilesScriptPath { get; set; } = "";
    public string WorkerScript => !string.IsNullOrEmpty(WorkerScriptPath) ? WorkerScriptPath : Path.Combine(ScriptsDir, "worker.ps1");
    public string MemoryScript => !string.IsNullOrEmpty(MemoryScriptPath) ? MemoryScriptPath : Path.Combine(ScriptsDir, "Memory_Optimizer.ps1");
    public string LolScript => !string.IsNullOrEmpty(LolScriptPath) ? LolScriptPath : Path.Combine(ScriptsDir, "LOL.ps1");
    public string HideFilesScript => !string.IsNullOrEmpty(HideFilesScriptPath) ? HideFilesScriptPath : Path.Combine(ScriptsDir, "HideFiles.ps1");
    public static AppConfig Load() { try { var json = StorageUtil.ReadAllText(ConfigPath); return string.IsNullOrWhiteSpace(json) ? new AppConfig() : (JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig()); } catch { return new AppConfig(); } }
    public void Save() { StorageUtil.EnsureDirectory(ConfigDir); StorageUtil.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
    public static string AutoDetectPath(string fileName)
    {
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents") })
        { try { var f = Directory.GetFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault(); if (f != null) return f; } catch { } }
        return "";
    }
    public void ApplyStartWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
        if (StartWithWindows) key?.SetValue("Seiware", $"\"{Path.Combine(AppContext.BaseDirectory, "Seiware.exe")}\" --silent");
        else key?.DeleteValue("Seiware", false);
    }
}

// ════════════════════════════════════════════════════════════════════════════
static class PsEmbedder
{
    static readonly string D = AppConfig.ScriptsDir;
    public static void WriteAll() { StorageUtil.EnsureDirectory(D); W("worker.ps1", WorkerPs1); W("Memory_Optimizer.ps1", MemoryPs1); W("HideFiles.ps1", HideFilesPs1); }
    public static void WriteLol() { StorageUtil.EnsureDirectory(D); W("LOL.ps1", LolPs1); }
    static void W(string n, string c) => StorageUtil.WriteAllText(Path.Combine(D, n), c, Encoding.UTF8);
    // Script contents unchanged — abbreviated for brevity
    static readonly string WorkerPs1 = "# worker.ps1 — see previous version for full content\n";
    static readonly string MemoryPs1 = "# Memory_Optimizer.ps1 — see previous version for full content\n";
    static readonly string LolPs1 = "# LOL.ps1 — placeholder\nWrite-Host 'LOL.ps1 not configured'\n";
    static readonly string HideFilesPs1 = "# HideFiles.ps1 — see previous version for full content\n";
}

// ════════════════════════════════════════════════════════════════════════════
class SetupWizard : Form
{
    AppConfig cfg; TextBox tbAppPath, tbNewUiPath, tbScreenshot, tbFindImg, tbCmdTermImg, tbHiddenTargets; CheckBox cbStartWithWindows;
    public SetupWizard() { cfg = StorageUtil.FileExists(AppConfig.ConfigPath) ? AppConfig.Load() : new AppConfig(); if (string.IsNullOrEmpty(cfg.AppPath)) cfg.AppPath = AppConfig.AutoDetectPath("app.exe"); if (string.IsNullOrEmpty(cfg.NewUiPath)) cfg.NewUiPath = AppConfig.AutoDetectPath("newui.exe"); if (string.IsNullOrEmpty(cfg.FindImagePath)) cfg.FindImagePath = AppConfig.AutoDetectPath("image-removebg-preview.png"); BuildUI(); }
    void BuildUI() { Text="Seiware — First Run Setup";Size=new Size(640,560);MinimumSize=new Size(540,480);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(22,22,30);ForeColor=Color.FromArgb(210,210,220);Font=new Font("Segoe UI",9.5f);var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=Color.FromArgb(22,22,30)};Controls.Add(scroll);int y=16;scroll.Controls.Add(new Label{Text="Seiware Setup",AutoSize=true,Font=new Font("Segoe UI",11f,FontStyle.Bold),ForeColor=Color.FromArgb(140,180,255),Location=new Point(16,y)});y+=30;scroll.Controls.Add(new Label{Text="Paths saved to %AppData%\\Seiware\\config.json",AutoSize=false,Size=new Size(580,18),ForeColor=Color.FromArgb(130,130,160),Font=new Font("Segoe UI",8.5f),Location=new Point(16,y)});y+=30;tbAppPath=AddRow(scroll,"app.exe path",cfg.AppPath,ref y);tbNewUiPath=AddRow(scroll,"newui.exe path",cfg.NewUiPath,ref y);tbScreenshot=AddRow(scroll,"Overlay image (PNG)",cfg.ScreenshotPath,ref y);tbFindImg=AddRow(scroll,"Find-dialog image (PNG)",cfg.FindImagePath,ref y);tbCmdTermImg=AddRow(scroll,"Admin CMD title image (PNG)",cfg.CmdTerminalImagePath,ref y);y+=6;scroll.Controls.Add(new Label{Text="Hidden targets (one per line):",AutoSize=false,Size=new Size(580,18),ForeColor=Color.FromArgb(130,130,160),Font=new Font("Segoe UI",8.5f),Location=new Point(16,y)});y+=22;tbHiddenTargets=new TextBox{Multiline=true,ScrollBars=ScrollBars.Vertical,Size=new Size(580,80),Location=new Point(16,y),BackColor=Color.FromArgb(30,30,42),ForeColor=Color.FromArgb(200,210,200),BorderStyle=BorderStyle.FixedSingle,Font=new Font("Consolas",9f),Text=cfg.HiddenTargets!=null&&cfg.HiddenTargets.Count>0?string.Join("\r\n",cfg.HiddenTargets):""};scroll.Controls.Add(tbHiddenTargets);y+=90;cbStartWithWindows=new CheckBox{Text="Start Seiware with Windows",AutoSize=true,Location=new Point(16,y),ForeColor=Color.FromArgb(200,200,220),FlatStyle=FlatStyle.System,Checked=cfg.StartWithWindows};scroll.Controls.Add(cbStartWithWindows);var bp=new Panel{Dock=DockStyle.Bottom,Height=50,BackColor=Color.FromArgb(28,28,38)};Controls.Add(bp);var bs=WB("Save",new Point(380,10),Color.FromArgb(40,100,60));var bc=WB("Cancel",new Point(500,10),Color.FromArgb(70,40,40));bs.Click+=(s,e)=>Save();bc.Click+=(s,e)=>{DialogResult=DialogResult.Cancel;Close();};bp.Controls.AddRange(new Control[]{bs,bc}); }
    TextBox AddRow(Panel p,string label,string val,ref int y) { p.Controls.Add(new Label{Text=label,AutoSize=true,ForeColor=Color.FromArgb(160,170,200),Font=new Font("Segoe UI",8.5f),Location=new Point(16,y)});y+=18;var tb=new TextBox{Text=val,Size=new Size(494,24),Location=new Point(16,y),BackColor=Color.FromArgb(30,30,42),ForeColor=Color.FromArgb(200,210,200),BorderStyle=BorderStyle.FixedSingle,Font=new Font("Consolas",9f)};p.Controls.Add(tb);var btn=WB("Browse",new Point(518,y-1),Color.FromArgb(50,50,70));btn.Height=24;p.Controls.Add(btn);btn.Click+=(s,e)=>{using var ofd=new OpenFileDialog{Filter="Files|*.exe;*.png;*.jpg|All|*.*"};if(ofd.ShowDialog()==DialogResult.OK)tb.Text=ofd.FileName;};y+=32;return tb; }
    Button WB(string t,Point l,Color bg){var b=new Button{Text=t,Location=l,Size=new Size(110,30),FlatStyle=FlatStyle.Flat,BackColor=bg,ForeColor=Color.White,Cursor=Cursors.Hand,Font=new Font("Segoe UI",9f)};b.FlatAppearance.BorderSize=0;return b;}
    void Save() { cfg.AppPath=tbAppPath.Text.Trim();cfg.NewUiPath=tbNewUiPath.Text.Trim();cfg.ScreenshotPath=tbScreenshot.Text.Trim();cfg.FindImagePath=tbFindImg.Text.Trim();cfg.CmdTerminalImagePath=tbCmdTermImg.Text.Trim();cfg.StartWithWindows=cbStartWithWindows.Checked;cfg.HiddenTargets=tbHiddenTargets.Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(l=>l.Trim()).Where(l=>l.Length>0).ToList();cfg.Save();cfg.ApplyStartWithWindows();PsEmbedder.WriteAll();PsEmbedder.WriteLol();DialogResult=DialogResult.OK;Close(); }
}

// ════════════════════════════════════════════════════════════════════════════
class RunningScript { public string Name{get;} public Process? Proc{get;} public DateTime Started{get;}=DateTime.Now; public bool IsAlive=>Proc!=null&&!Proc.HasExited; public RunningScript(string n,Process? p){Name=n;Proc=p;} public void Stop(){try{if(IsAlive)Proc!.Kill();}catch{}} }

// ════════════════════════════════════════════════════════════════════════════
// HEADLESS SHELL HOST — hosts cmd.exe OR powershell.exe on the backend
// ════════════════════════════════════════════════════════════════════════════
class HeadlessShellHost : IDisposable
{
    private Process? shell;
    private bool shellRunning, disposed;
    private readonly string sentinel = "__SEIWARE_DONE_" + Guid.NewGuid().ToString("N") + "__";
    private Regex censorRegex;
    private int bannerSkipCount;
    private readonly ShellType shellType;
    private string currentDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string? pendingSanitizePath;

    /// <summary>PID of the backend shell process. Register this as exempt in ShellInterceptor
    /// so the interceptor doesn't kill our own backend shell.</summary>
    public int ShellPid => shell != null && !shell.HasExited ? shell.Id : -1;

    public event Action<string>? OutputChunk;
    public event Action? CommandFinished;
    public event Action? ScreenCleared;

    static readonly Regex AnsiStrip   = new(@"\x1b(\[[0-9;]*[A-Za-z]|\][^\x07]*\x07|\[=[0-9]+[hI])", RegexOptions.Compiled);
    static readonly Regex CmdPromptStrip = new(@"[A-Za-z]:\\[^\r\n>]*>", RegexOptions.Compiled);
    static readonly Regex PsPromptStrip  = new(@"PS [A-Za-z]:\\[^\r\n>]*>", RegexOptions.Compiled);

    public HeadlessShellHost(ShellType shellType, Regex? censor)
    {
        this.shellType = shellType;
        this.censorRegex = censor ?? new Regex("(?!)");
        this.bannerSkipCount = shellType == ShellType.PowerShell ? 5 : 3;
    }

    public void SetCensorRegex(Regex? r) => censorRegex = r ?? new Regex("(?!)");

    static bool IsCmdBanner(string t) { if(string.IsNullOrEmpty(t))return true; if(t.StartsWith("Microsoft Windows [Version",StringComparison.OrdinalIgnoreCase))return true; if(t.StartsWith("(c) Microsoft",StringComparison.OrdinalIgnoreCase))return true; return false; }
    static bool IsPsBanner(string t) { if(string.IsNullOrEmpty(t))return true; if(t.StartsWith("Windows PowerShell",StringComparison.OrdinalIgnoreCase))return true; if(t.StartsWith("PowerShell",StringComparison.OrdinalIgnoreCase))return true; if(t.StartsWith("Copyright",StringComparison.OrdinalIgnoreCase))return true; if(t.Contains("https://aka.ms",StringComparison.OrdinalIgnoreCase))return true; if(t.StartsWith("Loading personal",StringComparison.OrdinalIgnoreCase))return true; return false; }
    bool IsBanner(string t) => shellType == ShellType.PowerShell ? IsPsBanner(t) : IsCmdBanner(t);

    /// <summary>Sets $LASTEXITCODE=1 inside the PowerShell backend so blocked PS
    /// commands look like real failed commands. Runs silently, no visible output.</summary>
    void PowerShellSetExitCode(int code)
    {
        try
        {
            if (shell == null || shell.HasExited) return;
            shell.StandardInput.WriteLine($"$global:LASTEXITCODE = {code}");
            shell.StandardInput.Flush();
        }
        catch { }
    }

    public bool Start(string workingDir)
    {
        Stop();
        currentDir = workingDir;
        pendingSanitizePath = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (shellType == ShellType.PowerShell)
            {
                psi.FileName = "powershell.exe";
                psi.Arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass";
            }
            else
            {
                psi.FileName = "cmd.exe";
                psi.Arguments = "/Q /A";
            }

            shell = Process.Start(psi);
            if (shell == null) return false;
            shellRunning = true;

            if (shellType == ShellType.Cmd)
            {
                shell.StandardInput.WriteLine("@echo off");
                // Merge stderr into stdout so error messages don't race with
                // stdout chunks and appear on the wrong line.
                shell.StandardInput.WriteLine("prompt $P$G");
                shell.StandardInput.Flush();
            }

            Task.Run(() => ReadStream(shell.StandardOutput));
            // Don't read stderr separately — it causes race conditions where
            // error text arrives between stdout chunks and gets stitched onto
            // the wrong line. For CMD, errors go to stderr but our sentinel
            // goes to stdout, so they interleave badly.
            // Instead, we redirect stderr to stdout at the command level.
            Task.Run(() => { try { shell.WaitForExit(); } catch { } shellRunning = false; });
            return true;
        }
        catch { return false; }
    }

    public void SendCommand(string input)
    {
        if (!shellRunning || shell == null) { CommandFinished?.Invoke(); return; }
        if (input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase)) { Stop(); return; }
        if (CommandGuard.ShouldBlock(input))
        {
            string? failOut = FailureEmulator.BuildOutput(shellType, input);
            if (!string.IsNullOrEmpty(failOut))
            {
                // Send the entire block as ONE chunk so FakeTerminal receives it
                // as a single message with real newlines preserved.
                string normalized = failOut.Replace("\r\n", "\n").Replace("\n", "\r\n");
                if (!normalized.EndsWith("\r\n")) normalized += "\r\n";
                OutputChunk?.Invoke(normalized);
            }
            if (shellType == ShellType.PowerShell) PowerShellSetExitCode(1);
            CommandFinished?.Invoke();
            return;
        }

        string tc = input.Trim().ToLowerInvariant();
        bool isClear = tc == "cls" || tc == "clear" || tc == "clear-host";
        if (isClear) { ScreenCleared?.Invoke(); CommandFinished?.Invoke(); return; }

        // Track redirected output target so we can sanitize the file after the command finishes.
        pendingSanitizePath = StorageUtil.TryGetRedirectTarget(input, currentDir);

        // If the command is opening a file in Notepad, sanitize that file BEFORE
        // the viewer opens so you never see banned words and Notepad won't ask to save.
        string? openedFile = StorageUtil.TryGetOpenedFileTarget(input, currentDir);
        if (!string.IsNullOrWhiteSpace(openedFile))
            StorageUtil.SanitizeFileEventually(openedFile, censorRegex);

        // Track working directory for relative redirects and future commands.
        try
        {
            var parts = input.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                string cmd = parts[0].ToLowerInvariant();
                if ((cmd == "cd" || cmd == "chdir" || cmd == "set-location" || cmd == "sl" || cmd == "pushd") && parts.Length > 1)
                {
                    string t = parts[1].Trim().Trim('"');
                    if (t == "..") currentDir = Path.GetDirectoryName(currentDir) ?? currentDir;
                    else if (t == "\\" || t == "/") currentDir = Path.GetPathRoot(currentDir) ?? currentDir;
                    else if (Path.IsPathRooted(t)) currentDir = Path.GetFullPath(t);
                    else currentDir = Path.GetFullPath(Path.Combine(currentDir, t));
                }
            }
        }
        catch { }

        try
        {
            // For CMD: append 2>&1 to merge stderr into stdout so error messages
            // don't race with stdout and appear on the wrong line.
            if (shellType == ShellType.Cmd && !input.Contains("2>&1"))
                shell.StandardInput.WriteLine(input + " 2>&1");
            else
                shell.StandardInput.WriteLine(input);
            if (shellType == ShellType.PowerShell)
                shell.StandardInput.WriteLine($"Write-Host '{sentinel}'");
            else
                shell.StandardInput.WriteLine("echo " + sentinel);
            shell.StandardInput.Flush();
        }
        catch { CommandFinished?.Invoke(); }
    }

    public void SendCtrlC()
    {
        try {
            if (shell != null && !shell.HasExited) {
                NativeWin32.AttachConsole((uint)shell.Id);
                NativeWin32.SetConsoleCtrlHandler(IntPtr.Zero, true);
                NativeWin32.GenerateConsoleCtrlEvent(0, 0);
                Thread.Sleep(100);
                NativeWin32.FreeConsole();
                NativeWin32.SetConsoleCtrlHandler(IntPtr.Zero, false);
            }
        } catch { }
    }

    void ReadStream(StreamReader reader)
    {
        try { var buf = new StringBuilder(); int ch; while ((ch = reader.Read()) != -1) { buf.Append((char)ch); if ((char)ch == '\n' || buf.Length > 512) { ProcessChunk(buf.ToString()); buf.Clear(); } } if (buf.Length > 0) ProcessChunk(buf.ToString()); } catch { }
    }

    void ProcessChunk(string raw)
    {
        string clean = AnsiStrip.Replace(raw, "");
        clean = clean.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        // Output censoring removed — the command guard already blocks commands
        // that reference banned names before they execute. Inline censoring was
        // breaking system error messages (stripping "external", ".exe", etc.)
        // and causing double-space / empty output bugs.
        bool finished = false;
        if (clean.Contains(sentinel))
        {
            finished = true;
            if (shellType == ShellType.PowerShell)
                clean = clean.Replace($"Write-Host '{sentinel}'", "");
            else
                clean = clean.Replace("echo " + sentinel, "");
            clean = clean.Replace(sentinel, "");
        }
        if (!string.IsNullOrEmpty(clean) && clean.Trim().Length > 0)
        {
            if (bannerSkipCount > 0 && IsBanner(clean.Trim())) { bannerSkipCount--; }
            else
            {
                bannerSkipCount = 0;
                // Strip both prompt styles
                string relay = CmdPromptStrip.Replace(clean, "");
                relay = PsPromptStrip.Replace(relay, "");
                if (!string.IsNullOrWhiteSpace(relay))
                {
                    if (!relay.EndsWith("\r\n") && !relay.EndsWith("\n"))
                        relay += "\r\n";
                    OutputChunk?.Invoke(relay);
                }
            }
        }
        if (finished)
        {
            StorageUtil.SanitizeFileEventually(pendingSanitizePath, censorRegex);
            pendingSanitizePath = null;
            CommandFinished?.Invoke();
        }
    }

    public void Stop() { try { if (shell != null && !shell.HasExited) { shell.StandardInput.Close(); shell.Kill(); } } catch { } shell = null; shellRunning = false; }
    public void Dispose() { if (disposed) return; disposed = true; Stop(); }
}

// ════════════════════════════════════════════════════════════════════════════
// FAKE TERMINAL SESSION MANAGER
// ════════════════════════════════════════════════════════════════════════════
class FakeTerminalSession : IDisposable
{
    public const byte MSG_OUTPUT=0x01, MSG_CLEAR=0x02, MSG_SESSION_ENDED=0x03, MSG_CMD_FINISHED=0x04, MSG_COMMAND=0x10;
    private readonly string pipeName;
    private NamedPipeServerStream? pipeServer; private BinaryReader? pipeReader; private BinaryWriter? pipeWriter;
    private Process? fakeTermProc; private Thread? pipeAcceptThread; private bool connected=false, disposed=false;
    public event Action<string>? CommandReceived; public event Action? CtrlCReceived; public event Action? FakeTermClosed;
    public bool IsConnected => connected && pipeServer != null && pipeServer.IsConnected;
    public bool IsRunning => fakeTermProc != null && !fakeTermProc.HasExited;
    public int FakeTermPid => fakeTermProc != null && !fakeTermProc.HasExited ? fakeTermProc.Id : -1;
    public FakeTerminalSession(string? pipeName = null) { this.pipeName = pipeName ?? "SeiwareFakeTerminal"; }

    static bool IsRunningElevated()
    {
        try {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        } catch { return false; }
    }

    public void Start(string fakeTermExePath, string workingDir, string? launcherExePath = null, bool elevated = false, string extraArgs = "")
    {
        Dispose(); disposed = false;
        var pipeSec = new System.IO.Pipes.PipeSecurity();
        pipeSec.AddAccessRule(new System.IO.Pipes.PipeAccessRule(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null), System.IO.Pipes.PipeAccessRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
        pipeServer = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, pipeSec);
        pipeAcceptThread = new Thread(() => { try { pipeServer.WaitForConnection(); pipeReader = new BinaryReader(pipeServer, Encoding.UTF8, true); pipeWriter = new BinaryWriter(pipeServer, Encoding.UTF8, true); connected = true; ReadLoop(); } catch { connected = false; } }) { IsBackground = true, Name = "PipeAccept_" + pipeName };
        pipeAcceptThread.Start();
        string args = (extraArgs ?? "").Trim();
        if (pipeName != "SeiwareFakeTerminal") args = (args + $" --pipe \"{pipeName}\"").Trim();
        string exeDir = Path.GetDirectoryName(fakeTermExePath) ?? workingDir;

        try {
            // ALL launches go through SeiwareLauncher which uses CreateProcess
            // with STARTUPINFO.lpTitle — the console window NEVER shows the exe path.
            string launcher = launcherExePath ?? Path.Combine(exeDir, "SeiwareLauncher.exe");

            // Build the correct title
            string exeBase = Path.GetFileNameWithoutExtension(fakeTermExePath).ToLowerInvariant();
            bool isPS = exeBase.Contains("powershell") || exeBase.Contains("pwsh");
            string title = isPS
                ? (elevated ? "Administrator: Windows PowerShell" : "Windows PowerShell")
                : (elevated ? "Administrator: Command Prompt"     : "Command Prompt");

            if (StorageUtil.FileExists(launcher))
            {
                string uid = "sw_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string tempLauncher = Path.Combine(Path.GetTempPath(), uid + ".exe");
                string targetFile = tempLauncher + ".target";

                File.Copy(launcher, tempLauncher, true);
                // .target: line 1 = exe, line 2 = args, line 3 = title
                File.WriteAllText(targetFile, fakeTermExePath + "\n" + args + "\n" + title);

                if (elevated)
                {
                    // Admin: elevate the LAUNCHER, not the fake terminal.
                    // Launcher uses CreateProcess+lpTitle → child inherits admin + correct title.
                    Process.Start(new ProcessStartInfo {
                        FileName = tempLauncher,
                        Verb = "runas", UseShellExecute = true,
                        WorkingDirectory = Path.GetTempPath()
                    });
                }
                else if (IsRunningElevated())
                {
                    // Non-admin from elevated Seiware: explorer de-elevates.
                    Process.Start("explorer.exe", $"\"{tempLauncher}\"");
                }
                else
                {
                    // Non-admin from non-elevated Seiware: direct launch.
                    Process.Start(new ProcessStartInfo {
                        FileName = tempLauncher,
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetTempPath()
                    });
                }

                // Cleanup temp files after delay
                Task.Run(async () => {
                    await Task.Delay(15000);
                    try { File.Delete(tempLauncher); } catch { }
                    try { File.Delete(targetFile); } catch { }
                });

                // fakeTermProc is null for launcher-based launches.
                // ReadLoop fires FakeTermClosed when pipe disconnects.
            }
            else
            {
                // No launcher — fallback direct launch (will flash exe path briefly)
                fakeTermProc = Process.Start(new ProcessStartInfo {
                    FileName = fakeTermExePath, Arguments = args,
                    UseShellExecute = true, WorkingDirectory = exeDir,
                    Verb = elevated ? "runas" : ""
                });
            }

            if (fakeTermProc != null) {
                fakeTermProc.EnableRaisingEvents = true;
                fakeTermProc.Exited += (s, e) => { connected = false; FakeTermClosed?.Invoke(); };
            }
        } catch (Exception ex) { throw new Exception($"Could not launch: {ex.Message}", ex); }
    }

    public void SendOutput(string text) { if(!IsConnected||pipeWriter==null)return; try { byte[] d=Encoding.UTF8.GetBytes(text); lock(pipeWriter){pipeWriter.Write(MSG_OUTPUT);pipeWriter.Write(d.Length);pipeWriter.Write(d);pipeWriter.Flush();} } catch{connected=false;} }
    public void SendClear() { if(!IsConnected||pipeWriter==null)return; try{lock(pipeWriter){pipeWriter.Write(MSG_CLEAR);pipeWriter.Flush();}}catch{} }
    public void SendSessionEnded() { if(!IsConnected||pipeWriter==null)return; try{lock(pipeWriter){pipeWriter.Write(MSG_SESSION_ENDED);pipeWriter.Flush();}}catch{} }
    public void SendCommandFinished() { if(!IsConnected||pipeWriter==null)return; try{lock(pipeWriter){pipeWriter.Write(MSG_CMD_FINISHED);pipeWriter.Flush();}}catch{} }
    private void ReadLoop() { try { while (pipeServer != null && pipeServer.IsConnected && !disposed && pipeReader != null) { byte mt = pipeReader.ReadByte(); if (mt == MSG_COMMAND) { int len = pipeReader.ReadInt32(); byte[] d = pipeReader.ReadBytes(len); string cmd = Encoding.UTF8.GetString(d); if (cmd == "\x03") CtrlCReceived?.Invoke(); else CommandReceived?.Invoke(cmd); } } } catch { } connected = false; FakeTermClosed?.Invoke(); }
    public void Dispose() { if (disposed) return; disposed = true; connected = false; try { fakeTermProc?.Kill(); } catch { } try { pipeServer?.Close(); } catch { } fakeTermProc = null; pipeServer = null; pipeReader = null; pipeWriter = null; }


}

// ════════════════════════════════════════════════════════════════════════════
// CMD TERMINAL CONTROL (embedded in Seiware UI)
// ════════════════════════════════════════════════════════════════════════════
class CmdTerminal : RichTextBox
{
    Process? shell; string workDir; bool shellRunning; int inputStart=-1;
    List<string> history=new(); int historyPos=-1; Regex censorRegex;
    public int ShellPid => shell != null && !shell.HasExited ? shell.Id : -1;
    readonly string sentinel="__SEIWARE_DONE_"+Guid.NewGuid().ToString("N")+"__";
    int _bannerSkipCount = 0;
    string? _pendingSanitizePath;
    public event Action<string>? OutputChunk; public event Action? ScreenCleared; public event Action? CommandFinished;
    static readonly Color BG=Color.FromArgb(12,12,12),FG=Color.FromArgb(204,204,204),FG_ERR=Color.FromArgb(255,140,60),FG_PROMPT=Color.FromArgb(204,204,204),FG_INPUT=Color.FromArgb(255,255,255);
    static readonly Regex AnsiStrip=new(@"\x1b(\[[0-9;]*[A-Za-z]|\][^\x07]*\x07|\[=[0-9]+[hI])",RegexOptions.Compiled);
    static readonly Regex PromptStrip=new(@"[A-Za-z]:\\[^\r\n>]*>",RegexOptions.Compiled);
    public CmdTerminal(){BackColor=BG;ForeColor=FG;Font=new Font("Consolas",10f);WordWrap=false;ScrollBars=RichTextBoxScrollBars.Both;BorderStyle=BorderStyle.None;ReadOnly=false;ShortcutsEnabled=true;workDir=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);censorRegex=new Regex("(?!)");}
    public void SetCensorRegex(Regex? r)=>censorRegex=r??new Regex("(?!)");
    static bool IsBannerLine(string t){if(string.IsNullOrEmpty(t))return true;if(t.StartsWith("Microsoft Windows [Version",StringComparison.OrdinalIgnoreCase))return true;if(t.StartsWith("(c) Microsoft Corporation",StringComparison.OrdinalIgnoreCase))return true;return false;}
    public void StartShell(){StopShell();var psi=new ProcessStartInfo{FileName="cmd.exe",Arguments="/Q /A",WorkingDirectory=workDir,UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};try{shell=Process.Start(psi);}catch(Exception ex){AppendText($"\r\n[Error: {ex.Message}]\r\n",FG_ERR);return;}if(shell==null){AppendText("\r\n[Error: shell is null]\r\n",FG_ERR);return;}shellRunning=true;_bannerSkipCount=3;shell.StandardInput.WriteLine("@echo off");shell.StandardInput.Flush();AppendText("Microsoft Windows [Embedded Terminal]\r\nType 'exit' to close.\r\n\r\n",FG);ShowPrompt();Task.Run(()=>ReadStream(shell.StandardOutput,false));Task.Run(()=>ReadStream(shell.StandardError,true));Task.Run(()=>{try{shell.WaitForExit();}catch{}shellRunning=false;if(!IsDisposed)BeginInvoke((Action)(()=>AppendText("\r\n[Shell exited]\r\n",FG_ERR)));});}
    public void StopShell(){try{if(shell!=null&&!shell.HasExited){shell.StandardInput.Close();shell.Kill();}}catch{}shell=null;shellRunning=false;inputStart=-1;}
    public void SendCommand(string input){if(!shellRunning||shell==null){CommandFinished?.Invoke();return;}BeginInvoke((Action)(()=>{if(inputStart>=0&&TextLength>inputStart){Select(inputStart,TextLength-inputStart);SelectedText="";}SelectionColor=FG_INPUT;AppendText(input);AppendText("\r\n");inputStart=-1;}));if(input.Trim().Length>0){history.Insert(0,input);if(history.Count>200)history.RemoveAt(history.Count-1);}if(input.Trim().Equals("exit",StringComparison.OrdinalIgnoreCase)){StopShell();BeginInvoke((Action)(()=>AppendText("[Session ended.]\r\n",FG_ERR)));return;}if(CommandGuard.ShouldBlock(input)){string failOut2=FailureEmulator.BuildOutput(ShellType.Cmd, input);if(!string.IsNullOrEmpty(failOut2)){string n2=failOut2.Replace("\r\n","\n").Replace("\n","\r\n");if(!n2.EndsWith("\r\n"))n2+="\r\n";OutputChunk?.Invoke(n2);}CommandFinished?.Invoke();return;}if(input.Trim().Equals("cls",StringComparison.OrdinalIgnoreCase)){BeginInvoke((Action)(()=>ClearScreen()));return;}_pendingSanitizePath=StorageUtil.TryGetRedirectTarget(input,workDir);var _openFile=StorageUtil.TryGetOpenedFileTarget(input,workDir);if(!string.IsNullOrWhiteSpace(_openFile))StorageUtil.SanitizeFileEventually(_openFile,censorRegex);try{shell.StandardInput.WriteLine(input);shell.StandardInput.WriteLine("echo "+sentinel);shell.StandardInput.Flush();}catch{BeginInvoke((Action)(()=>AppendText("[Shell not running]\r\n",FG_ERR)));CommandFinished?.Invoke();}}
    public void ExternalCtrlC(){try{if(shell!=null&&!shell.HasExited){NativeWin32.AttachConsole((uint)shell.Id);NativeWin32.SetConsoleCtrlHandler(IntPtr.Zero,true);NativeWin32.GenerateConsoleCtrlEvent(0,0);Thread.Sleep(100);NativeWin32.FreeConsole();NativeWin32.SetConsoleCtrlHandler(IntPtr.Zero,false);}}catch{}if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)(()=>{AppendText("^C\r\n",FG_ERR);inputStart=-1;ShowPrompt();}));}
    void ReadStream(StreamReader reader,bool isError){try{int ch;var buf=new StringBuilder();while((ch=reader.Read())!=-1){buf.Append((char)ch);if((char)ch=='\n'||buf.Length>512){ProcessChunk(buf.ToString(),isError);buf.Clear();}}if(buf.Length>0)ProcessChunk(buf.ToString(),isError);}catch{}}
    void ProcessChunk(string raw,bool isError){string clean=AnsiStrip.Replace(raw,"");clean=clean.Replace("\r\n","\n").Replace("\r","\n").Replace("\n","\r\n");bool finished=false;if(clean.Contains(sentinel)){finished=true;clean=clean.Replace("echo "+sentinel,"");clean=clean.Replace(sentinel,"");}if(!IsDisposed){string toSend=clean;BeginInvoke((Action)(()=>{TryParsePrompt(toSend);AppendTextBeforeInput(toSend,isError?FG_ERR:FG);if(!string.IsNullOrEmpty(toSend)&&toSend.Trim().Length>0){if(_bannerSkipCount>0&&IsBannerLine(toSend.Trim())){_bannerSkipCount--;}else{_bannerSkipCount=0;string relay=PromptStrip.Replace(toSend,"");if(!string.IsNullOrWhiteSpace(relay)){if(!relay.EndsWith("\r\n")&&!relay.EndsWith("\n"))relay+="\r\n";OutputChunk?.Invoke(relay);}}}if(finished){StorageUtil.SanitizeFileEventually(_pendingSanitizePath,censorRegex);_pendingSanitizePath=null;if(inputStart<0)ShowPrompt();CommandFinished?.Invoke();}}));}}
    static readonly Regex PromptDetect=new(@"^([A-Za-z]:\\[^\r\n>]*)>",RegexOptions.Multiline);
    void TryParsePrompt(string text){var m=PromptDetect.Match(text);if(m.Success){string c=m.Groups[1].Value.Trim();if(Directory.Exists(c))workDir=c;}}
    void AppendText(string text,Color col){SelectionStart=TextLength;SelectionLength=0;SelectionColor=col;base.AppendText(text);ScrollToCaret();}
    void AppendTextBeforeInput(string text,Color col){if(string.IsNullOrEmpty(text))return;if(inputStart<0){AppendText(text,col);return;}string ci=TextLength>inputStart?Text.Substring(inputStart):"";Select(inputStart,TextLength-inputStart);SelectedText="";AppendText(text,col);inputStart=TextLength;SelectionColor=FG_INPUT;base.AppendText(ci);Select(TextLength,0);ScrollToCaret();}
    void ShowPrompt(){if(IsDisposed)return;AppendText($"{workDir}>",FG_PROMPT);inputStart=TextLength;historyPos=-1;}
    protected override bool IsInputKey(Keys k){if(k==Keys.Up||k==Keys.Down||k==Keys.Left||k==Keys.Right||k==Keys.Tab)return true;return base.IsInputKey(k);}
    protected override void OnKeyDown(KeyEventArgs e){switch(e.KeyCode){case Keys.Enter:e.SuppressKeyPress=true;SendCurrentLine();return;case Keys.Back:if(SelectionStart<=inputStart&&SelectionLength==0){e.SuppressKeyPress=true;return;}break;case Keys.Up:e.SuppressKeyPress=true;NavHist(+1);return;case Keys.Down:e.SuppressKeyPress=true;NavHist(-1);return;case Keys.Left:case Keys.Home:if(SelectionStart<=inputStart){e.SuppressKeyPress=true;return;}break;case Keys.C when e.Control:if(SelectionLength==0){e.SuppressKeyPress=true;SendCtrlC();return;}break;case Keys.L when e.Control:e.SuppressKeyPress=true;ClearScreen();return;}if(SelectionStart<inputStart&&e.KeyCode!=Keys.C&&e.KeyCode!=Keys.A)Select(TextLength,0);base.OnKeyDown(e);}
    protected override void OnKeyPress(KeyPressEventArgs e){if(SelectionStart<inputStart)Select(TextLength,0);base.OnKeyPress(e);}
    void SendCurrentLine(){if(!shellRunning||shell==null||inputStart<0)return;string input=TextLength>inputStart?Text.Substring(inputStart).TrimEnd('\r','\n'):"";SelectionColor=FG_INPUT;AppendText("\r\n");inputStart=-1;if(input.Trim().Length>0){history.Insert(0,input);if(history.Count>200)history.RemoveAt(history.Count-1);}historyPos=-1;if(input.Trim().Equals("exit",StringComparison.OrdinalIgnoreCase)){StopShell();AppendText("[Session ended.]\r\n",FG_ERR);return;}if(CommandGuard.ShouldBlock(input)){string failOut3=FailureEmulator.BuildOutput(ShellType.Cmd, input);if(!string.IsNullOrEmpty(failOut3)){string n3=failOut3.Replace("\r\n","\n").Replace("\n","\r\n");if(!n3.EndsWith("\r\n"))n3+="\r\n";OutputChunk?.Invoke(n3);}ShowPrompt();CommandFinished?.Invoke();return;}if(input.Trim().Equals("cls",StringComparison.OrdinalIgnoreCase)){ClearScreen();return;}_pendingSanitizePath=StorageUtil.TryGetRedirectTarget(input,workDir);var _openFile=StorageUtil.TryGetOpenedFileTarget(input,workDir);if(!string.IsNullOrWhiteSpace(_openFile))StorageUtil.SanitizeFileEventually(_openFile,censorRegex);try{shell.StandardInput.WriteLine(input);shell.StandardInput.WriteLine("echo "+sentinel);shell.StandardInput.Flush();}catch{AppendText("[Shell not running]\r\n",FG_ERR);ShowPrompt();}}
    void NavHist(int dir){if(history.Count==0)return;historyPos=Math.Max(-1,Math.Min(history.Count-1,historyPos+dir));string t=historyPos>=0?history[historyPos]:"";if(inputStart>=0){Select(inputStart,TextLength-inputStart);SelectionColor=FG_INPUT;SelectedText=t;Select(TextLength,0);}}
    void SendCtrlC(){try{if(shell!=null&&!shell.HasExited){NativeWin32.AttachConsole((uint)shell.Id);NativeWin32.SetConsoleCtrlHandler(IntPtr.Zero,true);NativeWin32.GenerateConsoleCtrlEvent(0,0);Thread.Sleep(100);NativeWin32.FreeConsole();NativeWin32.SetConsoleCtrlHandler(IntPtr.Zero,false);}}catch{}AppendText("^C\r\n",FG_ERR);inputStart=-1;ShowPrompt();}
    void ClearScreen(){Clear();inputStart=-1;ShowPrompt();ScreenCleared?.Invoke();}
}

// ════════════════════════════════════════════════════════════════════════════
// MAIN FORM
// ════════════════════════════════════════════════════════════════════════════
class MainForm : Form
{
    AppConfig cfg; bool silentStart; List<RunningScript> running=new(); Regex censorRegex;
    FakeTerminalSession ftSession=new(); ShellInterceptor shellInterceptor=new();

    // Tracks active admin/PS headless sessions
    readonly List<(HeadlessShellHost host, FakeTerminalSession session)> headlessSessions = new();

    TabControl tabs; CmdTerminal terminal; Label statusLabel,sessionStatusLabel,connStatusLabel; Panel runningPanel;
    NotifyIcon trayIcon; ContextMenuStrip trayMenu; System.Windows.Forms.Timer refreshTimer;
    int uiModalCount = 0;

    string FakeTerminalExePath { get { if (!string.IsNullOrEmpty(cfg.FakeTerminalPath) && StorageUtil.FileExists(cfg.FakeTerminalPath)) return cfg.FakeTerminalPath; return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Command Prompt.exe"); } }
    string FakePowerShellExePath { get { if (!string.IsNullOrEmpty(cfg.FakePowerShellPath) && StorageUtil.FileExists(cfg.FakePowerShellPath)) return cfg.FakePowerShellPath; return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Windows PowerShell.exe"); } }
    string LauncherExePath { get { if (!string.IsNullOrEmpty(cfg.SeiwareLauncherPath) && StorageUtil.FileExists(cfg.SeiwareLauncherPath)) return cfg.SeiwareLauncherPath; return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SeiwareLauncher.exe"); } }
    string CmdIcoPath { get { if (!string.IsNullOrEmpty(cfg.CmdTerminalIcoPath) && StorageUtil.FileExists(cfg.CmdTerminalIcoPath)) return cfg.CmdTerminalIcoPath; return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cmdterminal.ico"); } }
    string PsIcoPath { get { if (!string.IsNullOrEmpty(cfg.PowerShellIcoPath) && StorageUtil.FileExists(cfg.PowerShellIcoPath)) return cfg.PowerShellIcoPath; return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "powershell.ico"); } }

    /// <summary>Returns the correct fake exe path for the given shell type.</summary>
    string FakeExeFor(ShellType st) => st == ShellType.PowerShell ? FakePowerShellExePath : FakeTerminalExePath;
    string IcoFor(ShellType st) => st == ShellType.PowerShell ? PsIcoPath : CmdIcoPath;

    // ═══ UNIFIED SHELL LAUNCH — works for CMD and PS, normal and admin ═══
    void LaunchShell(ShellType shellType, bool admin)
    {
        string pn = "SeiwareFT_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var ftSess = new FakeTerminalSession(pn);
        var host = new HeadlessShellHost(shellType, censorRegex);

        // Wire: HeadlessShellHost → pipe → FakeTerminal display
        host.OutputChunk    += t  => ftSess.SendOutput(t);
        host.CommandFinished += () => ftSess.SendCommandFinished();
        host.ScreenCleared  += () => ftSess.SendClear();

        // Wire: FakeTerminal input → pipe → HeadlessShellHost
        ftSess.CommandReceived += cmd => host.SendCommand(cmd);
        ftSess.CtrlCReceived   += ()  => host.SendCtrlC();
        ftSess.FakeTermClosed  += ()  => { host.Stop(); host.Dispose(); };

        string fakeExe = FakeExeFor(shellType);
        string workDir = admin ? @"C:\Windows\System32" : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // CRITICAL: Suppress interceptor BEFORE starting the backend shell.
        // HeadlessShellHost.Start() spawns a real cmd.exe or powershell.exe —
        // without suppression the interceptor kills it before we can even
        // read its PID. 3s is enough for the process to start + get registered.
        shellInterceptor.SuppressFor(3000);

        if (!host.Start(workDir))
        {
            string shellName = shellType == ShellType.PowerShell ? "powershell.exe" : "cmd.exe";
            ShowOwnedMessage($"Failed to start {shellName}\n\nMake sure \"{Path.GetFileName(fakeExe)}\" exists next to Seiware.exe", "Seiware", MessageBoxButtons.OK, MessageBoxIcon.Error);
            host.Dispose(); ftSess.Dispose();
            return;
        }

        // Now register the backend PID as exempt for future interceptions
        // (after suppression window expires)
        if (host.ShellPid > 0)
            shellInterceptor.RegisterExemptPid(host.ShellPid);

        // Build FakeTerminal args — the exe auto-detects shell type from its name,
        // so we don't need --shell. We pass it as a safety net anyway.
        string ba = cfg.BannedNames != null && cfg.BannedNames.Count > 0
            ? $" --banned \"{string.Join("|", cfg.BannedNames.Select(w => w.Replace("\"", "")))}\""
            : "";
        string ico = IcoFor(shellType);
        // If shell-specific icon not found, fall back to cmdterminal.ico
        if (!StorageUtil.FileExists(ico)) ico = CmdIcoPath;
        string iconArg = StorageUtil.FileExists(ico) ? $" --icon \"{ico}\"" : "";
        string configArg = $" --config \"{AppConfig.ConfigPath}\"";
        string shellArg = shellType == ShellType.PowerShell ? " --shell ps" : "";
        string adminArg = admin ? " --admin" : "";
        string extraArgs = $"{adminArg}{shellArg}{ba}{iconArg}{configArg}".Trim();

        try
        {
            ftSess.Start(fakeExe, workDir, LauncherExePath, elevated: admin, extraArgs: extraArgs);
            if (ftSess.FakeTermPid > 0)
                shellInterceptor.RegisterExemptPid(ftSess.FakeTermPid);
            headlessSessions.Add((host, ftSess));
        }
        catch (Exception ex)
        {
            host.Dispose(); ftSess.Dispose();
            if (!ex.Message.Contains("1223") && !ex.Message.ToLower().Contains("cancelled"))
                ShowOwnedMessage($"Failed: {ex.Message}", "Seiware", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public MainForm(bool silent)
    {
        silentStart=silent;cfg=AppConfig.Load();RebuildRegex();CommandGuard.SetBannedNames(cfg.BannedNames);BuildUI();BuildTray();WireInterceptor();
        CommandGuard.Blocked += (name) => { try { if (!IsDisposed) BeginInvoke((Action)(() => CaptureProofNotify($"Command guard blocked: {name}", isWarning:true))); } catch { } };
        if(silentStart){ShowInTaskbar=false;Visible=false;}
        refreshTimer=new System.Windows.Forms.Timer{Interval=2000};refreshTimer.Tick+=(s,e)=>RefreshRunningPanel();refreshTimer.Start();
    }

    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);NativeWin32.SetWindowDisplayAffinity(this.Handle,NativeWin32.WDA_EXCLUDEFROMCAPTURE);}

    void WireInterceptor()
    {
        // Normal interception: CMD → open embedded CMD tab, PS → launch fake PS exe
        shellInterceptor.OnInterceptNormal = (shellType) => {
            if (IsDisposed) return;
            // Respect config toggle for PS
            if (shellType == ShellType.PowerShell && !cfg.InterceptPowerShell) return;
            BeginInvoke((Action)(() => {
                if (shellType == ShellType.Cmd)
                {
                    ShowMain();
                    if (tabs != null && tabs.TabPages.Count > 1) tabs.SelectedIndex = 1;
                    if (!ftSession.IsRunning) StartSession();
                    CaptureProofNotify("CMD intercepted");
                }
                else
                {
                    LaunchShell(ShellType.PowerShell, admin: false);
                    CaptureProofNotify("PowerShell intercepted");
                }
            }));
        };

        // Admin interception: launch correct fake exe (Command Prompt.exe or Windows PowerShell.exe)
        shellInterceptor.OnInterceptAdmin = (shellType) => {
            if (IsDisposed) return;
            if (shellType == ShellType.PowerShell && !cfg.InterceptPowerShell) return;
            BeginInvoke((Action)(() => {
                LaunchShell(shellType, admin: true);
                string name = shellType == ShellType.PowerShell ? "Admin PowerShell" : "Admin CMD";
                CaptureProofNotify($"{name} intercepted", isWarning: true);
            }));
        };

        shellInterceptor.OnControlPanelBlocked = () => {
            if (IsDisposed) return;
            BeginInvoke((Action)(() => CaptureProofNotify("Control Panel blocked", isWarning: true)));
        };

        shellInterceptor.Start(); UpdateStatus();
    }

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80;

    void CaptureProofNotify(string msg, bool isWarning = false)
    {
        var accentCol = isWarning ? C_AMBER : C_ACCENT2;
        var n = new Form { FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, TopMost = true, StartPosition = FormStartPosition.Manual, Size = new Size(320, 54), BackColor = C_CARD, Opacity = 0.95 };
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0,0,1920,1080);
        n.Location = new Point(wa.Right - n.Width - 16, wa.Bottom - n.Height - 12);
        n.Paint += (s, e) => { using var pen = new Pen(C_BORDER); e.Graphics.DrawRectangle(pen, 0, 0, n.Width - 1, n.Height - 1); };
        n.Controls.Add(new Panel { Location = new Point(0, 0), Size = new Size(3, n.Height), BackColor = accentCol });
        n.Controls.Add(new Label { Text = isWarning ? "⚠" : "◈", AutoSize = true, Font = new Font("Segoe UI", 14f), ForeColor = accentCol, Location = new Point(12, 12), BackColor = Color.Transparent });
        n.Controls.Add(new Label { Text = msg, Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = isWarning ? C_AMBER : C_FG, Location = new Point(40, 0), Size = new Size(n.Width - 50, n.Height), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent });
        n.HandleCreated += (s, e) => { int ex = GetWindowLong(n.Handle, GWL_EXSTYLE); SetWindowLong(n.Handle, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW); NativeWin32.SetWindowDisplayAffinity(n.Handle, NativeWin32.WDA_EXCLUDEFROMCAPTURE); };
        n.Show(this);
        var t = new System.Windows.Forms.Timer { Interval = 3500 };
        t.Tick += (s, e) => { t.Stop(); t.Dispose(); if (!n.IsDisposed) n.Close(); };
        t.Start();
    }

    void BuildTray()
    {
        trayMenu=new ContextMenuStrip();trayMenu.Items.Add("Open Seiware").Click+=(s,e)=>ShowMain();trayMenu.Items.Add("Stop All").Click+=(s,e)=>StopAllScripts();
        trayMenu.Items.Add(new ToolStripSeparator());
        var ii=new ToolStripMenuItem("Intercept: ...");ii.Click+=(s,e)=>{if(shellInterceptor.IsEnabled)shellInterceptor.Stop();else shellInterceptor.Start();ii.Text=shellInterceptor.IsEnabled?"Intercept On":"Intercept Off";UpdateStatus();};
        trayMenu.Opening+=(s,e)=>{ii.Text=shellInterceptor.IsEnabled?"Intercept On":"Intercept Off";};trayMenu.Items.Add(ii);
        trayMenu.Items.Add(new ToolStripSeparator());trayMenu.Items.Add("Exit").Click+=(s,e)=>FullExit();
        Icon ic;try{string p=Path.Combine(AppContext.BaseDirectory,"Seiware.ico");ic=File.Exists(p)?new Icon(p):SystemIcons.Application;}catch{ic=SystemIcons.Application;}
        trayIcon=new NotifyIcon{Icon=ic,Text="Seiware",Visible=true,ContextMenuStrip=trayMenu};trayIcon.DoubleClick+=(s,e)=>ShowMain();
    }

    void ShowMain(){Show();ShowInTaskbar=true;WindowState=FormWindowState.Normal;BringToFront();Activate();}
    T WithUiModal<T>(Func<T> f){uiModalCount++;try{return f();}finally{uiModalCount--;}}
    void WithUiModal(Action a){uiModalCount++;try{a();}finally{uiModalCount--;}}
    DialogResult ShowOwnedMessage(string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon)=>WithUiModal(()=>MessageBox.Show(this,text,caption,buttons,icon));
    DialogResult ShowOwnedFileDialog(FileDialog dlg)=>WithUiModal(()=>dlg.ShowDialog(this));
    DialogResult ShowOwnedDialog(Form dlg)=>WithUiModal(()=>dlg.ShowDialog(this));
    protected override void OnDeactivate(EventArgs e){base.OnDeactivate(e);if(uiModalCount>0)return;Hide();ShowInTaskbar=false;}
    protected override void OnFormClosing(FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();ShowInTaskbar=false;}else base.OnFormClosing(e);}
    void FullExit(){var r=ShowOwnedMessage("Stop all running scripts before exiting?","Seiware",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);if(r==DialogResult.Cancel)return;if(r==DialogResult.Yes)StopAllScripts();shellInterceptor?.Dispose();EndSession();terminal?.StopShell();foreach(var(h,s)in headlessSessions){h.Dispose();s.Dispose();}headlessSessions.Clear();refreshTimer?.Dispose();trayIcon.Visible=false;trayIcon.Dispose();Application.Exit();}

    static readonly Color C_BG=Color.FromArgb(12,10,18),C_CARD=Color.FromArgb(20,17,32),C_BORDER=Color.FromArgb(44,38,68),C_FG=Color.FromArgb(225,222,240),C_FG_DIM=Color.FromArgb(130,120,160),C_ACCENT=Color.FromArgb(120,90,255),C_ACCENT2=Color.FromArgb(60,180,255),C_GREEN=Color.FromArgb(50,200,120),C_RED=Color.FromArgb(220,60,80),C_AMBER=Color.FromArgb(240,180,50),C_BTN_BG=Color.FromArgb(36,30,58),C_BTN_HOV=Color.FromArgb(50,42,78);
    // PS windows only spawn via ShellInterceptor — no manual PS buttons

    void BuildUI()
    {
        Text="Seiware";Size=new Size(1020,700);MinimumSize=new Size(780,520);BackColor=C_BG;ForeColor=C_FG;Font=new Font("Segoe UI",9.5f);StartPosition=FormStartPosition.CenterScreen;
        try{string p=Path.Combine(AppContext.BaseDirectory,"Seiware.ico");if(File.Exists(p))Icon=new Icon(p);}catch{}
        var header=new Panel{Dock=DockStyle.Top,Height=56};
        header.Paint+=(s,e)=>{using var br=new System.Drawing.Drawing2D.LinearGradientBrush(header.ClientRectangle,Color.FromArgb(26,18,52),Color.FromArgb(14,10,28),System.Drawing.Drawing2D.LinearGradientMode.Horizontal);e.Graphics.FillRectangle(br,header.ClientRectangle);using var pen=new Pen(Color.FromArgb(50,40,90));e.Graphics.DrawLine(pen,0,header.Height-1,header.Width,header.Height-1);};
        header.Controls.Add(new Label{Text="◈",AutoSize=true,ForeColor=C_ACCENT,Font=new Font("Segoe UI",16f,FontStyle.Bold),Location=new Point(16,15),BackColor=Color.Transparent});
        header.Controls.Add(new Label{Text="Seiware",AutoSize=true,ForeColor=Color.White,Font=new Font("Segoe UI Semibold",13f),Location=new Point(44,17),BackColor=Color.Transparent});
        header.Controls.Add(new Label{Text="v2.0",AutoSize=true,ForeColor=C_FG_DIM,Font=new Font("Segoe UI",8f),Location=new Point(120,23),BackColor=Color.Transparent});
        var btnSettings=MakeHeaderBtn("⚙  Settings",520);btnSettings.Click+=(s,e)=>OpenSettings();header.Controls.Add(btnSettings);
        var btnStopAll=MakeHeaderBtn("■  Stop All",660,true);btnStopAll.Click+=(s,e)=>StopAllScripts();header.Controls.Add(btnStopAll);
        statusLabel=new Label{Dock=DockStyle.Bottom,Height=26,BackColor=Color.FromArgb(10,8,16),ForeColor=C_FG_DIM,Font=new Font("Segoe UI",8f),TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(10,0,0,0),Text="  Ready"};
        tabs=new TabControl{Dock=DockStyle.Fill,Font=new Font("Segoe UI",9.5f)};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,56));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,26));
        layout.Controls.Add(header,0,0);layout.Controls.Add(tabs,0,1);layout.Controls.Add(statusLabel,0,2);Controls.Add(layout);
        BuildLauncherTab();BuildCmdTab();BuildConfigTab();
    }

    Button MakeHeaderBtn(string text,int x,bool danger=false){var bg=danger?Color.FromArgb(90,30,40):C_BTN_BG;var bd=danger?Color.FromArgb(160,55,70):Color.FromArgb(80,65,140);var hov=danger?Color.FromArgb(120,40,55):C_BTN_HOV;var b=new Button{Text=text,FlatStyle=FlatStyle.Flat,BackColor=bg,ForeColor=Color.White,Font=new Font("Segoe UI",9f,FontStyle.Bold),Size=new Size(130,32),Location=new Point(x,12),Cursor=Cursors.Hand};b.FlatAppearance.BorderSize=1;b.FlatAppearance.BorderColor=bd;b.FlatAppearance.MouseOverBackColor=hov;return b;}
    string ScriptPath(string name){string configured=name switch{"worker.ps1"=>cfg.WorkerScriptPath,"Memory_Optimizer.ps1"=>cfg.MemoryScriptPath,"LOL.ps1"=>cfg.LolScriptPath,"HideFiles.ps1"=>cfg.HideFilesScriptPath,_=>""};if(!string.IsNullOrEmpty(configured)&&StorageUtil.FileExists(configured))return configured;string local=Path.Combine(AppContext.BaseDirectory,name);if(StorageUtil.FileExists(local))return local;string scripts=Path.Combine(AppConfig.ScriptsDir,name);if(StorageUtil.FileExists(scripts))return scripts;return!string.IsNullOrEmpty(configured)?configured:local;}

    void BuildLauncherTab()
    {
        var page=new TabPage("  ⚡ Scripts  "){BackColor=C_BG};tabs.TabPages.Add(page);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,BackColor=C_BG};layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,110));page.Controls.Add(layout);
        var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=C_BG,Padding=new Padding(16,14,16,14)};layout.Controls.Add(scroll,0,0);
        scroll.Controls.Add(new Label{Text="⚡  Script Launcher",AutoSize=true,Font=new Font("Segoe UI Semibold",12f),ForeColor=C_ACCENT,Location=new Point(14,8),BackColor=Color.Transparent});
        scroll.Controls.Add(new Label{Text="All scripts run elevated (Admin).",AutoSize=false,Size=new Size(860,20),Font=new Font("Segoe UI",8.5f),ForeColor=C_FG_DIM,Location=new Point(14,34),BackColor=Color.Transparent});
        var scripts=new[]{("LOL.ps1","MuiCache Cleaner","Tray app — intercepts Ctrl+F in regedit, cleans MuiCache.","🧹"),("Memory_Optimizer.ps1","App Hotkey Launcher","Numpad + launches app hidden, Numpad − kills it.","🎮"),("worker.ps1","Overlay + newui Worker","PNG overlay on backslash. Numpad 9 → newui.","🖥"),("HideFiles.ps1","File Visibility Manager","Hides/unhides with +h +s attribs and search index.","📁")};
        int cardY=58;foreach(var(file,name,desc,icon)in scripts){var card=MakeScriptCard(file,name,desc,icon,ScriptPath(file),scroll.Width-32);card.Location=new Point(14,cardY);scroll.Controls.Add(card);cardY+=card.Height+10;}
        scroll.Resize+=(s,e)=>{int w=scroll.Width-32;if(w<200)return;int y=58;foreach(var c in scroll.Controls.OfType<Panel>()){c.Width=w;c.Location=new Point(14,y);y+=c.Height+10;}};
        runningPanel=new Panel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(14,11,22),Padding=new Padding(16,8,16,8)};
        var runBorder=new Panel{Dock=DockStyle.Top,Height=1,BackColor=C_BORDER};var runWrap=new Panel{Dock=DockStyle.Fill};runWrap.Controls.Add(runningPanel);runWrap.Controls.Add(runBorder);layout.Controls.Add(runWrap,0,1);RefreshRunningPanel();
    }

    Panel MakeScriptCard(string file,string name,string desc,string icon,string scriptPath,int width)
    {
        var card=new Panel{Width=width,Height=78,BackColor=C_CARD};var accent=new Panel{Location=new Point(0,0),Size=new Size(3,78)};accent.Paint+=(s,e)=>{using var br=new System.Drawing.Drawing2D.LinearGradientBrush(accent.ClientRectangle,C_ACCENT,C_ACCENT2,System.Drawing.Drawing2D.LinearGradientMode.Vertical);e.Graphics.FillRectangle(br,accent.ClientRectangle);};card.Controls.Add(accent);card.Paint+=(s,e)=>{using var pen=new Pen(C_BORDER);e.Graphics.DrawRectangle(pen,0,0,card.Width-1,card.Height-1);};card.Controls.Add(new Label{Text=icon,AutoSize=true,Font=new Font("Segoe UI",14f),ForeColor=C_FG,Location=new Point(14,14),BackColor=Color.Transparent});card.Controls.Add(new Label{Text=name,AutoSize=true,Font=new Font("Segoe UI Semibold",10f),ForeColor=Color.White,Location=new Point(42,10),BackColor=Color.Transparent});bool exists=StorageUtil.FileExists(scriptPath);card.Controls.Add(new Label{Text=file,AutoSize=true,Font=new Font("Consolas",7.5f),ForeColor=exists?C_GREEN:Color.FromArgb(180,80,80),Location=new Point(42,30),BackColor=Color.Transparent});var descLbl=new Label{Text=desc,AutoSize=false,Size=new Size(width-170,30),Font=new Font("Segoe UI",8f),ForeColor=C_FG_DIM,Location=new Point(42,46),BackColor=Color.Transparent};card.Controls.Add(descLbl);var btn=new Button{Text="▶  Launch",Size=new Size(108,32),Location=new Point(width-124,23),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(30,110,60),ForeColor=Color.FromArgb(180,255,200),Font=new Font("Segoe UI",9f,FontStyle.Bold),Cursor=Cursors.Hand};btn.FlatAppearance.BorderSize=1;btn.FlatAppearance.BorderColor=Color.FromArgb(50,180,90);btn.FlatAppearance.MouseOverBackColor=Color.FromArgb(40,140,75);card.Controls.Add(btn);var captFile=file;var captPath=scriptPath;btn.Click+=(s,e)=>LaunchScript(captFile,captPath,btn);card.Resize+=(s,e)=>{int w=card.Width-170;if(w>40){descLbl.Width=w;btn.Location=new Point(card.Width-124,23);}accent.Height=card.Height;};return card;
    }

    void RefreshRunningPanel(){if(runningPanel==null||runningPanel.IsDisposed)return;running.RemoveAll(r=>!r.IsAlive);runningPanel.SuspendLayout();runningPanel.Controls.Clear();var hdr=new Label{Text=running.Count==0?"  No scripts running":$"  ● {running.Count} script(s) active",AutoSize=true,Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=running.Count==0?C_FG_DIM:C_GREEN,Location=new Point(0,8),BackColor=Color.Transparent};runningPanel.Controls.Add(hdr);int x=10;foreach(var r in running){var pill=new Panel{Size=new Size(160,26),Location=new Point(x,34),BackColor=Color.FromArgb(25,55,35)};pill.Paint+=(s,e)=>{using var pen=new Pen(Color.FromArgb(50,140,70));e.Graphics.DrawRectangle(pen,0,0,pill.Width-1,pill.Height-1);};pill.Controls.Add(new Label{Text=$"✓ {r.Name}",AutoSize=true,Font=new Font("Segoe UI",8f),ForeColor=C_GREEN,Location=new Point(6,4),BackColor=Color.Transparent});var xBtn=new Button{Text="✕",Size=new Size(22,22),Location=new Point(134,2),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(100,30,30),ForeColor=Color.White,Font=new Font("Segoe UI",7f),Cursor=Cursors.Hand,Tag=r};xBtn.FlatAppearance.BorderSize=0;xBtn.Click+=(s,e)=>{((RunningScript)((Button)s!).Tag!).Stop();RefreshRunningPanel();};pill.Controls.Add(xBtn);runningPanel.Controls.Add(pill);x+=170;}if(running.Count>0){var sa=new Button{Text="■  Stop All",AutoSize=true,Location=new Point(x+10,36),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(90,25,30),ForeColor=Color.FromArgb(255,180,180),Font=new Font("Segoe UI",8f,FontStyle.Bold),Cursor=Cursors.Hand};sa.FlatAppearance.BorderSize=1;sa.FlatAppearance.BorderColor=Color.FromArgb(140,50,55);sa.Click+=(s,e)=>{StopAllScripts();RefreshRunningPanel();};runningPanel.Controls.Add(sa);}runningPanel.ResumeLayout();UpdateStatus();}

    void BuildCmdTab()
    {
        var page=new TabPage("  ▸ CMD  "){BackColor=C_BG};tabs.TabPages.Add(page);
        var bar=new Panel{Dock=DockStyle.Top,Height=48};
        bar.Paint+=(s,e)=>{using var br=new System.Drawing.Drawing2D.LinearGradientBrush(bar.ClientRectangle,Color.FromArgb(22,16,40),Color.FromArgb(14,11,24),System.Drawing.Drawing2D.LinearGradientMode.Vertical);e.Graphics.FillRectangle(br,bar.ClientRectangle);using var pen=new Pen(C_BORDER);e.Graphics.DrawLine(pen,0,bar.Height-1,bar.Width,bar.Height-1);};
        int bx=10;
        Button CB(string t,Color bg,Color bd){var b=new Button{Text=t,Location=new Point(bx,8),Size=new Size(140,32),FlatStyle=FlatStyle.Flat,BackColor=bg,ForeColor=Color.White,Font=new Font("Segoe UI",8.5f,FontStyle.Bold),Cursor=Cursors.Hand};b.FlatAppearance.BorderSize=1;b.FlatAppearance.BorderColor=bd;b.FlatAppearance.MouseOverBackColor=ControlPaint.Light(bg,0.15f);bar.Controls.Add(b);bx+=148;return b;}
        var btnStart=CB("▶  Start Session",Color.FromArgb(25,90,50),Color.FromArgb(50,170,90));
        var btnStop=CB("■  End Session",Color.FromArgb(90,25,35),Color.FromArgb(170,55,65));
        var btnClear=CB("⌫  Clear",C_BTN_BG,C_BORDER);
        var btnAdmin=CB("⊞  Admin CMD",Color.FromArgb(80,55,15),Color.FromArgb(170,120,40));

        var sbar=new Panel{Dock=DockStyle.Top,Height=30,BackColor=Color.FromArgb(14,11,22)};
        sbar.Paint+=(s,e)=>{using var pen=new Pen(C_BORDER);e.Graphics.DrawLine(pen,0,sbar.Height-1,sbar.Width,sbar.Height-1);};
        sessionStatusLabel=new Label{Text="● Session: Inactive",AutoSize=true,Location=new Point(12,7),Font=new Font("Segoe UI",8.5f),ForeColor=C_FG_DIM,BackColor=Color.Transparent};
        connStatusLabel=new Label{Text="○ FakeTerminal: Disconnected",AutoSize=true,Location=new Point(240,7),Font=new Font("Segoe UI",8.5f),ForeColor=C_FG_DIM,BackColor=Color.Transparent};
        sbar.Controls.Add(sessionStatusLabel);sbar.Controls.Add(connStatusLabel);

        terminal=new CmdTerminal{Dock=DockStyle.Fill};terminal.SetCensorRegex(censorRegex);
        terminal.OutputChunk+=text=>ftSession.SendOutput(text);
        terminal.ScreenCleared+=()=>ftSession.SendClear();
        terminal.CommandFinished+=()=>ftSession.SendCommandFinished();
        ftSession.CommandReceived+=cmd=>{if(!terminal.IsDisposed)terminal.BeginInvoke((Action)(()=>terminal.SendCommand(cmd)));};
        ftSession.CtrlCReceived+=()=>{if(!terminal.IsDisposed)terminal.BeginInvoke((Action)(()=>terminal.ExternalCtrlC()));};
        ftSession.FakeTermClosed+=()=>{if(!connStatusLabel.IsDisposed)connStatusLabel.BeginInvoke((Action)(()=>UpdStat(true,false)));};

        btnStart.Click+=(s,e)=>StartSession();
        btnStop.Click+=(s,e)=>EndSession();
        btnClear.Click+=(s,e)=>{terminal.Clear();ftSession.SendClear();};
        btnAdmin.Click+=(s,e)=>LaunchShell(ShellType.Cmd, admin: true);

        bar.Controls.Add(new Label { Text = "↑↓ history  •  Ctrl+C break  •  Ctrl+L clear  •  PS via interceptor", AutoSize = true, Font = new Font("Segoe UI", 7.5f), ForeColor = C_FG_DIM, Location = new Point(bx + 12, 16), BackColor = Color.Transparent });

        page.Controls.Add(terminal);page.Controls.Add(sbar);page.Controls.Add(bar);bar.BringToFront();sbar.BringToFront();
        terminal.AppendText("─────────────────────────────────────────\r\n  Seiware  │  Embedded CMD Terminal\r\n─────────────────────────────────────────\r\n  Click  ▶ Start Session  to begin.\r\n  PowerShell is intercepted automatically.\r\n\r\n");
    }

    void StartSession(){if(!StorageUtil.FileExists(FakeTerminalExePath)){ShowOwnedMessage($"Command Prompt.exe not found at:\n{FakeTerminalExePath}","Seiware",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}shellInterceptor.SuppressFor(3000);terminal.StartShell();if(terminal.ShellPid>0)shellInterceptor.RegisterExemptPid(terminal.ShellPid);try{string workDir=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);ftSession.Start(FakeTerminalExePath,workDir,LauncherExePath);if(ftSession.FakeTermPid>0)shellInterceptor.RegisterExemptPid(ftSession.FakeTermPid);UpdStat(true,false);var pt=new System.Windows.Forms.Timer{Interval=500};int polls=0;pt.Tick+=(s,e)=>{polls++;if(ftSession.IsConnected){pt.Stop();pt.Dispose();UpdStat(true,true);}else if(polls>30){pt.Stop();pt.Dispose();}};pt.Start();}catch(Exception ex){ShowOwnedMessage($"Failed:\n{ex.Message}","Seiware",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
    void EndSession(){ftSession.SendSessionEnded();Thread.Sleep(300);terminal.StopShell();ftSession.Dispose();UpdStat(false,false);}
    void UpdStat(bool active,bool conn){if(sessionStatusLabel.IsDisposed)return;if(sessionStatusLabel.InvokeRequired){sessionStatusLabel.BeginInvoke((Action)(()=>UpdStat(active,conn)));return;}sessionStatusLabel.Text=active?"● Session: Active":"● Session: Inactive";sessionStatusLabel.ForeColor=active?C_GREEN:C_FG_DIM;connStatusLabel.Text=conn?"◉ FakeTerminal: Connected":(active?"○ Connecting...":"○ FakeTerminal: Disconnected");connStatusLabel.ForeColor=conn?C_ACCENT2:(active?C_AMBER:C_FG_DIM);}

    void BuildConfigTab()
    {
        var page=new TabPage("  ⚙ Config  "){BackColor=C_BG};tabs.TabPages.Add(page);
        var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=C_BG};page.Controls.Add(scroll);
        int y=16;
        scroll.Controls.Add(new Label{Text="⚙  Configuration",AutoSize=true,Font=new Font("Segoe UI Semibold",12f),ForeColor=C_ACCENT,Location=new Point(16,y),BackColor=Color.Transparent});y+=36;

        // ── Buttons row ──
        var btnOpen=MakeConfigBtn("📝  Open in Notepad",16,y);btnOpen.Click+=(s,e)=>{try{Process.Start("notepad.exe",AppConfig.ConfigPath);}catch{}};scroll.Controls.Add(btnOpen);
        var btnWizard=MakeConfigBtn("🔧  Re-run Setup Wizard",200,y);btnWizard.Click+=(s,e)=>{var wiz=new SetupWizard();if(ShowOwnedDialog(wiz)==DialogResult.OK){cfg=AppConfig.Load();RebuildRegex();terminal?.SetCensorRegex(censorRegex);UpdateStatus();}};scroll.Controls.Add(btnWizard);
        var btnBanned=MakeConfigBtn("🚫  Manage Banned Names",430,y);btnBanned.Click+=(s,e)=>OpenSettings();scroll.Controls.Add(btnBanned);y+=46;

        // ── Checkboxes ──
        var cbPS=new CheckBox{Text="  Intercept PowerShell (powershell.exe / pwsh.exe)",AutoSize=true,Location=new Point(20,y),ForeColor=C_FG,FlatStyle=FlatStyle.System,Font=new Font("Segoe UI",9.5f),Checked=cfg.InterceptPowerShell};
        cbPS.CheckedChanged+=(s,e)=>{cfg.InterceptPowerShell=cbPS.Checked;cfg.Save();};scroll.Controls.Add(cbPS);y+=28;
        var cb=new CheckBox{Text="  Start Seiware automatically with Windows",AutoSize=true,Location=new Point(20,y),ForeColor=C_FG,FlatStyle=FlatStyle.System,Font=new Font("Segoe UI",9.5f),Checked=cfg.StartWithWindows};
        cb.CheckedChanged+=(s,e)=>{cfg.StartWithWindows=cb.Checked;cfg.Save();cfg.ApplyStartWithWindows();};scroll.Controls.Add(cb);y+=28;
        var ci=new CheckBox{Text="  Intercept shell launches (requires Administrator)",AutoSize=true,Location=new Point(20,y),ForeColor=C_FG,FlatStyle=FlatStyle.System,Font=new Font("Segoe UI",9.5f),Checked=shellInterceptor.IsEnabled};
        ci.CheckedChanged+=(s,e)=>{if(ci.Checked)shellInterceptor.Start();else shellInterceptor.Stop();UpdateStatus();};scroll.Controls.Add(ci);y+=36;

        // ── Bootstrap overrides — stored OUTSIDE config.json for future-proofing ──
        scroll.Controls.Add(new Label{Text="── Bootstrap Overrides ──  (saved in Seiware.bootstrap.json next to Seiware.exe)",AutoSize=true,Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=C_ACCENT,Location=new Point(16,y),BackColor=Color.Transparent});y+=24;
        scroll.Controls.Add(new Label{Text="config.json path",AutoSize=true,Font=new Font("Consolas",9f),ForeColor=C_FG,Location=new Point(20,y),BackColor=Color.Transparent});
        var tbBootstrapConfig=new TextBox{Text=AppConfig.ConfigOverridePath,Size=new Size(420,22),Location=new Point(220,y-2),BackColor=Color.FromArgb(24,20,38),ForeColor=C_FG,BorderStyle=BorderStyle.FixedSingle,Font=new Font("Consolas",8f),PlaceholderText=AppConfig.ConfigPath};
        scroll.Controls.Add(tbBootstrapConfig);y+=28;
        scroll.Controls.Add(new Label{Text="scripts folder path",AutoSize=true,Font=new Font("Consolas",9f),ForeColor=C_FG,Location=new Point(20,y),BackColor=Color.Transparent});
        var tbBootstrapScripts=new TextBox{Text=AppConfig.ScriptsOverrideDir,Size=new Size(420,22),Location=new Point(220,y-2),BackColor=Color.FromArgb(24,20,38),ForeColor=C_FG,BorderStyle=BorderStyle.FixedSingle,Font=new Font("Consolas",8f),PlaceholderText=AppConfig.ScriptsDir};
        scroll.Controls.Add(tbBootstrapScripts);y+=34;

        // ── File Paths — tell Seiware exactly where each component lives ──
        scroll.Controls.Add(new Label{Text="── Executables & Icons ──  (blank = auto-detect next to Seiware.exe)",AutoSize=true,Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=C_ACCENT,Location=new Point(16,y),BackColor=Color.Transparent});y+=24;

        // We collect all textboxes so the Save button can read them
        var pathTextBoxes = new Dictionary<string, TextBox>();

        var pathDefs = new (string key, string label, string configVal, string filter)[] {
            ("FakeTerminalPath",     "Command Prompt.exe",      cfg.FakeTerminalPath ?? "",    "EXE|*.exe|All|*.*"),
            ("FakePowerShellPath",   "Windows PowerShell.exe",  cfg.FakePowerShellPath ?? "",  "EXE|*.exe|All|*.*"),
            ("SeiwareLauncherPath",  "SeiwareLauncher.exe",     cfg.SeiwareLauncherPath ?? "", "EXE|*.exe|All|*.*"),
            ("CmdTerminalIcoPath",   "cmdterminal.ico",         cfg.CmdTerminalIcoPath ?? "",  "ICO|*.ico|All|*.*"),
            ("PowerShellIcoPath",    "powershell.ico",          cfg.PowerShellIcoPath ?? "",   "ICO|*.ico|All|*.*"),
        };

        foreach (var (key, label, curVal, filter) in pathDefs)
        {
            string defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, label);
            bool found = !string.IsNullOrEmpty(curVal) ? StorageUtil.FileExists(curVal) : StorageUtil.FileExists(defaultPath);
            scroll.Controls.Add(new Label{Text=(found?"✓ ":"✕ ")+label,AutoSize=true,Font=new Font("Consolas",9f),ForeColor=found?C_GREEN:C_RED,Location=new Point(20,y),BackColor=Color.Transparent});
            var tb=new TextBox{Text=curVal,Size=new Size(420,22),Location=new Point(220,y-2),BackColor=Color.FromArgb(24,20,38),ForeColor=C_FG,BorderStyle=BorderStyle.FixedSingle,Font=new Font("Consolas",8f),PlaceholderText=defaultPath};
            scroll.Controls.Add(tb); pathTextBoxes[key]=tb;
            var captFilter=filter; var captLabel=label; var captTb=tb;
            var btnBr=new Button{Text="...",Size=new Size(30,22),Location=new Point(646,y-2),FlatStyle=FlatStyle.Flat,BackColor=C_BTN_BG,ForeColor=C_FG,Font=new Font("Segoe UI",8f),Cursor=Cursors.Hand};
            btnBr.FlatAppearance.BorderSize=1;btnBr.FlatAppearance.BorderColor=C_BORDER;
            btnBr.Click+=(s,e)=>{using var ofd=new OpenFileDialog{Title=$"Locate {captLabel}",Filter=captFilter};if(ShowOwnedFileDialog(ofd)==DialogResult.OK)captTb.Text=ofd.FileName;};
            scroll.Controls.Add(btnBr);
            var btnX=new Button{Text="×",Size=new Size(22,22),Location=new Point(680,y-2),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(80,30,30),ForeColor=Color.FromArgb(255,150,150),Font=new Font("Segoe UI",8f),Cursor=Cursors.Hand};
            btnX.FlatAppearance.BorderSize=0; btnX.Click+=(s,e)=>captTb.Text="";
            scroll.Controls.Add(btnX);
            y+=28;
        }

        // ── PS1 Scripts ──
        y+=6;
        scroll.Controls.Add(new Label{Text="── PowerShell Scripts ──  (blank = auto-generate in %AppData%\\Seiware\\scripts)",AutoSize=true,Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=C_ACCENT2,Location=new Point(16,y),BackColor=Color.Transparent});y+=24;

        var scriptDefs = new (string key, string label, string configVal)[] {
            ("WorkerScriptPath",    "worker.ps1",              cfg.WorkerScriptPath ?? ""),
            ("MemoryScriptPath",    "Memory_Optimizer.ps1",    cfg.MemoryScriptPath ?? ""),
            ("LolScriptPath",       "LOL.ps1",                 cfg.LolScriptPath ?? ""),
            ("HideFilesScriptPath", "HideFiles.ps1",           cfg.HideFilesScriptPath ?? ""),
        };

        foreach (var (key, label, curVal) in scriptDefs)
        {
            string defaultPath = Path.Combine(AppConfig.ScriptsDir, label);
            bool found = !string.IsNullOrEmpty(curVal) ? StorageUtil.FileExists(curVal) : StorageUtil.FileExists(defaultPath);
            scroll.Controls.Add(new Label{Text=(found?"✓ ":"✕ ")+label,AutoSize=true,Font=new Font("Consolas",9f),ForeColor=found?C_GREEN:C_RED,Location=new Point(20,y),BackColor=Color.Transparent});
            var tb=new TextBox{Text=curVal,Size=new Size(420,22),Location=new Point(220,y-2),BackColor=Color.FromArgb(24,20,38),ForeColor=C_FG,BorderStyle=BorderStyle.FixedSingle,Font=new Font("Consolas",8f),PlaceholderText=defaultPath};
            scroll.Controls.Add(tb); pathTextBoxes[key]=tb;
            var captLabel=label; var captTb=tb;
            var btnBr=new Button{Text="...",Size=new Size(30,22),Location=new Point(646,y-2),FlatStyle=FlatStyle.Flat,BackColor=C_BTN_BG,ForeColor=C_FG,Font=new Font("Segoe UI",8f),Cursor=Cursors.Hand};
            btnBr.FlatAppearance.BorderSize=1;btnBr.FlatAppearance.BorderColor=C_BORDER;
            btnBr.Click+=(s,e)=>{using var ofd=new OpenFileDialog{Title=$"Locate {captLabel}",Filter="PS1|*.ps1|All|*.*"};if(ShowOwnedFileDialog(ofd)==DialogResult.OK)captTb.Text=ofd.FileName;};
            scroll.Controls.Add(btnBr);
            var btnX=new Button{Text="×",Size=new Size(22,22),Location=new Point(680,y-2),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(80,30,30),ForeColor=Color.FromArgb(255,150,150),Font=new Font("Segoe UI",8f),Cursor=Cursors.Hand};
            btnX.FlatAppearance.BorderSize=0; btnX.Click+=(s,e)=>captTb.Text="";
            scroll.Controls.Add(btnX);
            y+=28;
        }

        // ── SAVE button ──
        y+=10;
        var btnSave=new Button{Text="💾  Save All Paths",Size=new Size(180,36),Location=new Point(16,y),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(30,100,55),ForeColor=Color.White,Font=new Font("Segoe UI",10f,FontStyle.Bold),Cursor=Cursors.Hand};
        btnSave.FlatAppearance.BorderSize=1;btnSave.FlatAppearance.BorderColor=Color.FromArgb(50,160,80);btnSave.FlatAppearance.MouseOverBackColor=Color.FromArgb(40,130,70);
        btnSave.Click+=(s,e)=>{
            AppConfig.SaveBootstrap(tbBootstrapConfig.Text.Trim(), tbBootstrapScripts.Text.Trim());
            cfg.FakeTerminalPath    = pathTextBoxes["FakeTerminalPath"].Text.Trim();
            cfg.FakePowerShellPath  = pathTextBoxes["FakePowerShellPath"].Text.Trim();
            cfg.SeiwareLauncherPath = pathTextBoxes["SeiwareLauncherPath"].Text.Trim();
            cfg.CmdTerminalIcoPath  = pathTextBoxes["CmdTerminalIcoPath"].Text.Trim();
            cfg.PowerShellIcoPath   = pathTextBoxes["PowerShellIcoPath"].Text.Trim();
            cfg.WorkerScriptPath    = pathTextBoxes["WorkerScriptPath"].Text.Trim();
            cfg.MemoryScriptPath    = pathTextBoxes["MemoryScriptPath"].Text.Trim();
            cfg.LolScriptPath       = pathTextBoxes["LolScriptPath"].Text.Trim();
            cfg.HideFilesScriptPath = pathTextBoxes["HideFilesScriptPath"].Text.Trim();
            cfg.Save();
            CommandGuard.SetBannedNames(cfg.BannedNames);
            ShowOwnedMessage("Saved bootstrap overrides + config paths.\n\nBootstrap file: Seiware.bootstrap.json next to Seiware.exe\nRestart Seiware if you changed config/scripts or exe paths.","Seiware — Saved",MessageBoxButtons.OK,MessageBoxIcon.Information);
        };
        scroll.Controls.Add(btnSave);
        y+=46;

        scroll.Controls.Add(new Label{Text=$"Config: {AppConfig.ConfigPath}",AutoSize=true,Font=new Font("Consolas",7.5f),ForeColor=C_FG_DIM,Location=new Point(20,y),BackColor=Color.Transparent});
    }

    Button MakeConfigBtn(string text,int x,int y){var b=new Button{Text=text,AutoSize=true,MinimumSize=new Size(170,34),Location=new Point(x,y),FlatStyle=FlatStyle.Flat,BackColor=C_BTN_BG,ForeColor=C_FG,Cursor=Cursors.Hand,Font=new Font("Segoe UI",9f,FontStyle.Bold)};b.FlatAppearance.BorderSize=1;b.FlatAppearance.BorderColor=C_BORDER;b.FlatAppearance.MouseOverBackColor=C_BTN_HOV;return b;}
    void OpenSettings(){var dlg=new SettingsForm(new List<string>(cfg.BannedNames));if(ShowOwnedDialog(dlg)==DialogResult.OK){cfg.BannedNames=dlg.BannedNames;cfg.Save();RebuildRegex();CommandGuard.SetBannedNames(cfg.BannedNames);terminal?.SetCensorRegex(censorRegex);foreach(var(h,_)in headlessSessions)h.SetCensorRegex(censorRegex);UpdateStatus();}}
    void LaunchScript(string file,string scriptPath,Button btn){if(!StorageUtil.FileExists(scriptPath)){using var ofd=new OpenFileDialog{Title=$"Locate {file}",Filter="PowerShell Scripts (*.ps1)|*.ps1"};if(ShowOwnedFileDialog(ofd)!=DialogResult.OK)return;scriptPath=ofd.FileName;}bool interactive=file.Equals("HideFiles.ps1",StringComparison.OrdinalIgnoreCase);var psi=new ProcessStartInfo{FileName="powershell.exe",Arguments=$"-ExecutionPolicy Bypass -WindowStyle {(interactive?"Normal":"Hidden")} -File \"{scriptPath}\"",Verb="runas",UseShellExecute=true,WindowStyle=interactive?ProcessWindowStyle.Normal:ProcessWindowStyle.Hidden};try{var proc=Process.Start(psi);running.Add(new RunningScript(file,proc));RefreshRunningPanel();var orig=btn.BackColor;btn.BackColor=Color.FromArgb(60,160,90);btn.Text="✔  Launched";var t=new System.Windows.Forms.Timer{Interval=2000};t.Tick+=(s,e)=>{t.Stop();t.Dispose();if(!btn.IsDisposed){btn.BackColor=orig;btn.Text="▶  Launch";}};t.Start();}catch(Exception ex){string msg=ex.Message.Contains("1223")||ex.Message.ToLower().Contains("cancelled")?"[UAC prompt cancelled]":$"[Error] {ex.Message}";ShowOwnedMessage(msg,"Seiware",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
    void StopAllScripts(){foreach(var r in running)r.Stop();running.Clear();RefreshRunningPanel();}
    void RebuildRegex(){if(cfg.BannedNames==null||cfg.BannedNames.Count==0){censorRegex=new Regex("(?!)");return;}censorRegex=new Regex(@"(?<![a-zA-Z0-9_])("+string.Join("|",cfg.BannedNames.Select(Regex.Escape))+@")(?![a-zA-Z0-9_])",RegexOptions.IgnoreCase);}
    void UpdateStatus(){int s=running.Count(r=>r.IsAlive);bool i=shellInterceptor?.IsEnabled??false;statusLabel.Text=$"  ◈ {cfg.BannedNames.Count} censored  │  {s} script(s) active  │  Intercept: {(i?"ON ✓":"OFF")}  │  CMD+PS  │  Capture shield: ON";}
}

// ════════════════════════════════════════════════════════════════════════════
class SettingsForm : Form
{
    public List<string> BannedNames{get;private set;}ListBox listBox;TextBox nameInput;
    public SettingsForm(List<string> current){BannedNames=current;BuildUI();}
    void BuildUI(){Text="Manage Banned Names";Size=new Size(420,420);BackColor=Color.FromArgb(16,12,28);ForeColor=Color.FromArgb(215,210,235);Font=new Font("Segoe UI",9.5f);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterParent;Controls.Add(new Label{Text="Names/patterns to censor",Dock=DockStyle.Top,Height=36,Padding=new Padding(10,10,0,0),ForeColor=Color.FromArgb(170,140,255),Font=new Font("Segoe UI",9f,FontStyle.Bold)});listBox=new ListBox{Dock=DockStyle.Fill,BackColor=Color.FromArgb(12,10,22),ForeColor=Color.FromArgb(215,225,215),BorderStyle=BorderStyle.None,Font=new Font("Consolas",10f),SelectionMode=SelectionMode.MultiExtended};listBox.Items.AddRange(BannedNames.ToArray<object>());var ar=new Panel{Dock=DockStyle.Bottom,Height=38,BackColor=Color.FromArgb(22,16,40)};nameInput=new TextBox{PlaceholderText="Type a name...",BackColor=Color.FromArgb(28,20,48),ForeColor=Color.FromArgb(215,225,215),BorderStyle=BorderStyle.FixedSingle,Font=new Font("Consolas",9.5f),Location=new Point(8,7),Width=264};nameInput.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter)AddN();};var ab=AB("Add",new Point(280,7),Color.FromArgb(40,90,50));ab.Click+=(s,e)=>AddN();var rb=AB("Remove",new Point(348,7),Color.FromArgb(90,40,40));rb.Click+=(s,e)=>{foreach(var i in listBox.SelectedItems.Cast<string>().ToList())listBox.Items.Remove(i);};ar.Controls.AddRange(new Control[]{nameInput,ab,rb});var br=new Panel{Dock=DockStyle.Bottom,Height=44,BackColor=Color.FromArgb(16,12,30)};var ok=AB("Save",new Point(230,8),Color.FromArgb(40,80,130));ok.Click+=(s,e)=>{BannedNames=listBox.Items.Cast<string>().ToList();DialogResult=DialogResult.OK;Close();};var cc=AB("Cancel",new Point(308,8),Color.FromArgb(60,60,70));cc.Click+=(s,e)=>{DialogResult=DialogResult.Cancel;Close();};br.Controls.AddRange(new Control[]{ok,cc});Controls.Add(listBox);Controls.Add(ar);Controls.Add(br);}
    Button AB(string t,Point p,Color bg){var b=new Button{Text=t,FlatStyle=FlatStyle.Flat,BackColor=bg,ForeColor=Color.FromArgb(230,220,255),Size=new Size(70,26),Location=p,Cursor=Cursors.Hand,Font=new Font("Segoe UI",9f,FontStyle.Bold)};b.FlatAppearance.BorderSize=1;b.FlatAppearance.BorderColor=Color.FromArgb(90,75,160);return b;}
    void AddN(){string n=nameInput.Text.Trim();if(n.Length==0||listBox.Items.Contains(n))return;listBox.Items.Add(n);nameInput.Clear();}
}
