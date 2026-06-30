# DreamLand UI → C# .NET Framework Integration

Complete guide to replace your existing Seiware/DreamLand WinForms UI with the new
web UI. The web UI runs inside WebView2, is streamproof, and every button/toggle/input
auto-forwards structured JSON to your C# backend. Zero JS injection needed.

Your existing backend (FakeTerminal.cs, named pipes, LOL.ps1, worker.ps1,
HideFiles.ps1, Memory_Optimizer) stays unchanged. Only the UI layer swaps out.

---

## FILE STRUCTURE

```
DreamLand/
├── DreamLand.sln
├── DreamLand.csproj
├── Program.cs                        ← entry point
├── FakeTerminal.cs                   ← EXISTING — unchanged
├── AdminTerminal.cs                  ← EXISTING — unchanged
├── DreamLandConfig.cs                ← NEW: config model
├── DreamLandForm.cs                  ← NEW: replaces old MainForm
├── DreamLandForm.Designer.cs         ← NEW: designer
├── DreamLandBridge.cs                ← NEW: UI↔C# message router
├── ScriptManager.cs                  ← NEW: manages script processes
├── ui/
│   └── index.html                    ← copy from dist/index.html
├── LOL.ps1                           ← EXISTING — unchanged
├── worker.ps1                        ← EXISTING — unchanged
├── HideFiles.ps1                     ← EXISTING — unchanged
├── cmdterminal.ico                   ← EXISTING — unchanged
└── config.json                       ← at %APPDATA%\Seiware\config.json
```

---

## STEP 1: NUGET PACKAGES

```xml
<PackageReference Include="Microsoft.Web.WebView2" Version="1.0.*" />
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
```

---

## STEP 2: COPY UI

Copy `dist/index.html` → `ui/index.html` in your project.

