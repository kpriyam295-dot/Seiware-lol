// Seiware.cs — Integrated DreamLand + FakeTerminal controller
// Based on original working Seiware.cs with additions:
// - mmc.exe selective interception (eventvwr.msc only, allows Device Manager etc.)
// - Start Menu shortcut creation on startup
// - CustomScript class for user-defined PS1 scripts
// - Component paths panel with copy buttons
using System; using System.Collections.Generic; using System.Diagnostics; using System.Drawing; using System.IO; using System.IO.Pipes; using System.Linq; using System.Reflection; using System.Runtime.InteropServices; using System.Text; using System.Text.Json; using System.Text.Json.Serialization; using System.Text.RegularExpressions; using System.Threading; using System.Threading.Tasks; using System.Windows.Forms; using Microsoft.Win32; using System.Management;

enum ShellType { Cmd, PowerShell }

// ═══ NEW: Custom script definition ═══
class CustomScript { public string Name{get;set;}=""; public string Description{get;set;}=""; public string ScriptPath{get;set;}=""; [JsonIgnore] public bool Exists=>!string.IsNullOrEmpty(ScriptPath)&&File.Exists(ScriptPath); }

// ═══ NEW: Shortcut creator ═══
static class ShortcutManager
{
    public static void EnsureStartMenuShortcuts(AppConfig cfg)
    {
        try
        {
            string userName = !string.IsNullOrEmpty(cfg.UserName) ? cfg.UserName : Environment.UserName;
            // CMD shortcut — named "Command Prompt.exe.lnk" so it NEVER touches the original
            string cmdDir = $@"C:\Users\{userName}\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\System Tools";
            string cmdLnk = Path.Combine(cmdDir, "Command Prompt.exe.lnk");
            string cmdExe = AppConfig.ResolvePath(cfg.FakeTerminalPath, "Terminal.exe", "Command Prompt.exe");
            string cmdIco = AppConfig.ResolvePath(cfg.CmdTerminalIcoPath, "terminal.ico", "cmdterminal.ico");
            if (!File.Exists(cmdLnk) && File.Exists(cmdExe))
                MakeShortcut(cmdLnk, cmdExe, cmdIco, "Opens a command window");

            // PowerShell shortcut — named "Windows PowerShell.exe.lnk"
            string psDir = $@"C:\Users\{userName}\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Windows PowerShell";
            string psLnk = Path.Combine(psDir, "Windows PowerShell.exe.lnk");
            string psExe = AppConfig.ResolvePath(cfg.FakePowerShellPath, "Windows PowerShell.exe");
            string psIco = AppConfig.ResolvePath(cfg.PowerShellIcoPath, "powershell.ico");
            if (!File.Exists(psLnk) && File.Exists(psExe))
                MakeShortcut(psLnk, psExe, psIco, "Windows PowerShell", "--shell ps");
        }
        catch { } // Never crash on shortcut creation failure
    }
    static void MakeShortcut(string lnk,string target,string ico,string desc="",string args="")
    { try { string d=Path.GetDirectoryName(lnk)??""; if(d.Length>0&&!Directory.Exists(d))Directory.CreateDirectory(d);
        Type t=Type.GetTypeFromProgID("WScript.Shell"); if(t==null)return; dynamic sh=Activator.CreateInstance(t); dynamic sc=sh.CreateShortcut(lnk);
        sc.TargetPath=target;sc.Arguments=args;sc.Description=desc;sc.WorkingDirectory=Path.GetDirectoryName(target)??"";
        if(!string.IsNullOrEmpty(ico)&&File.Exists(ico))sc.IconLocation=ico+",0"; sc.Save(); } catch{} }
}

// ════════════════════════════════════════════════════════════════════════════
// SHELL INTERCEPTOR — ORIGINAL + mmc.exe selective interception
// ════════════════════════════════════════════════════════════════════════════
class ShellInterceptor : IDisposable
{
    private const int KillDelayMs=120; private ManagementEventWatcher _watcher; private readonly int _ownPid; private bool _disposed,_enabled;
    private readonly HashSet<int> _exemptPids=new(); private readonly object _exemptLock=new(); private volatile int _suppressUntil=0;
    public Action<ShellType> OnInterceptNormal{get;set;} public Action<ShellType> OnInterceptAdmin{get;set;} public Action OnControlPanelBlocked{get;set;}
    public bool IsEnabled=>_enabled;
    public void SuppressFor(int ms){_suppressUntil=Environment.TickCount+ms;}
    public ShellInterceptor(){_ownPid=Process.GetCurrentProcess().Id;}
    public void RegisterExemptPid(int pid){lock(_exemptLock){_exemptPids.Add(pid);_exemptPids.RemoveWhere(p=>{try{Process.GetProcessById(p);return false;}catch{return true;}});}}

    public void Start()
    { if(_enabled||_disposed)return; try {
        // CHANGED: Added mmc.exe to watch list for selective snap-in blocking
        var query=new WqlEventQuery("__InstanceCreationEvent",TimeSpan.FromSeconds(0.1),
            "TargetInstance ISA 'Win32_Process' AND ("+
            "TargetInstance.Name = 'cmd.exe' OR "+
            "TargetInstance.Name = 'powershell.exe' OR "+
            "TargetInstance.Name = 'pwsh.exe' OR "+
            "TargetInstance.Name = 'eventvwr.exe' OR "+
            "TargetInstance.Name = 'perfmon.exe' OR "+
            "TargetInstance.Name = 'wercon.exe' OR "+
            "TargetInstance.Name = 'mmc.exe')");
        _watcher=new ManagementEventWatcher(query);_watcher.EventArrived+=OnShellCreated;_watcher.Start();_enabled=true;
    } catch{_enabled=false;} }

    public void Stop(){if(!_enabled)return;_enabled=false;try{_watcher?.Stop();}catch{}}

    // ═══ NEW: Get command line for mmc.exe filtering ═══
    static string GetProcessCommandLine(int pid)
    { try{using var s=new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
        foreach(ManagementObject obj in s.Get())return obj["CommandLine"]?.ToString()??""; }catch{} return""; }

    // ═══ NEW: Only block log/crash viewer MMC snap-ins ═══
    // BLOCKED: eventvwr.msc, perfmon.msc /rel, reliability
    // ALLOWED: devmgmt.msc, diskmgmt.msc, compmgmt.msc, services.msc, everything else
    static bool IsBlockedMmcSnapIn(string cmdLine)
    { if(string.IsNullOrWhiteSpace(cmdLine))return false; string low=cmdLine.ToLowerInvariant();
      if(low.Contains("eventvwr.msc"))return true;
      if(low.Contains("perfmon.msc")&&low.Contains("/rel"))return true;
      if(low.Contains("reliability"))return true;
      if(low.Contains("wercon"))return true; return false; }

