// DreamLandLauncher.exe — Launches fake terminals with correct title from the start.
// Uses CreateProcess with STARTUPINFO.lpTitle so the console window NEVER
// shows the exe path — it starts with the correct title immediately.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

class Program
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO { public int cb; public string lpReserved; public string lpDesktop; public string lpTitle; public int dwX, dwY, dwXSize, dwYSize; public int dwXCountChars, dwYCountChars; public int dwFillAttribute, dwFlags; public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcess(string lpApplicationName, StringBuilder lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    const uint CREATE_NEW_CONSOLE = 0x00000010;
    const int STARTF_USETITLE = 0x00001000;

    static bool LaunchWithTitle(string exePath, string arguments, string workDir, string title)
    {
        var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpTitle = title, dwFlags = STARTF_USETITLE };
        var cmdLine = new StringBuilder(); cmdLine.Append('"').Append(exePath).Append('"');
        if (!string.IsNullOrEmpty(arguments)) cmdLine.Append(' ').Append(arguments);
        bool ok = CreateProcess(null, cmdLine, IntPtr.Zero, IntPtr.Zero, false, CREATE_NEW_CONSOLE, IntPtr.Zero, workDir, ref si, out PROCESS_INFORMATION pi);
        if (ok) { CloseHandle(pi.hProcess); CloseHandle(pi.hThread); }
        return ok;
    }

    static string DetectTitle(string exePath, string arguments)
    {
        string name = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
        bool isPS = name.Contains("powershell") || name.Contains("pwsh");
        bool isAdmin = arguments.IndexOf("--admin", StringComparison.OrdinalIgnoreCase) >= 0;
        if (isPS) return isAdmin ? "Administrator: Windows PowerShell" : "Windows PowerShell";
        return isAdmin ? "Administrator: Command Prompt" : "Command Prompt";
    }

    static int Main(string[] args)
    {
        string target = null; string targetArgs = ""; string title = "";
        try
        {
            string myExe = typeof(Program).Assembly.Location;
            if (string.IsNullOrEmpty(myExe)) myExe = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(myExe)) myExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            string myDir = Path.GetDirectoryName(myExe) ?? Directory.GetCurrentDirectory();

            string targetFile = myExe + ".target";
            if (File.Exists(targetFile)) { var lines = File.ReadAllLines(targetFile); if (lines.Length > 0) target = lines[0].Trim(); if (lines.Length > 1) targetArgs = lines[1].Trim(); if (lines.Length > 2) title = lines[2].Trim(); }

            if (string.IsNullOrEmpty(target) || !File.Exists(target)) { if (args.Length > 0 && File.Exists(args[0])) { target = args[0]; if (args.Length > 1) targetArgs = string.Join(" ", args, 1, args.Length - 1); } }
            if (string.IsNullOrEmpty(target) || !File.Exists(target)) { if (args.Length > 0) { string candidate = Path.Combine(myDir, args[0]); if (File.Exists(candidate)) { target = candidate; if (args.Length > 1) targetArgs = string.Join(" ", args, 1, args.Length - 1); } } }
            // Try all known terminal names
            if (string.IsNullOrEmpty(target) || !File.Exists(target)) target = Path.Combine(myDir, "Terminal.exe");
            if (string.IsNullOrEmpty(target) || !File.Exists(target)) target = Path.Combine(myDir, "Command Prompt.exe");
            if (string.IsNullOrEmpty(target) || !File.Exists(target)) target = Path.Combine(myDir, "Windows PowerShell.exe");
            if (string.IsNullOrEmpty(target) || !File.Exists(target)) return 1;

            if (string.IsNullOrEmpty(title)) title = DetectTitle(target, targetArgs);
            string workDir = Path.GetDirectoryName(target) ?? myDir;

            if (LaunchWithTitle(target, targetArgs, workDir, title)) return 0;
            Process.Start(new ProcessStartInfo { FileName = target, Arguments = targetArgs, WorkingDirectory = workDir, UseShellExecute = true });
            return 0;
        }
        catch { return 1; }
    }
}