```xml
<ItemGroup>
  <Content Include="ui\index.html">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

---

## STEP 3: DreamLandConfig.cs

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace DreamLand
{
    public class DreamLandConfig
    {
        // ── Toggles ──
        public bool Intercept { get; set; } = false;
        public bool StartWithWindows { get; set; } = false;

        // ── Component Paths (matches UI Config tab exactly) ──
        public string DreamLandExe { get; set; } = @"C:\Seiware\DreamLand.exe";
        public string TerminalExe { get; set; } = @"C:\Seiware\Terminal.exe";
        public string PowerShellExe { get; set; } = @"C:\Seiware\Windows PowerShell.exe";
        public string LauncherExe { get; set; } = @"C:\Seiware\Launcher.exe";
        public string TerminalIco { get; set; } = @"C:\Seiware\Assets\terminal.ico";
        public string PowerShellIco { get; set; } = @"C:\Seiware\Assets\powershell.ico";
        public string ConfigJsonPath { get; set; } = @"C:\Users\ACER\AppData\Roaming\Seiware\config.json";
        public string ScriptsDirectory { get; set; } = @"C:\Seiware\Scripts\";
        public string WorkerPs1 { get; set; } = @"C:\Seiware\worker.ps1";
        public string MemoryOptimizerPs1 { get; set; } = @"C:\Seiware\Memory_Optimizer.ps1";
        public string LolPs1 { get; set; } = @"C:\Seiware\LOL.ps1";
        public string HideFilesPs1 { get; set; } = @"C:\Seiware\HideFiles.ps1";
        public string CmdShortcut { get; set; } = @"C:\Seiware\Command Prompt.exe";
        public string PsShortcut { get; set; } = @"C:\Seiware\Windows PowerShell.exe";

        // ── Banned names (FakeTerminal censor regex) ──
        public List<string> BannedNames { get; set; } = new List<string>();

        // ── Scripts ──
        public List<ScriptEntry> Scripts { get; set; } = new List<ScriptEntry>
        {
            new ScriptEntry { Id = "1", Name = "Placeholder Script 1", Path = "" },
            new ScriptEntry { Id = "2", Name = "Placeholder Script 2", Path = "" },
            new ScriptEntry { Id = "3", Name = "Placeholder Script 3", Path = "" },
            new ScriptEntry { Id = "4", Name = "Placeholder Script 4", Path = "" },
        };
        public List<ScriptEntry> CustomScripts { get; set; } = new List<ScriptEntry>();

        // ── Theme ──
        public string ThemeColor { get; set; } = "#dc2626";
        public string Title { get; set; } = "DREAMLAND";

        // ── Path ID → Property mapping ──
        public string GetPath(string id)
        {
            switch (id)
            {
                case "dreamland": return DreamLandExe;
                case "terminal": return TerminalExe;
                case "powershell": return PowerShellExe;
                case "launcher": return LauncherExe;
                case "terminal_ico": return TerminalIco;
                case "powershell_ico": return PowerShellIco;
                case "config": return ConfigJsonPath;
                case "scripts_dir": return ScriptsDirectory;
                case "worker": return WorkerPs1;
                case "memory_optimizer": return MemoryOptimizerPs1;
                case "lol": return LolPs1;
                case "hidefiles": return HideFilesPs1;
                case "cmd_shortcut": return CmdShortcut;
                case "ps_shortcut": return PsShortcut;
                default: return "";
            }
        }

        public void SetPath(string id, string value)
        {
            switch (id)
            {
                case "dreamland": DreamLandExe = value; break;
                case "terminal": TerminalExe = value; break;
                case "powershell": PowerShellExe = value; break;
                case "launcher": LauncherExe = value; break;
                case "terminal_ico": TerminalIco = value; break;
                case "powershell_ico": PowerShellIco = value; break;
                case "config": ConfigJsonPath = value; break;
                case "scripts_dir": ScriptsDirectory = value; break;
                case "worker": WorkerPs1 = value; break;
                case "memory_optimizer": MemoryOptimizerPs1 = value; break;
                case "lol": LolPs1 = value; break;
                case "hidefiles": HideFilesPs1 = value; break;
                case "cmd_shortcut": CmdShortcut = value; break;
                case "ps_shortcut": PsShortcut = value; break;
            }
        }

        public void SetAllPaths(Dictionary<string, string> paths)
        {
            foreach (var kv in paths) SetPath(kv.Key, kv.Value);
        }

        // ── Load / Save ──
        // Config lives at: C:\Users\ACER\AppData\Roaming\Seiware\config.json
        public static string DefaultPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Seiware", "config.json");

        public static DreamLandConfig Load(string path = null)
        {
            path = path ?? DefaultPath;
            if (!File.Exists(path)) return new DreamLandConfig();
            return JsonConvert.DeserializeObject<DreamLandConfig>(
                File.ReadAllText(path)) ?? new DreamLandConfig();
        }

        public void Save(string path = null)
        {
            path = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
    }

    public class ScriptEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; } = "";
    }
}
```

---