    private void OnShellCreated(object sender,EventArrivedEventArgs e)
    { try {
        if(Environment.TickCount<_suppressUntil)return;
        var inst=(ManagementBaseObject)e.NewEvent["TargetInstance"];
        string procName=inst["Name"]?.ToString()?.ToLowerInvariant()??"";
        int newPid=Convert.ToInt32(inst["ProcessId"]);
        int parentPid=Convert.ToInt32(inst["ParentProcessId"]);

        // ═══ NEW: mmc.exe — only block log/crash viewer snap-ins ═══
        if(procName=="mmc.exe")
        { Thread.Sleep(80); string cmdLine=GetProcessCommandLine(newPid);
          if(IsBlockedMmcSnapIn(cmdLine)){try{Process.GetProcessById(newPid).Kill();}catch{}OnControlPanelBlocked?.Invoke();}
          return; /* Allow all other MMC snap-ins */ }

        // Block direct log viewer executables (ORIGINAL + perfmon fix)
        if(procName=="eventvwr.exe"||procName=="wercon.exe")
        { try{Process.GetProcessById(newPid).Kill();}catch{}OnControlPanelBlocked?.Invoke();return; }

        // CHANGED: perfmon.exe — only block when /rel (Reliability Monitor), allow regular perfmon
        if(procName=="perfmon.exe")
        { Thread.Sleep(50); string cmdLine=GetProcessCommandLine(newPid).ToLowerInvariant();
          if(cmdLine.Contains("/rel")||cmdLine.Contains("reliability"))
          { try{Process.GetProcessById(newPid).Kill();}catch{}OnControlPanelBlocked?.Invoke(); }
          return; }

        // ═══ ORIGINAL shell interception logic ═══
        if(IsDescendantOfSelf(newPid))return;
        ShellType shellType=procName switch{"powershell.exe"=>ShellType.PowerShell,"pwsh.exe"=>ShellType.PowerShell,_=>ShellType.Cmd};
        // Retry elevation check a few times because the token may not be queryable immediately
        bool isAdminLaunch=false;
        for(int i=0;i<8&&!isAdminLaunch;i++)
        {
            Thread.Sleep(40);
            isAdminLaunch=IsProcessElevated(newPid);
        }
        if(!isAdminLaunch){try{var parent=Process.GetProcessById(parentPid);string parentName=parent.ProcessName.ToLowerInvariant();if(parentName!="explorer"&&parentName!="cmd"&&parentName!="powershell"&&parentName!="pwsh"&&parentName!="windowsterminal")isAdminLaunch=true;}catch{}}
        // Kill the real shell
        try{var proc=Process.GetProcessById(newPid);if(!proc.HasExited){int waited=0;while(proc.MainWindowHandle==IntPtr.Zero&&waited<KillDelayMs){Thread.Sleep(10);waited+=10;proc.Refresh();}proc.Kill();}}catch{}
        if(isAdminLaunch)OnInterceptAdmin?.Invoke(shellType);else OnInterceptNormal?.Invoke(shellType);
    } catch{} }

    bool IsDescendantOfSelf(int pid){Dictionary<int,int>parentMap;try{parentMap=new();using var searcher=new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId FROM Win32_Process");foreach(ManagementObject obj in searcher.Get())parentMap[Convert.ToInt32(obj["ProcessId"])]=Convert.ToInt32(obj["ParentProcessId"]);}catch{return false;}HashSet<int>exempt;lock(_exemptLock){exempt=new HashSet<int>(_exemptPids);}exempt.Add(_ownPid);int cur=pid;int depth=0;while(cur>0&&depth++<20){if(exempt.Contains(cur))return true;if(!parentMap.TryGetValue(cur,out int p)||p==cur)break;cur=p;}return false;}
    // FIXED IsProcessElevated — TokenElevation (class 20) returns 0 or non-zero
    // The old code checked >= 0x3000 which NEVER matched (0 or 1 is never >= 12288)
    static bool IsProcessElevated(int pid)
    {
        IntPtr hProcess=IntPtr.Zero,hToken=IntPtr.Zero;
        try
        {
            hProcess=OpenProcess(0x0400,false,pid);
            if(hProcess==IntPtr.Zero)return false;
            if(!OpenProcessToken(hProcess,0x0008,out hToken))return false;
            // TokenElevation (class 20): TOKEN_ELEVATION.TokenIsElevated = 0 (no) or non-zero (yes)
            int len=4;IntPtr buf=Marshal.AllocHGlobal(len);
            try{if(!GetTokenInformation(hToken,20,buf,len,out _))return false;return Marshal.ReadInt32(buf)!=0;}
            finally{Marshal.FreeHGlobal(buf);}
        }
        catch{return false;}
        finally{if(hToken!=IntPtr.Zero)CloseHandle(hToken);if(hProcess!=IntPtr.Zero)CloseHandle(hProcess);}
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(IntPtr h,uint access,out IntPtr token);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool GetTokenInformation(IntPtr token,int cls,IntPtr info,int len,out int retLen);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr hWnd,int nCmdShow);
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr hWnd);
    static void ShowProcessWindow(Process p,bool show){if(p==null)return;try{if(p.HasExited)return;for(int i=0;i<50&&p.MainWindowHandle==IntPtr.Zero;i++){Thread.Sleep(50);p.Refresh();}if(p.MainWindowHandle!=IntPtr.Zero){if(show){ShowWindow(p.MainWindowHandle,9);SetForegroundWindow(p.MainWindowHandle);}else ShowWindow(p.MainWindowHandle,0);}}catch{}}
    public void Dispose(){if(_disposed)return;_disposed=true;Stop();try{_watcher?.Dispose();}catch{}_watcher=null;}
}

// ════════════════════════════════════════════════════════════════════════════
// ENTRY POINT — ORIGINAL + shortcut creation
// ════════════════════════════════════════════════════════════════════════════
static class EntryPoint
{
    static Mutex singleInstanceMutex;
    [STAThread] static void Main(string[] args)
    {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        singleInstanceMutex=new Mutex(true,"SeiwareSingleInstance",out bool createdNew);
        if(!createdNew){MessageBox.Show("DreamLand is already running.","DreamLand",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
        bool silent=args.Any(a=>a.Equals("--silent",StringComparison.OrdinalIgnoreCase));
        PsEmbedder.WriteAll();
        if(!StorageUtil.FileExists(AppConfig.ConfigPath)){var wizard=new SetupWizard();if(wizard.ShowDialog()!=DialogResult.OK){singleInstanceMutex.ReleaseMutex();return;}}
        // ═══ NEW: Create Start Menu shortcuts on every launch ═══
        var cfg=AppConfig.Load(); ShortcutManager.EnsureStartMenuShortcuts(cfg);
        Application.Run(new MainForm(silent)); singleInstanceMutex.ReleaseMutex();
    }
}

// ════════════════════════════════════════════════════════════════════════════
static class NativeWin32
{
    [DllImport("kernel32.dll")]public static extern bool AttachConsole(uint dwProcessId);
    [DllImport("kernel32.dll")]public static extern bool FreeConsole();
    [DllImport("kernel32.dll")]public static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent,uint dwProcessGroupId);
    [DllImport("kernel32.dll")]public static extern bool SetConsoleCtrlHandler(IntPtr HandlerRoutine,bool Add);
    [DllImport("user32.dll")]public static extern bool SetWindowDisplayAffinity(IntPtr hWnd,uint dwAffinity);
    public const uint WDA_NONE=0x00000000,WDA_EXCLUDEFROMCAPTURE=0x00000011;
}

// ════════════════════════════════════════════════════════════════════════════
// STORAGE UTIL — ORIGINAL, UNCHANGED
// ════════════════════════════════════════════════════════════════════════════
static class StorageUtil
{
    static FileAttributes? ClearHiddenSystem(string path){try{var attrs=File.GetAttributes(path);var cleared=attrs&~FileAttributes.Hidden&~FileAttributes.System;if(cleared!=attrs)File.SetAttributes(path,cleared);return attrs;}catch{return null;}}
    static void Restore(string path,FileAttributes? attrs){try{if(attrs.HasValue)File.SetAttributes(path,attrs.Value);}catch{}}
    public static bool FileExists(string path){try{return File.Exists(path);}catch{return false;}}
    public static void EnsureDirectory(string dir){if(string.IsNullOrWhiteSpace(dir))return;if(!Directory.Exists(dir))Directory.CreateDirectory(dir);}
    public static string ReadAllText(string path){FileAttributes? oldFile=null;try{if(!File.Exists(path))return"";oldFile=ClearHiddenSystem(path);return File.ReadAllText(path);}finally{Restore(path,oldFile);}}
    public static void WriteAllText(string path,string content,Encoding encoding=null){string dir=Path.GetDirectoryName(path);FileAttributes? oldDir=null;FileAttributes? oldFile=null;try{if(!string.IsNullOrEmpty(dir)){if(Directory.Exists(dir))oldDir=ClearHiddenSystem(dir);EnsureDirectory(dir);}if(File.Exists(path))oldFile=ClearHiddenSystem(path);if(encoding==null)File.WriteAllText(path,content);else File.WriteAllText(path,content,encoding);}finally{Restore(path,oldFile);if(!string.IsNullOrEmpty(dir))Restore(dir,oldDir);}}
    public static string TryGetRedirectTarget(string command,string currentDir){try{if(string.IsNullOrWhiteSpace(command))return null;bool inDouble=false,inSingle=false;int idx=-1;for(int i=0;i<command.Length;i++){char c=command[i];if(c=='"')inDouble=!inDouble;else if(c=='\'')inSingle=!inSingle;else if(c=='>'&&!inDouble&&!inSingle){idx=i;break;}}if(idx<0)return null;int start=idx+1;if(start<command.Length&&command[start]=='>')start++;string tail=command.Substring(start).Trim();if(string.IsNullOrWhiteSpace(tail))return null;if(tail.StartsWith("&",StringComparison.Ordinal))return null;if(tail.StartsWith("1>&",StringComparison.OrdinalIgnoreCase)||tail.StartsWith("2>&",StringComparison.OrdinalIgnoreCase))return null;string target;if(tail[0]=='"'){int end=tail.IndexOf('"',1);target=end>1?tail.Substring(1,end-1):tail.Trim('"');}else{int end=tail.IndexOfAny(new[]{' ','\t','&','|'});target=end>=0?tail.Substring(0,end):tail;}if(string.IsNullOrWhiteSpace(target))return null;if(target.Equals("nul",StringComparison.OrdinalIgnoreCase))return null;if(!Path.IsPathRooted(target))target=Path.Combine(currentDir,target);return Path.GetFullPath(target);}catch{return null;}}
    public static string TryGetOpenedFileTarget(string command,string currentDir){try{if(string.IsNullOrWhiteSpace(command))return null;string low=command.Trim().ToLowerInvariant();if(!low.StartsWith("notepad")&&!low.StartsWith("type")&&!low.StartsWith("more"))return null;var parts=command.Trim().Split(' ',2,StringSplitOptions.RemoveEmptyEntries);if(parts.Length<2)return null;string tail=parts[1].Trim();string target;if(tail[0]=='"'){int end=tail.IndexOf('"',1);target=end>1?tail.Substring(1,end-1):tail.Trim('"');}else{int end=tail.IndexOfAny(new[]{' ','\t','&','|'});target=end>=0?tail.Substring(0,end):tail;}if(string.IsNullOrWhiteSpace(target))return null;if(!Path.IsPathRooted(target))target=Path.Combine(currentDir,target);return Path.GetFullPath(target);}catch{return null;}}
    public static void SanitizeFile(string path,Regex censor){try{if(string.IsNullOrWhiteSpace(path)||censor==null)return;if(!FileExists(path))return;string original=ReadAllText(path);if(string.IsNullOrEmpty(original))return;string cleaned=censor.Replace(original,"");if(!string.Equals(original,cleaned,StringComparison.Ordinal))WriteAllText(path,cleaned,Encoding.UTF8);}catch{}}
    public static void SanitizeFileEventually(string path,Regex censor,int attempts=10,int delayMs=120){try{if(string.IsNullOrWhiteSpace(path)||censor==null)return;for(int i=0;i<attempts;i++){Thread.Sleep(delayMs);try{SanitizeFile(path,censor);return;}catch{}}}catch{}}
}

// ════════════════════════════════════════════════════════════════════════════
class BootstrapOverride{public string ConfigPath{get;set;}="";public string ScriptsDir{get;set;}="";}

// ════════════════════════════════════════════════════════════════════════════
// COMMAND GUARD — ORIGINAL, UNCHANGED
// ════════════════════════════════════════════════════════════════════════════
static class CommandGuard
{
    static readonly object _lock=new();static List<string>_bannedNames=new();static List<(string,Regex)>_rules=new();
    public static event Action<string> Blocked;
    static Regex BuildPattern(string banned){string esc=Regex.Escape(banned);return new Regex(@"(?i)(^|[\s\""'`=,:;()\[\]{} |/&\\])"+esc+@"($|[\s\""'`=,:;()\[\]{} |/&\\.])",RegexOptions.Compiled);}
    public static void SetBannedNames(IEnumerable<string> names){lock(_lock){_bannedNames=(names??Array.Empty<string>()).Where(s=>!string.IsNullOrWhiteSpace(s)).Select(s=>s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();_rules=_bannedNames.Select(n=>(n,BuildPattern(n))).ToList();}}
    public static string GetMatchedBanned(string command){if(string.IsNullOrWhiteSpace(command))return null;lock(_lock){foreach(var(name,pattern)in _rules)if(pattern.IsMatch(command))return name;}return null;}
    public static bool ShouldBlock(string command){if(string.IsNullOrWhiteSpace(command))return false;string low=command.Trim().ToLowerInvariant();if(IsEventLogQuery(low)){Blocked?.Invoke("event-log-query");return true;}string hit=GetMatchedBanned(command.Trim());if(string.IsNullOrWhiteSpace(hit))return false;Blocked?.Invoke(hit);return true;}
    static bool IsEventLogQuery(string low){if(low.Contains("wevtutil")&&(low.Contains("application")||low.Contains("system")||low.Contains("security")))return true;if(low.Contains("get-winevent")||low.Contains("get-eventlog"))return true;if(low.Contains("eventvwr")||low.Contains("eventvwr.msc"))return true;if(low.Contains("perfmon")&&low.Contains("/rel"))return true;if(low.Contains("reliability")&&(low.Contains("monitor")||low.Contains("history")))return true;if(low.Contains("wercon")||low.Contains("problem reports"))return true;return false;}
}

// ════════════════════════════════════════════════════════════════════════════
// FAILURE EMULATOR — ORIGINAL, UNCHANGED
// ════════════════════════════════════════════════════════════════════════════
static class FailureEmulator
{
    public static string BuildOutput(ShellType shellType,string command){if(string.IsNullOrWhiteSpace(command))return null;string s=command.Trim();string low=s.ToLowerInvariant();string first=s.Split(' ',2,StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();if(first.StartsWith("\"")){int end=first.IndexOf('"',1);first=end>1?first.Substring(1,end-1):first.Trim('"');}if(shellType==ShellType.PowerShell)return BuildPowerShell(first,low,s);return BuildCmd(first,low,s);}
    static string FirstArg(string cmd){var p=cmd.Trim().Split(' ',2,StringSplitOptions.RemoveEmptyEntries);return p.Length>1?p[1].Trim():"";}
    static string BuildCmd(string first,string low,string original){switch(first){case"where":return"INFO: Could not find files for the given pattern(s).";case"findstr":case"find":return"";case"dir":return" File Not Found";case"sc":return"[SC] EnumQueryServicesStatus:OpenService FAILED 1060:\n\nThe specified service does not exist as an installed service.\n";case"net":return low.Contains("start")||low.Contains("stop")?"System error 2 has occurred.\n\nThe system cannot find the file specified.\n":"The syntax of this command is:";case"wmic":return"No Instance(s) Available.";case"tasklist":return"INFO: No tasks are running which match the specified criteria.";case"taskkill":return"ERROR: The process \"\" not found.";case"reg":return"ERROR: The system was unable to find the specified registry key or value.";case"type":return"The system cannot find the file specified.";case"del":case"erase":return"Could Not Find "+(FirstArg(original).Length==0?" ":FirstArg(original));case"copy":case"move":case"xcopy":case"robocopy":return"The system cannot find the file specified.";case"ren":case"rename":return"The system cannot find the file specified.";case"attrib":return"File Not Found - "+(FirstArg(original).Length==0?" ":FirstArg(original));case"notepad":case"start":case"explorer":case"for":case"forfiles":return"The system cannot find the file specified.";case"powershell":case"pwsh":case"cmd":return"The system cannot find the file specified.";case"fc":return"FC: Cannot open - The system cannot find the file specified.";case"wevtutil":return"No events were found that match the specified selection criteria.";case"eventvwr":return"The system cannot find the file specified.";default:return null;}}
    static string BuildPowerShell(string first,string low,string original){switch(first){case"gci":case"get-childitem":case"ls":return null;case"dir":case"where":return null;case"select-string":case"sls":return"Select-String : Cannot find path because it does not exist.";case"get-content":case"gc":case"cat":return"Get-Content : Cannot find path because it does not exist.";case"get-item":case"gi":return"Get-Item : Cannot find path because it does not exist.";case"get-process":case"gps":return"Get-Process : Cannot find a process with the process ID.";case"test-path":return"False";case"remove-item":case"ri":case"del":case"rm":case"rmdir":case"rdi":return"Remove-Item : Cannot find path because it does not exist.";case"copy-item":case"ci":case"cp":case"copy":case"move-item":case"mi":case"mv":case"move":case"rename-item":case"rni":case"ren":return"Cannot find path because it does not exist.";case"invoke-item":case"ii":return"Invoke-Item : Cannot find path because it does not exist.";case"start-process":case"saps":case"start":return"Start-Process : This command cannot be run due to the error: The system cannot find the file specified.";case"where.exe":return"where.exe : Cannot find path because it does not exist.";case"get-winevent":return"No events were found that match the specified selection criteria.";case"get-eventlog":return"Get-EventLog : No matches found";default:return null;}}
}

// ════════════════════════════════════════════════════════════════════════════
// APP CONFIG — ORIGINAL + CustomScripts + GetAllPaths
// ════════════════════════════════════════════════════════════════════════════
class AppConfig
{
    public static readonly string DefaultConfigDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Seiware");
    public static readonly string BootstrapPath=Path.Combine(AppContext.BaseDirectory,"Seiware.bootstrap.json");
    static BootstrapOverride _bootstrap=LoadBootstrap();
    static BootstrapOverride LoadBootstrap(){try{if(!StorageUtil.FileExists(BootstrapPath))return new BootstrapOverride();string json=StorageUtil.ReadAllText(BootstrapPath);return string.IsNullOrWhiteSpace(json)?new BootstrapOverride():(JsonSerializer.Deserialize<BootstrapOverride>(json)??new BootstrapOverride());}catch{return new BootstrapOverride();}}
    static string NormalizePath(string p){if(string.IsNullOrWhiteSpace(p))return"";try{return Path.GetFullPath(p.Trim());}catch{return p.Trim();}}
    public static void SaveBootstrap(string configPath,string scriptsDir){_bootstrap=new BootstrapOverride{ConfigPath=NormalizePath(configPath),ScriptsDir=NormalizePath(scriptsDir)};StorageUtil.WriteAllText(BootstrapPath,JsonSerializer.Serialize(_bootstrap,new JsonSerializerOptions{WriteIndented=true}));}
    public static string ConfigOverridePath=>_bootstrap.ConfigPath; public static string ScriptsOverrideDir=>_bootstrap.ScriptsDir;
    public static string ConfigDir=>!string.IsNullOrWhiteSpace(_bootstrap.ConfigPath)?(Path.GetDirectoryName(_bootstrap.ConfigPath)??DefaultConfigDir):DefaultConfigDir;
    public static string ConfigPath=>!string.IsNullOrWhiteSpace(_bootstrap.ConfigPath)?_bootstrap.ConfigPath:Path.Combine(DefaultConfigDir,"config.json");
    public static string ScriptsDir=>!string.IsNullOrWhiteSpace(_bootstrap.ScriptsDir)?_bootstrap.ScriptsDir:Path.Combine(ConfigDir,"scripts");
    // ORIGINAL properties
    public string UserName{get;set;}=Environment.UserName; public string AppPath{get;set;}=""; public string NewUiPath{get;set;}=""; public string ScreenshotPath{get;set;}=""; public string FindImagePath{get;set;}="";
    public string CmdTerminalImagePath{get;set;}="cmdterminal.png"; public bool StartWithWindows{get;set;}=false; public bool InterceptPowerShell{get;set;}=true;
    public List<string> HiddenTargets{get;set;}=new(); public List<string> BannedNames{get;set;}=new(){"app.exe","newui","newui.exe","updater.exe","loader.exe","TGMacro"};
    public string FakeTerminalPath{get;set;}=""; public string FakePowerShellPath{get;set;}=""; public string SeiwareLauncherPath{get;set;}="";
    public string CmdTerminalIcoPath{get;set;}=""; public string PowerShellIcoPath{get;set;}="";
    public string WorkerScriptPath{get;set;}=""; public string MemoryScriptPath{get;set;}=""; public string LolScriptPath{get;set;}=""; public string HideFilesScriptPath{get;set;}="";
    // ═══ NEW: Custom scripts list ═══
    public List<CustomScript> CustomScripts{get;set;}=new();
    // ORIGINAL computed paths
    public string WorkerScript=>!string.IsNullOrEmpty(WorkerScriptPath)?WorkerScriptPath:Path.Combine(ScriptsDir,"worker.ps1");
    public string MemoryScript=>!string.IsNullOrEmpty(MemoryScriptPath)?MemoryScriptPath:Path.Combine(ScriptsDir,"Memory_Optimizer.ps1");
    public string LolScript=>!string.IsNullOrEmpty(LolScriptPath)?LolScriptPath:Path.Combine(ScriptsDir,"LOL.ps1");
    public string HideFilesScript=>!string.IsNullOrEmpty(HideFilesScriptPath)?HideFilesScriptPath:Path.Combine(ScriptsDir,"HideFiles.ps1");
    // ORIGINAL methods
    public static AppConfig Load(){try{var json=StorageUtil.ReadAllText(ConfigPath);return string.IsNullOrWhiteSpace(json)?new AppConfig():(JsonSerializer.Deserialize<AppConfig>(json)??new AppConfig());}catch{return new AppConfig();}}
    public void Save(){StorageUtil.EnsureDirectory(ConfigDir);StorageUtil.WriteAllText(ConfigPath,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));}
    public static string AutoDetectPath(string fileName){foreach(var root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),Environment.GetFolderPath(Environment.SpecialFolder.Desktop),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Documents")}){try{var f=Directory.GetFiles(root,fileName,SearchOption.AllDirectories).FirstOrDefault();if(f!=null)return f;}catch{}}return"";}
    public void ApplyStartWithWindows(){using var key=Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",true);if(StartWithWindows)key?.SetValue("Seiware",$"\"{Path.Combine(AppContext.BaseDirectory,"DreamLand.exe")}\" --silent");else key?.DeleteValue("Seiware",false);}
    /// Resolves the best path for a component, checking config override then fallback names next to exe.
    public static string ResolvePath(string configValue, params string[] fallbackNames)
    {
        if (!string.IsNullOrEmpty(configValue) && File.Exists(configValue)) return configValue;
        string b = AppContext.BaseDirectory;
        foreach (var name in fallbackNames)
        {
            string p = Path.Combine(b, name);
            if (File.Exists(p)) return p;
        }
        return fallbackNames.Length > 0 ? Path.Combine(b, fallbackNames[0]) : "";
    }

    /// Get all component paths for display — always resolves fresh
    public Dictionary<string, string> GetAllPaths()
    {
        string b = AppContext.BaseDirectory;
        string user = !string.IsNullOrEmpty(UserName) ? UserName : Environment.UserName;
        return new Dictionary<string, string>
        {
            ["DreamLand.exe"] = ResolvePath("", "DreamLand.exe", "Seiware.exe"),
            ["Terminal.exe"] = ResolvePath(FakeTerminalPath, "Terminal.exe", "Command Prompt.exe"),
            ["Windows PowerShell.exe"] = ResolvePath(FakePowerShellPath, "Windows PowerShell.exe"),
            ["Launcher.exe"] = ResolvePath(SeiwareLauncherPath, "DreamLandLauncher.exe", "SeiwareLauncher.exe"),
            ["terminal.ico"] = ResolvePath(CmdTerminalIcoPath, "terminal.ico", "cmdterminal.ico"),
            ["powershell.ico"] = ResolvePath(PowerShellIcoPath, "powershell.ico"),
            ["config.json"] = ConfigPath,
            ["Scripts Directory"] = ScriptsDir,
            ["worker.ps1"] = WorkerScript,
            ["Memory_Optimizer.ps1"] = MemoryScript,
            ["LOL.ps1"] = LolScript,
            ["HideFiles.ps1"] = HideFilesScript,
            ["CMD Shortcut"] = $@"C:\Users\{user}\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\System Tools\Command Prompt.exe.lnk",
            ["PS Shortcut"] = $@"C:\Users\{user}\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Windows PowerShell\Windows PowerShell.exe.lnk",
        };
    }
}

// ════════════════════════════════════════════════════════════════════════════
// PS EMBEDDER — ORIGINAL
// ════════════════════════════════════════════════════════════════════════════
static class PsEmbedder
{
    static readonly string D=AppConfig.ScriptsDir;
    public static void WriteAll(){StorageUtil.EnsureDirectory(D);W("worker.ps1",WorkerPs1);W("Memory_Optimizer.ps1",MemoryPs1);W("HideFiles.ps1",HideFilesPs1);}
    public static void WriteLol(){StorageUtil.EnsureDirectory(D);W("LOL.ps1",LolPs1);}
    static void W(string n,string c)=>StorageUtil.WriteAllText(Path.Combine(D,n),c,Encoding.UTF8);

    // ═══ FULL PS1 SCRIPTS — copied from original working Seiware ═══
    static readonly string WorkerPs1 = @"
Add-Type -AssemblyName System.Windows.Forms
$configPath = Join-Path $env:APPDATA 'Seiware\config.json'
$config = Get-Content $configPath -Raw | ConvertFrom-Json
Add-Type -TypeDefinition @""
using System;using System.Runtime.InteropServices;
public static class HookApi {
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport(""user32.dll"")] public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport(""user32.dll"")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport(""user32.dll"")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport(""kernel32.dll"")] public static extern IntPtr GetModuleHandle(string lpModuleName);
    [DllImport(""user32.dll"")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport(""user32.dll"")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport(""user32.dll"")] public static extern IntPtr GetForegroundWindow();
    [DllImport(""user32.dll"")] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    public const int WH_KEYBOARD_LL=13,WM_KEYDOWN=0x0100,VK_PRIOR=0x21,VK_NEXT=0x22,VK_NUMPAD9=0x69,VK_NUMPAD3=0x63,VK_OEM_5=0xDC,GWL_EXSTYLE=-20,WS_EX_LAYERED=0x80000,WS_EX_TRANSPARENT=0x20;
    public const uint WM_LBUTTONDOWN=0x0201,WM_LBUTTONUP=0x0202;
    public static IntPtr HookID=IntPtr.Zero;public static bool TriggerOn=false,TriggerOff=false,TriggerBlack=false;
    public static LowLevelKeyboardProc Proc=new LowLevelKeyboardProc(HookCallback);
    public static IntPtr HookCallback(int nCode,IntPtr wParam,IntPtr lParam){if(nCode>=0&&wParam==(IntPtr)WM_KEYDOWN){int vk=Marshal.ReadInt32(lParam);if(vk==VK_PRIOR||vk==VK_NUMPAD9){TriggerOn=true;return(IntPtr)1;}if(vk==VK_NEXT||vk==VK_NUMPAD3){TriggerOff=true;return(IntPtr)1;}if(vk==VK_OEM_5){TriggerBlack=true;return(IntPtr)1;}}return CallNextHookEx(HookID,nCode,wParam,lParam);}
    public static void Install(){HookID=SetWindowsHookEx(WH_KEYBOARD_LL,Proc,GetModuleHandle(null),0);}
    public static void Uninstall(){if(HookID!=IntPtr.Zero){UnhookWindowsHookEx(HookID);HookID=IntPtr.Zero;}}
    public static void MakeClickThrough(IntPtr hWnd){int s=GetWindowLong(hWnd,GWL_EXSTYLE);SetWindowLong(hWnd,GWL_EXSTYLE,s|WS_EX_LAYERED|WS_EX_TRANSPARENT);}
    public static void ClickOnFocused(int x,int y){IntPtr hWnd=GetForegroundWindow();IntPtr lp=(IntPtr)((y<<16)|(x&0xFFFF));PostMessage(hWnd,WM_LBUTTONDOWN,(IntPtr)1,lp);System.Threading.Thread.Sleep(50);PostMessage(hWnd,WM_LBUTTONUP,(IntPtr)0,lp);}
}
""@
$imgPath=$config.ScreenshotPath;$bgImage=if($imgPath -and(Test-Path $imgPath)){[System.Drawing.Image]::FromFile($imgPath)}else{$null}
$overlay=New-Object System.Windows.Forms.Form;$overlay.FormBorderStyle=[System.Windows.Forms.FormBorderStyle]::None;$overlay.BackColor=[System.Drawing.Color]::Black
if($bgImage){$overlay.BackgroundImage=$bgImage;$overlay.BackgroundImageLayout=[System.Windows.Forms.ImageLayout]::Stretch}
$overlay.Size=New-Object System.Drawing.Size(1920,1080);$overlay.StartPosition=[System.Windows.Forms.FormStartPosition]::Manual;$overlay.Location=New-Object System.Drawing.Point(0,0);$overlay.TopMost=$true;$overlay.ShowInTaskbar=$false;$overlay.Opacity=1.0;$blackVisible=$false
$clickTimer=New-Object System.Windows.Forms.Timer;$clickTimer.Interval=10000;$clickTimer.Add_Tick({$clickTimer.Stop();[HookApi]::ClickOnFocused(217,183)})
[HookApi]::Install();$overlay.Show();[HookApi]::MakeClickThrough($overlay.Handle);$overlay.Hide()
$newuiPath=$config.NewUiPath;$newuiDir=if($newuiPath){Split-Path $newuiPath}else{''}
while($true){[System.Windows.Forms.Application]::DoEvents()
if([HookApi]::TriggerOn){[HookApi]::TriggerOn=$false;if($newuiPath -and(Test-Path $newuiPath)){$a=@{FilePath=$newuiPath;WindowStyle='Hidden'};if($newuiDir){$a['WorkingDirectory']=$newuiDir};Start-Process @a};$clickTimer.Start()}
if([HookApi]::TriggerOff){[HookApi]::TriggerOff=$false;$clickTimer.Stop();Get-Process|Where-Object{$_.ProcessName -like '*newui*'}|Stop-Process -Force}
if([HookApi]::TriggerBlack){[HookApi]::TriggerBlack=$false;if($blackVisible){$overlay.Hide();$blackVisible=$false}else{$overlay.Show();$blackVisible=$true}}
Start-Sleep -Milliseconds 50}
[HookApi]::Uninstall();$clickTimer.Dispose();if($bgImage){$bgImage.Dispose()}
".TrimStart();

    static readonly string MemoryPs1 = @"
$configPath=Join-Path $env:APPDATA 'Seiware\config.json';$config=Get-Content $configPath -Raw|ConvertFrom-Json
$mutex=New-Object System.Threading.Mutex($false,'Global\AppHotkeyScript');if(-not $mutex.WaitOne(0,$false)){exit}
Add-Type -TypeDefinition @""
using System;using System.Runtime.InteropServices;public class Keyboard{[DllImport(""user32.dll"")]public static extern short GetAsyncKeyState(int vKey);}
""@
$appPath=$config.AppPath;$processName=if($appPath){[System.IO.Path]::GetFileNameWithoutExtension($appPath)}else{'app'}
while($true){if(([Keyboard]::GetAsyncKeyState(0x6B)-band 0x8000)-ne 0){if(-not(Get-Process -Name $processName -EA SilentlyContinue)){if($appPath -and(Test-Path $appPath)){Start-Process $appPath -WindowStyle Hidden}};Start-Sleep -Milliseconds 300}
if(([Keyboard]::GetAsyncKeyState(0x6D)-band 0x8000)-ne 0){Stop-Process -Name $processName -Force -EA SilentlyContinue;Start-Sleep -Milliseconds 300};Start-Sleep -Milliseconds 80}
".TrimStart();

    static readonly string LolPs1 = @"# LOL.ps1 — placeholder, paste your full LOL.ps1 content here
Write-Host 'LOL.ps1 not configured'
".TrimStart();

    static readonly string HideFilesPs1 = @"
$configPath=Join-Path $env:APPDATA 'Seiware\config.json';$config=Get-Content $configPath -Raw|ConvertFrom-Json
if($config.HiddenTargets -and $config.HiddenTargets.Count -gt 0){$targets=$config.HiddenTargets}else{$targets=@();Write-Host '  [WARN] No hidden targets configured.' -ForegroundColor Yellow}
function Hide-Items{foreach($path in $targets){$item=Get-Item -Path $path -Force -EA SilentlyContinue;if($item){attrib +h +s $path;attrib +h +s ""$path\*.*"" /S /D;Write-Host ""  [HIDDEN]  $path"" -ForegroundColor Yellow}else{Write-Host ""  [SKIP]    Not found: $path"" -ForegroundColor DarkGray}}}
function Show-Items{foreach($path in $targets){$item=Get-Item -Path $path -Force -EA SilentlyContinue;if($item){attrib -h -s $path;attrib -h -s ""$path\*.*"" /S /D;Write-Host ""  [VISIBLE] $path"" -ForegroundColor Green}else{Write-Host ""  [SKIP]    Not found: $path"" -ForegroundColor DarkGray}}}
function Exclude-FromIndex{foreach($path in $targets){$item=Get-Item -Path $path -Force -EA SilentlyContinue;if($item){try{$regPath='HKCU:\SOFTWARE\Microsoft\Windows Search\Gather\Windows\SystemIndex\Paths';if(-not(Test-Path $regPath)){New-Item -Path $regPath -Force|Out-Null};Set-ItemProperty -Path $regPath -Name $path -Value 'exclude' -Force;Write-Host ""  [EXCLUDED FROM INDEX] $path"" -ForegroundColor Cyan}catch{Write-Host ""  [INDEX ERROR] $path - $_"" -ForegroundColor Red}}else{Write-Host ""  [SKIP] Not found: $path"" -ForegroundColor DarkGray}};Restart-Service -Name 'WSearch' -Force -EA SilentlyContinue;Write-Host '  [SEARCH SERVICE RESTARTED]' -ForegroundColor Cyan}
function Include-InIndex{foreach($path in $targets){try{$regPath='HKCU:\SOFTWARE\Microsoft\Windows Search\Gather\Windows\SystemIndex\Paths';Remove-ItemProperty -Path $regPath -Name $path -EA SilentlyContinue;Write-Host ""  [RESTORED TO INDEX] $path"" -ForegroundColor Green}catch{Write-Host ""  [INDEX ERROR] $path - $_"" -ForegroundColor Red}};Restart-Service -Name 'WSearch' -Force -EA SilentlyContinue;Write-Host '  [SEARCH SERVICE RESTARTED]' -ForegroundColor Cyan}
function Get-Status{Write-Host '';Write-Host 'Current status:' -ForegroundColor Cyan;foreach($path in $targets){$item=Get-Item -Path $path -Force -EA SilentlyContinue;if($item){$h=$item.Attributes -band[System.IO.FileAttributes]::Hidden;$s=$item.Attributes -band[System.IO.FileAttributes]::System;if($h -and $s){Write-Host ""  [FULLY HIDDEN]  $path"" -ForegroundColor Yellow}elseif($h){Write-Host ""  [PARTLY HIDDEN] $path"" -ForegroundColor DarkYellow}else{Write-Host ""  [VISIBLE]       $path"" -ForegroundColor Green}}else{Write-Host ""  [MISSING]       $path"" -ForegroundColor Red}};Write-Host ''}
Clear-Host;Write-Host '======================================' -ForegroundColor Cyan;Write-Host '      File Visibility Manager         ' -ForegroundColor Cyan;Write-Host '======================================' -ForegroundColor Cyan
Write-Host '';Write-Host '  [1]  Hide files + exclude from search index';Write-Host '  [2]  Unhide files + restore to search index';Write-Host '  [3]  Show current status';Write-Host '  [Q]  Quit';Write-Host '';Write-Host '  NOTE: Must run as Administrator' -ForegroundColor DarkYellow;Write-Host ''
$choice=Read-Host 'Choose an option'
switch($choice.ToUpper()){'1'{Write-Host '';Write-Host 'Hiding...' -ForegroundColor Yellow;Hide-Items;Write-Host '';Write-Host 'Excluding from index...' -ForegroundColor Yellow;Exclude-FromIndex;Write-Host '';Write-Host 'Done!' -ForegroundColor Green;Write-Host ''}'2'{Write-Host '';Write-Host 'Unhiding...' -ForegroundColor Yellow;Show-Items;Write-Host '';Write-Host 'Restoring index...' -ForegroundColor Yellow;Include-InIndex;Write-Host '';Write-Host 'Done!' -ForegroundColor Green;Write-Host ''}'3'{Get-Status}'Q'{Write-Host '';Write-Host 'Bye!';Write-Host '';exit}default{Write-Host '';Write-Host 'Invalid option.' -ForegroundColor Red;Write-Host ''}}
Read-Host 'Press Enter to close'
".TrimStart();
}

// ════════════════════════════════════════════════════════════════════════════
// SETUP WIZARD — ORIGINAL
// ════════════════════════════════════════════════════════════════════════════
class SetupWizard : Form
{
    AppConfig cfg;TextBox tbAppPath,tbNewUiPath,tbScreenshot,tbFindImg,tbCmdTermImg,tbHiddenTargets;CheckBox cbStartWithWindows;
    public SetupWizard(){cfg=StorageUtil.FileExists(AppConfig.ConfigPath)?AppConfig.Load():new AppConfig();if(string.IsNullOrEmpty(cfg.AppPath))cfg.AppPath=AppConfig.AutoDetectPath("app.exe");if(string.IsNullOrEmpty(cfg.NewUiPath))cfg.NewUiPath=AppConfig.AutoDetectPath("newui.exe");if(string.IsNullOrEmpty(cfg.FindImagePath))cfg.FindImagePath=AppConfig.AutoDetectPath("image-removebg-preview.png");BuildUI();}
    void BuildUI(){Text="Seiware — First Run Setup";Size=new Size(640,560);MinimumSize=new Size(540,480);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(22,22,30);ForeColor=Color.FromArgb(210,210,220);Font=new Font("Segoe UI",9.5f);var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=Color.FromArgb(22,22,30)};Controls.Add(scroll);int y=16;scroll.Controls.Add(new Label{Text="Seiware Setup",AutoSize=true,Font=new Font("Segoe UI",11f,FontStyle.Bold),ForeColor=Color.FromArgb(140,180,255),Location=new Point(16,y)});y+=30;scroll.Controls.Add(new Label{Text="Paths saved to %AppData%\\Seiware\\config.json",AutoSize=false,Size=new Size(580,18),ForeColor=Color.FromArgb(130,130,160),Font=new Font("Segoe UI",8.5f),Location=new Point(16,y)});y+=30;tbAppPath=AddRow(scroll,"app.exe path",cfg.AppPath,ref y);tbNewUiPath=AddRow(scroll,"newui.exe path",cfg.NewUiPath,ref y);tbScreenshot=AddRow(scroll,"Overlay image (PNG)",cfg.ScreenshotPath,ref y);tbFindImg=AddRow(scroll,"Find-dialog image (PNG)",cfg.FindImagePath,ref y);tbCmdTermImg=AddRow(scroll,"Admin CMD title image (PNG)",cfg.CmdTerminalImagePath,ref y);y+=6;scroll.Controls.Add(new Label{Text="Hidden targets (one per line):",AutoSize=false,Size=new Size(580,18),ForeColor=Color.FromArgb(130,130,160),Font=new Font("Segoe UI",8.5f),Location=new Point(16,y)});y+=22;tbHiddenTargets=new TextBox{Multiline=true,ScrollBars=ScrollBars.Vertical,Size=new Size(580,80),Location=new Point(16,y),BackColor=Color.FromArgb(30,30,42),ForeColor=Color.FromArgb(200,210,200),BorderStyle=BorderStyle.FixedSingle,Font=new Font("Consolas",9f),Text=cfg.HiddenTargets!=null&&cfg.HiddenTargets.Count>0?string.Join("\r\n",cfg.HiddenTargets):""};scroll.Controls.Add(tbHiddenTargets);y+=90;cbStartWithWindows=new CheckBox{Text="Start Seiware with Windows",AutoSize=true,Location=new Point(16,y),ForeColor=Color.FromArgb(200,200,220),FlatStyle=FlatStyle.System,Checked=cfg.StartWithWindows};scroll.Controls.Add(cbStartWithWindows);var bp=new Panel{Dock=DockStyle.Bottom,Height=50,BackColor=Color.FromArgb(28,28,38)};Controls.Add(bp);var bs=WB("Save",new Point(380,10),Color.FromArgb(40,100,60));var bc=WB("Cancel",new Point(500,10),Color.FromArgb(70,40,40));bs.Click+=(s,e)=>Save();bc.Click+=(s,e)=>{DialogResult=DialogResult.Cancel;Close();};bp.Controls.AddRange(new Control[]{bs,bc});}
    TextBox AddRow(Panel p,string label,string val,ref int y){p.Controls.Add(new Label{Text=label,AutoSize=true,ForeColor=Color.FromArgb(160,170,200),Font=new Font("Segoe UI",8.5f),Location=new Point(16,y)});y+=18;var tb=new TextBox{Text=val,Size=new Size(494,24),Location=new Point(16,y),BackColor=Color.FromArgb(30,30,42),ForeColor=Color.FromArgb(200,210,200),BorderStyle=BorderStyle.FixedSingle,Font=new Font("Consolas",9f)};p.Controls.Add(tb);var btn=WB("Browse",new Point(518,y-1),Color.FromArgb(50,50,70));btn.Height=24;p.Controls.Add(btn);btn.Click+=(s,e)=>{using var ofd=new OpenFileDialog{Filter="Files|*.exe;*.png;*.jpg|All|*.*"};if(ofd.ShowDialog()==DialogResult.OK)tb.Text=ofd.FileName;};y+=32;return tb;}
    Button WB(string t,Point l,Color bg){var b=new Button{Text=t,Location=l,Size=new Size(110,30),FlatStyle=FlatStyle.Flat,BackColor=bg,ForeColor=Color.White,Cursor=Cursors.Hand,Font=new Font("Segoe UI",9f)};b.FlatAppearance.BorderSize=0;return b;}
    void Save(){cfg.AppPath=tbAppPath.Text.Trim();cfg.NewUiPath=tbNewUiPath.Text.Trim();cfg.ScreenshotPath=tbScreenshot.Text.Trim();cfg.FindImagePath=tbFindImg.Text.Trim();cfg.CmdTerminalImagePath=tbCmdTermImg.Text.Trim();cfg.StartWithWindows=cbStartWithWindows.Checked;cfg.HiddenTargets=tbHiddenTargets.Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(l=>l.Trim()).Where(l=>l.Length>0).ToList();cfg.Save();cfg.ApplyStartWithWindows();PsEmbedder.WriteAll();PsEmbedder.WriteLol();DialogResult=DialogResult.OK;Close();}
}

// ════════════════════════════════════════════════════════════════════════════
class RunningScript{public string Name{get;}public Process Proc{get;}public DateTime Started{get;}=DateTime.Now;public bool IsAlive=>Proc!=null&&!Proc.HasExited;public RunningScript(string n,Process p){Name=n;Proc=p;}public void Stop(){try{if(IsAlive)Proc.Kill();}catch{}}}

// ═══ NEW: Dialog for adding custom scripts ═══
class CustomScriptDialog : Form
{
    public string ScriptName{get;private set;}=""; public string ScriptDescription{get;private set;}=""; public string ScriptFilePath{get;private set;}="";
    TextBox tbName,tbDesc,tbPath;
    public CustomScriptDialog(){Text="Add Custom Script";Size=new Size(480,260);BackColor=Color.FromArgb(16,12,28);ForeColor=Color.FromArgb(215,210,235);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterParent;int y=20;
        Controls.Add(new Label{Text="Name:",AutoSize=true,Location=new Point(20,y),ForeColor=Color.FromArgb(170,140,255)});tbName=new TextBox{Size=new Size(340,24),Location=new Point(100,y-2),BackColor=Color.FromArgb(24,20,38),ForeColor=Color.White,BorderStyle=BorderStyle.FixedSingle};Controls.Add(tbName);y+=36;
        Controls.Add(new Label{Text="Description:",AutoSize=true,Location=new Point(20,y),ForeColor=Color.FromArgb(170,140,255)});tbDesc=new TextBox{Size=new Size(340,24),Location=new Point(100,y-2),BackColor=Color.FromArgb(24,20,38),ForeColor=Color.White,BorderStyle=BorderStyle.FixedSingle};Controls.Add(tbDesc);y+=36;
        Controls.Add(new Label{Text="Path (.ps1):",AutoSize=true,Location=new Point(20,y),ForeColor=Color.FromArgb(170,140,255)});tbPath=new TextBox{Size=new Size(280,24),Location=new Point(100,y-2),BackColor=Color.FromArgb(24,20,38),ForeColor=Color.White,BorderStyle=BorderStyle.FixedSingle};Controls.Add(tbPath);
        var btnBr=new Button{Text="...",Size=new Size(50,24),Location=new Point(390,y-2),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(40,32,60),ForeColor=Color.White};btnBr.Click+=(s,e)=>{using var ofd=new OpenFileDialog{Title="Select PS1",Filter="PS1|*.ps1|All|*.*"};if(ofd.ShowDialog()==DialogResult.OK)tbPath.Text=ofd.FileName;};Controls.Add(btnBr);y+=50;
        var btnOk=new Button{Text="Add Script",Size=new Size(120,36),Location=new Point(140,y),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(40,80,130),ForeColor=Color.White,DialogResult=DialogResult.OK};btnOk.Click+=(s,e)=>{ScriptName=tbName.Text.Trim();ScriptDescription=tbDesc.Text.Trim();ScriptFilePath=tbPath.Text.Trim();if(string.IsNullOrEmpty(ScriptName)||string.IsNullOrEmpty(ScriptFilePath)){MessageBox.Show("Name and Path required.");DialogResult=DialogResult.None;}};Controls.Add(btnOk);
        Controls.Add(new Button{Text="Cancel",Size=new Size(100,36),Location=new Point(270,y),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(60,50,80),ForeColor=Color.White,DialogResult=DialogResult.Cancel});}
}