## STEP 4: ScriptManager.cs

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace DreamLand
{
    public class ScriptManager
    {
        private readonly Dictionary<string, Process> _running = new Dictionary<string, Process>();
        private readonly DreamLandConfig _config;

        public ScriptManager(DreamLandConfig config) { _config = config; }

        public bool IsRunning(string id) =>
            _running.ContainsKey(id) && !_running[id].HasExited;

        public bool Toggle(string id, bool isCustom)
        {
            if (IsRunning(id)) { Kill(id); return false; }
            return Launch(id, isCustom);
        }

        public bool Launch(string id, bool isCustom)
        {
            var list = isCustom ? _config.CustomScripts : _config.Scripts;
            var entry = list.Find(s => s.Id == id);
            if (entry == null || string.IsNullOrEmpty(entry.Path)) return false;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = entry.Path,
                    WorkingDirectory = Path.GetDirectoryName(entry.Path) ?? "",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                var proc = Process.Start(psi);
                if (proc != null) { _running[id] = proc; return true; }
            }
            catch (Exception ex) { Debug.WriteLine($"[ScriptManager] {ex.Message}"); }
            return false;
        }

        public void Kill(string id)
        {
            if (_running.TryGetValue(id, out var p))
            {
                try { if (!p.HasExited) p.Kill(); } catch { }
                _running.Remove(id);
            }
        }

        public void KillAll()
        {
            foreach (var kv in _running)
                try { if (!kv.Value.HasExited) kv.Value.Kill(); } catch { }
            _running.Clear();
        }
    }
}
```

---

## STEP 5: DreamLandBridge.cs

Every UI event routes through here. This is the brain.

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using CommandPrompt; // your FakeTerminal namespace

namespace DreamLand
{
    public class DreamLandBridge : IDisposable
    {
        private readonly DreamLandForm _form;
        private readonly DreamLandConfig _config;
        private readonly ScriptManager _scripts;
        private FakeTerminal _terminal;

        public DreamLandBridge(DreamLandForm form, DreamLandConfig config, ScriptManager scripts)
        {
            _form = form;
            _config = config;
            _scripts = scripts;
        }

        // ══════════════════════════════════════
        //  MAIN ROUTER
        // ══════════════════════════════════════
        public void HandleMessage(string rawJson)
        {
            try
            {
                string json = rawJson.Trim('"').Replace("\\\"", "\"").Replace("\\\\", "\\");
                var msg = JObject.Parse(json);
                string type = msg["type"]?.ToString() ?? "";
                JToken data = msg["data"];

                switch (type)
                {
                    // ── Scripts ──
                    case "seiware:script:toggle":
                        Script_Toggle(data["id"].ToString(), (bool)data["isCustom"]);
                        break;
                    case "seiware:script:add":
                        Script_Add(data["id"].ToString());
                        break;
                    case "seiware:script:rename":
                        Script_Rename(data["id"].ToString(), data["name"].ToString(), (bool)data["isCustom"]);
                        break;

                    // ── Terminal ──
                    case "seiware:terminal:action":
                        Terminal_Action(data["action"].ToString());
                        break;

                    // ── Config ──
                    case "seiware:config:action":
                        Config_Action(data["action"].ToString());
                        break;
                    case "seiware:config:toggle":
                        Config_Toggle(data["setting"].ToString(), (bool)data["value"]);
                        break;
                    case "seiware:config:path:save":
                        Config_PathSave(data["id"].ToString(), data["path"].ToString());
                        break;
                    case "seiware:config:paths:saveall":
                        Config_SaveAllPaths(data["paths"] as JObject);
                        break;
                    case "seiware:config:paths:copy":
                        break; // UI handles clipboard
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[Bridge] {ex.Message}"); }
        }

        // ── SCRIPTS ──

        private void Script_Toggle(string id, bool isCustom)
        {
            bool running = _scripts.Toggle(id, isCustom);
            _form.RunJS($@"window.dispatchEvent(new CustomEvent('dreamland:backend:script:state',{{
                detail:{{id:'{id}',running:{running.ToString().ToLower()}}}}}))");
        }

        private void Script_Add(string id)
        {
            _form.Invoke((Action)(() =>
            {
                using (var ofd = new OpenFileDialog())
                {
                    ofd.Title = "Select Script";
                    ofd.Filter = "Executables|*.exe;*.ps1;*.bat;*.cmd|All|*.*";
                    ofd.InitialDirectory = _config.ScriptsDirectory;
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        _config.CustomScripts.Add(new ScriptEntry
                        {
                            Id = id,
                            Name = System.IO.Path.GetFileNameWithoutExtension(ofd.FileName),
                            Path = ofd.FileName,
                        });
                        _config.Save();
                    }
                }
            }));
        }

        private void Script_Rename(string id, string name, bool isCustom)
        {
            var list = isCustom ? _config.CustomScripts : _config.Scripts;
            var entry = list.Find(s => s.Id == id);
            if (entry != null) { entry.Name = name; _config.Save(); }
        }

        // ── TERMINAL ──
        // Uses your existing FakeTerminal.cs named pipe protocol

        private void Terminal_Action(string action)
        {
            switch (action)
            {
                case "start_session":
                    Terminal_Start(admin: false);
                    break;
                case "end_session":
                    Terminal_Stop();
                    break;
                case "clear":
                    _form.RunJS(@"
                        var c=document.getElementById('seiware-terminal-container');
                        if(c){var d=c.querySelector('.terminal-output');if(d)d.innerHTML='';}
                    ");
                    break;
                case "admin":
                    Terminal_Start(admin: true);
                    break;
            }
        }

        private void Terminal_Start(bool admin)
        {
            Terminal_Stop();
            Regex censor = null;
            if (_config.BannedNames.Count > 0)
                censor = new Regex(string.Join("|", _config.BannedNames),
                    RegexOptions.IgnoreCase | RegexOptions.Compiled);

            _terminal = new FakeTerminal(
                admin: admin,
                shellType: admin ? ShellType.PowerShell : ShellType.Cmd,
                img: null,
                censor: censor,
                iconPath: admin ? _config.PowerShellIco : _config.TerminalIco,
                pipeName: "SeiwareFakeTerminal"
            );
            System.Threading.Tasks.Task.Run(() => { try { _terminal.Run(); } catch { } });
        }

        private void Terminal_Stop()
        {
            if (_terminal != null) { _terminal.Dispose(); _terminal = null; }
        }

        // ── CONFIG ──

        private void Config_Action(string action)
        {
            switch (action)
            {
                case "open_config":
                    // Opens config.json at C:\Users\ACER\AppData\Roaming\Seiware\config.json
                    try
                    {
                        string configPath = _config.ConfigJsonPath;
                        if (string.IsNullOrEmpty(configPath))
                            configPath = DreamLandConfig.DefaultPath;
                        Process.Start("notepad.exe", configPath);
                    }
                    catch { }
                    break;

                case "setup_wizard":
                    // Tells the DreamLand backend to open its existing setup wizard
                    // Replace with your actual setup wizard call:
                    _form.Invoke((Action)(() =>
                    {
                        // Option A: If you have a SetupWizardForm:
                        // new SetupWizardForm(_config).ShowDialog();

                        // Option B: If setup wizard is a separate exe:
                        // Process.Start(_config.DreamLandExe, "--setup");

                        // Placeholder until wired:
                        MessageBox.Show(_form, "Setup Wizard\n\nReplace this with your existing setup wizard call.\n\nExample:\nnew SetupWizardForm(_config).ShowDialog();",
                            "DreamLand Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                    break;

                case "banned_names":
                    // Tells the DreamLand backend to open its existing banned names editor
                    _form.Invoke((Action)(() =>
                    {
                        // Option A: If you have a BannedNamesForm:
                        // new BannedNamesForm(_config).ShowDialog();

                        // Option B: Simple InputBox fallback:
                        string current = string.Join("|", _config.BannedNames);
                        string input = Microsoft.VisualBasic.Interaction.InputBox(
                            "Banned names separated by | (pipe):\n\n" +
                            "These are used by FakeTerminal's censor regex.\n" +
                            "Example: seiware|dreamland|hack",
                            "DreamLand — Banned Names", current);
                        if (!string.IsNullOrEmpty(input) || input == "")
                        {
                            _config.BannedNames.Clear();
                            if (!string.IsNullOrEmpty(input))
                            {
                                foreach (var n in input.Split('|'))
                                {
                                    string t = n.Trim();
                                    if (t.Length > 0) _config.BannedNames.Add(t);
                                }
                            }
                            _config.Save();
                        }
                    }));
                    break;
            }
        }

        private void Config_Toggle(string setting, bool value)
        {
            switch (setting)
            {
                case "intercept":
                    _config.Intercept = value;
                    break;
                case "start_with_windows":
                    _config.StartWithWindows = value;
                    try
                    {
                        var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                        if (value)
                            key?.SetValue("DreamLand", Application.ExecutablePath);
                        else
                            key?.DeleteValue("DreamLand", false);
                    }
                    catch { }
                    break;
            }
            _config.Save();
        }

        private void Config_PathSave(string id, string path)
        {
            _config.SetPath(id, path);
            _config.Save();
        }

        private void Config_SaveAllPaths(JObject paths)
        {
            if (paths == null) return;
            var dict = new Dictionary<string, string>();
            foreach (var prop in paths.Properties())
                dict[prop.Name] = prop.Value.ToString();
            _config.SetAllPaths(dict);
            _config.Save();
        }

        public void Dispose()
        {
            Terminal_Stop();
            _scripts.KillAll();
        }
    }
}
```

---

## STEP 6: DreamLandForm.cs

```csharp
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

namespace DreamLand
{
    public partial class DreamLandForm : Form
    {
        private WebView2 webView;
        private DreamLandConfig config;
        private ScriptManager scripts;
        private DreamLandBridge bridge;

        [DllImport("user32.dll")]
        static extern uint SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string wnd);
        const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        public DreamLandForm()
        {
            InitializeComponent();
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.BackColor = System.Drawing.Color.Black;

            config = DreamLandConfig.Load();
            scripts = new ScriptManager(config);
            bridge = new DreamLandBridge(this, config, scripts);
            InitWebView();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SetWindowDisplayAffinity(this.Handle, WDA_EXCLUDEFROMCAPTURE);
            // WebView2 child window spawns async — poll for it
            Timer t = new Timer { Interval = 500 };
            t.Tick += (s, _) =>
            {
                IntPtr child = FindWindowEx(webView.Handle, IntPtr.Zero,
                    "Chrome_WidgetWin_0", null);
                if (child != IntPtr.Zero)
                {
                    SetWindowDisplayAffinity(child, WDA_EXCLUDEFROMCAPTURE);
                    t.Stop(); t.Dispose();
                }
            };
            t.Start();
        }

        private async void InitWebView()
        {
            webView = new WebView2 { Dock = DockStyle.Fill };
            this.Controls.Add(webView);

            string udf = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DreamLand", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, udf);
            await webView.EnsureCoreWebView2Async(env);

            webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.Settings.IsZoomControlEnabled = false;

            webView.CoreWebView2.WebMessageReceived += (s, e) =>
                bridge.HandleMessage(e.WebMessageAsJson);

            string html = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui", "index.html");
            if (File.Exists(html))
                webView.CoreWebView2.Navigate(new Uri(html).AbsoluteUri);
            else
                MessageBox.Show($"UI not found:\n{html}", "DreamLand");

            webView.CoreWebView2.NavigationCompleted += async (s, e) =>
                await PushConfigToUI();
        }

        private async System.Threading.Tasks.Task PushConfigToUI()
        {
            // Push saved config state into UI on startup
            RunJS($@"window.dispatchEvent(new CustomEvent('dreamland:backend:init',{{
                detail:{{
                    intercept:{config.Intercept.ToString().ToLower()},
                    startWithWindows:{config.StartWithWindows.ToString().ToLower()},
                    themeColor:'{config.ThemeColor}',
                    title:'{config.Title}'
                }}}}))");
        }

        public async void RunJS(string script)
        {
            try { if (webView?.CoreWebView2 != null)
                await webView.CoreWebView2.ExecuteScriptAsync(script); }
            catch { }
        }

        public void OpenTab(string tab) => RunJS($"window.SeiwareUI.openTab('{tab}')");
        public void CloseTab() => RunJS("window.SeiwareUI.closeTab()");

        public void SendTerminalOutput(string text)
        {
            string esc = Newtonsoft.Json.JsonConvert.SerializeObject(text);
            RunJS($@"window.dispatchEvent(new CustomEvent('dreamland:backend:terminal:output',{{
                detail:{{text:{esc}}}}}}))" );
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            bridge.Dispose();
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                RunJS("window.SeiwareUI.getActiveTab()?window.SeiwareUI.closeTab():null");
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
```

---

## STEP 7: DreamLandForm.Designer.cs

```csharp
namespace DreamLand
{
    partial class DreamLandForm
    {
        private System.ComponentModel.IContainer components = null;
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }
        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1920, 1080);
            this.Name = "DreamLandForm";
            this.Text = "DreamLand";
            this.ResumeLayout(false);
        }
    }
}
```

---

## STEP 8: Program.cs

```csharp
using System;
using System.Windows.Forms;

namespace DreamLand
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new DreamLandForm());
        }
    }
}
```

---

## COMPLETE EVENT MAP

### UI → C# (auto-forwarded, zero setup)

| Button / Input | Event | Data | What C# Does |
|---|---|---|---|
| Scripts > Launch/Stop | `seiware:script:toggle` | `{id, isCustom}` | `ScriptManager.Toggle()` |
| Scripts > Add Custom | `seiware:script:add` | `{id}` | File picker → config |
| Scripts > Rename | `seiware:script:rename` | `{id, name, isCustom}` | Config update |
| Terminal > Start Session | `seiware:terminal:action` | `{action:"start_session"}` | `new FakeTerminal(admin:false, ShellType.Cmd)` |
| Terminal > End Session | `seiware:terminal:action` | `{action:"end_session"}` | `FakeTerminal.Dispose()` |
| Terminal > Clear | `seiware:terminal:action` | `{action:"clear"}` | Clears DOM container |
| Terminal > Admin | `seiware:terminal:action` | `{action:"admin"}` | `new FakeTerminal(admin:true, ShellType.PowerShell)` |
| Config > Open Config | `seiware:config:action` | `{action:"open_config"}` | `notepad.exe C:\Users\ACER\AppData\Roaming\Seiware\config.json` |
| Config > Setup Wizard | `seiware:config:action` | `{action:"setup_wizard"}` | Opens existing DreamLand setup wizard |
| Config > Banned Names | `seiware:config:action` | `{action:"banned_names"}` | Opens banned names editor (InputBox or custom form) |
| Config > Intercept | `seiware:config:toggle` | `{setting:"intercept", value}` | `config.Intercept = value` |
| Config > Start w/ Windows | `seiware:config:toggle` | `{setting:"start_with_windows", value}` | Registry Run key |
| Config > Any Path Save | `seiware:config:path:save` | `{id, path}` | `config.SetPath(id, path)` |
| Config > Save All Paths | `seiware:config:paths:saveall` | `{paths:{dreamland:"...", ...}}` | `config.SetAllPaths(dict)` |
| Config > Copy All Paths | `seiware:config:paths:copy` | `{paths:[...]}` | Clipboard (UI handles) |

### C# → UI

```csharp
form.OpenTab("scripts");
form.OpenTab("terminal");
form.OpenTab("config");
form.CloseTab();
form.SendTerminalOutput("C:\\> dir");
form.RunJS("window.SeiwareUI.openTab('config')");
```

---

## HOW IT WORKS

1. Web UI loads in WebView2 (single `index.html`, no server needed)
2. User clicks any button → UI dispatches `seiware:*` CustomEvent
3. App.tsx auto-catches ALL `seiware:*` events
4. Serializes to `{type, data}` JSON
5. Sends via `window.chrome.webview.postMessage()`
6. WebView2 fires `WebMessageReceived` in C#
7. `DreamLandBridge.HandleMessage()` routes to the right handler
8. Handler does the work (launch process, save config, open file, etc.)

**No JS injection. No bridge scripts. Built into the UI.**

---

## STREAMPROOF

- `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` on main form handle
- Same on WebView2's `Chrome_WidgetWin_0` child (timer polls until it spawns)
- DevTools disabled, context menu disabled, zoom disabled
- No `canvas.toDataURL()`, no WebRTC, no screenshot vectors in the UI

---

## FOR ANOTHER AI: DO THIS

1. Create these 6 C# files in the project (copy-paste from above)
2. Copy `dist/index.html` → `ui/index.html`
3. Add NuGet: `Microsoft.Web.WebView2`, `Newtonsoft.Json`
4. Delete old WinForms UI files (old MainForm, old Designer, etc.)
5. Keep FakeTerminal.cs, AdminTerminal.cs, LOL.ps1, worker.ps1, HideFiles.ps1 unchanged
6. Wire FakeTerminal pipe output → `form.SendTerminalOutput(text)`
7. Wire setup wizard button → your existing `SetupWizardForm` or setup exe
8. Wire banned names button → your existing `BannedNamesForm` or the InputBox fallback
9. Config lives at `C:\Users\ACER\AppData\Roaming\Seiware\config.json`
10. Build, run, test each tab. Everything is streamproof.

---

**Made by Ang3l** | DreamLand v3.7.1
