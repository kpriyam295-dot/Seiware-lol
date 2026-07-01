// DreamLandForm.cs — WebView2 UI replacement
// Named DreamLandMainForm so it does NOT conflict with MainForm in Seiware.UI.cs.
// Seiware.UI.cs compiles as-is (untouched). All its classes (HeadlessShellHost,
// FakeTerminalSession, CmdTerminal, MainForm, SettingsForm, GradientPanel,
// CustomScriptDialog) remain available.
//
// DreamLandEntry.Main() replaces EntryPoint.Main() via <StartupObject> in .csproj.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// ════════════════════════════════════════════════════════════════════════════
// NEW ENTRY POINT — replaces EntryPoint in Seiware.cs via <StartupObject>
// Identical logic: single instance, --silent, PsEmbedder, SetupWizard,
// ShortcutManager. Just creates DreamLandMainForm instead of MainForm.
// ════════════════════════════════════════════════════════════════════════════
static class DreamLandEntry
{
    static Mutex singleInstanceMutex;

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        singleInstanceMutex = new Mutex(true, "SeiwareSingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("DreamLand is already running.", "DreamLand",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        bool silent = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));

        PsEmbedder.WriteAll();

        if (!StorageUtil.FileExists(AppConfig.ConfigPath))
        {
            var wizard = new SetupWizard();
            if (wizard.ShowDialog() != DialogResult.OK)
            {
                singleInstanceMutex.ReleaseMutex();
                return;
            }
        }

        var cfg = AppConfig.Load();
        ShortcutManager.EnsureStartMenuShortcuts(cfg);

        Application.Run(new DreamLandMainForm(silent));
        singleInstanceMutex.ReleaseMutex();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// DREAMLAND MAIN FORM — WebView2 UI
// ════════════════════════════════════════════════════════════════════════════
class DreamLandMainForm : Form
{
    WebView2 webView;
    AppConfig cfg;
    ShellInterceptor shellInterceptor;
    Regex censorRegex;
    List<RunningScript> running = new();
    NotifyIcon trayIcon;
    ToolStripMenuItem interceptMenuItem;
    bool realClose = false;

    // Pipe server
    CancellationTokenSource pipeCts = new();
    HeadlessShellHost shellHost;
    // Track what shell type was last intercepted so pipe server creates the right host
    volatile ShellType lastInterceptedShellType = ShellType.Cmd;

    // For single-file publish, AppContext.BaseDirectory may point to temp extraction.
    // The REAL exe directory where ui/ folder lives:
    static string ExeDirectory
    {
        get
        {
            string path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path)) path = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(path)) path = AppContext.BaseDirectory;
            string dir = Path.GetDirectoryName(path);
            return !string.IsNullOrEmpty(dir) ? dir : AppContext.BaseDirectory;
        }
    }

    public DreamLandMainForm(bool silent = false)
    {
        cfg = AppConfig.Load();
        RebuildRegex();
        CommandGuard.SetBannedNames(cfg.BannedNames);
        shellInterceptor = new ShellInterceptor();

        BuildForm();
        BuildTray();
        StartInterceptor();
        StartPipeServer();

        if (silent)
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            trayIcon.Visible = true;
        }
    }

    void RebuildRegex()
    {
        if (cfg.BannedNames == null || cfg.BannedNames.Count == 0)
        { censorRegex = new Regex("(?!)"); return; }
        censorRegex = new Regex(
            @"(?i)(" + string.Join("|", cfg.BannedNames.Select(Regex.Escape)) + @")",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    // ════════════════════════════════════════════════════════════════════════
    // FORM
    // ════════════════════════════════════════════════════════════════════════
    void BuildForm()
    {
        Text = "DreamLand";
        Size = new Size(1100, 720);
        MinimumSize = new Size(800, 500);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        Icon = LoadFormIcon();

        webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.Black,
        };
        Controls.Add(webView);

        // Use Shown instead of Load — WebView2 needs the form to be FULLY VISIBLE
        // before EnsureCoreWebView2Async can complete (it's a windowed control).
        // Load fires too early; Shown fires after the form is rendered on screen.
        Shown += async (s, e) =>
        {
            try { NativeWin32.SetWindowDisplayAffinity(Handle, NativeWin32.WDA_EXCLUDEFROMCAPTURE); } catch { }
            try
            {
                await InitWebView();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebView2] Init error: {ex}");
                MessageBox.Show($"WebView2 failed to initialize:\n{ex.Message}",
                    "DreamLand", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        FormClosing += (s, e) =>
        {
            if (!realClose) { e.Cancel = true; Hide(); trayIcon.Visible = true; }
        };
    }

    Icon LoadFormIcon()
    {
        foreach (var name in new[] { "DreamLand.ico", "Seiware.ico" })
        {
            string p = Path.Combine(ExeDirectory, name);
            if (File.Exists(p)) try { return new Icon(p); } catch { }
        }
        return SystemIcons.Shield;
    }

    // ════════════════════════════════════════════════════════════════════════
    // WEBVIEW2
    // ════════════════════════════════════════════════════════════════════════
    async Task InitWebView()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(
                null, Path.Combine(Path.GetTempPath(), "DreamLand_WebView2"));
            await webView.EnsureCoreWebView2Async(env);

            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            webView.CoreWebView2.Settings.IsBuiltInErrorPageEnabled = false;

            webView.CoreWebView2.WebMessageReceived += OnWebMessage;

            // Find ui/index.html
            string uiDir = null;
            foreach (var dir in new[] {
                Path.Combine(ExeDirectory, "ui"),
                ExeDirectory,
                Path.Combine(AppContext.BaseDirectory, "ui"),
                AppContext.BaseDirectory,
            })
            {
                if (File.Exists(Path.Combine(dir, "index.html")))
                { uiDir = dir; break; }
            }

            if (uiDir != null)
            {
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "dreamland.local", uiDir,
                    CoreWebView2HostResourceAccessKind.Allow);
                webView.CoreWebView2.Navigate("https://dreamland.local/index.html");
            }
            else
            {
                webView.CoreWebView2.NavigateToString(
                    "<html><body style='background:#000;color:#dc2626;font-family:monospace;" +
                    "display:flex;align-items:center;justify-content:center;height:100vh;margin:0'>" +
                    "<div style='text-align:center'><h1>DREAMLAND</h1>" +
                    "<p style='color:#666'>ui/index.html not found</p>" +
                    "<p style='color:#444;font-size:11px'>Checked:<br>" +
                    Path.Combine(ExeDirectory, "ui", "index.html").Replace("\\", "\\\\") + "<br>" +
                    Path.Combine(AppContext.BaseDirectory, "ui", "index.html").Replace("\\", "\\\\") +
                    "</p></div></body></html>");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"WebView2 failed:\n{ex.Message}\n\nInstall WebView2 Runtime.",
                "DreamLand", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // TRAY
    // ════════════════════════════════════════════════════════════════════════
    void BuildTray()
    {
        trayIcon = new NotifyIcon
        {
            Text = "DreamLand",
            Icon = Icon ?? SystemIcons.Shield,
            Visible = false,
        };

        var menu = new ContextMenuStrip();
        menu.BackColor = Color.FromArgb(20, 16, 32);
        menu.ForeColor = Color.FromArgb(210, 210, 230);
        menu.Renderer = new ToolStripProfessionalRenderer(new DarkTrayColors());

        interceptMenuItem = new ToolStripMenuItem("◆ Intercept: ON");
        interceptMenuItem.Click += (s, e) =>
        {
            if (shellInterceptor.IsEnabled)
            { shellInterceptor.Stop(); interceptMenuItem.Text = "◇ Intercept: OFF"; }
            else
            { shellInterceptor.Start(); interceptMenuItem.Text = "◆ Intercept: ON"; }
        };
        menu.Items.Add(interceptMenuItem);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("■ Stop All Scripts", null, (s, e) =>
        { foreach (var r in running) r.Stop(); running.Clear(); });

        menu.Items.Add("⚙ Settings", null, (s, e) =>
        {
            var wiz = new SetupWizard();
            if (wiz.ShowDialog() == DialogResult.OK)
            { cfg = AppConfig.Load(); RebuildRegex(); CommandGuard.SetBannedNames(cfg.BannedNames); }
        });

        menu.Items.Add("🚫 Banned Names", null, (s, e) =>
        {
            var sf = new SettingsForm(cfg.BannedNames);
            if (sf.ShowDialog() == DialogResult.OK)
            { cfg.BannedNames = sf.BannedNames; cfg.Save(); RebuildRegex(); CommandGuard.SetBannedNames(cfg.BannedNames); }
        });

        menu.Items.Add(new ToolStripSeparator());

        var show = new ToolStripMenuItem("Show DreamLand") { Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        show.Click += (s, e) => ShowFromTray();
        menu.Items.Add(show);

        menu.Items.Add("↻ Reload UI", null, (s, e) =>
        { try { webView?.CoreWebView2?.Reload(); } catch { } });

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Exit", null, (s, e) =>
        {
            realClose = true;
            foreach (var r in running) r.Stop();
            running.Clear();
            shellInterceptor?.Stop();
            shellInterceptor?.Dispose();
            pipeCts?.Cancel();
            shellHost?.Dispose();
            trayIcon.Visible = false;
            Application.Exit();
        });

        trayIcon.ContextMenuStrip = menu;
        trayIcon.DoubleClick += (s, e) => ShowFromTray();
    }

    void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        trayIcon.Visible = false;
    }

    class DarkTrayColors : ProfessionalColorTable
    {
        public override Color MenuBorder => Color.FromArgb(60, 50, 80);
        public override Color MenuItemSelected => Color.FromArgb(40, 35, 60);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(40, 35, 60);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(40, 35, 60);
        public override Color MenuStripGradientBegin => Color.FromArgb(20, 16, 32);
        public override Color MenuStripGradientEnd => Color.FromArgb(20, 16, 32);
        public override Color MenuItemBorder => Color.FromArgb(80, 70, 120);
        public override Color SeparatorDark => Color.FromArgb(50, 40, 70);
        public override Color SeparatorLight => Color.FromArgb(50, 40, 70);
        public override Color ToolStripDropDownBackground => Color.FromArgb(20, 16, 32);
        public override Color ImageMarginGradientBegin => Color.FromArgb(20, 16, 32);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(20, 16, 32);
        public override Color ImageMarginGradientEnd => Color.FromArgb(20, 16, 32);
    }

    // ════════════════════════════════════════════════════════════════════════
    // SHELL INTERCEPTOR
    // ════════════════════════════════════════════════════════════════════════
    void StartInterceptor()
    {
        try
        {
            shellInterceptor.OnInterceptNormal = (st) =>
            {
                lastInterceptedShellType = st;
                LaunchFakeTerminal(st, false);
            };
            shellInterceptor.OnInterceptAdmin = (st) =>
            {
                lastInterceptedShellType = st;
                LaunchFakeTerminal(st, true);
            };
            shellInterceptor.Start();
        }
        catch { }
    }

    void LaunchFakeTerminal(ShellType shellType, bool admin)
    {
        try
        {
            string exe, ico;
            if (shellType == ShellType.PowerShell)
            {
                exe = AppConfig.ResolvePath(cfg.FakePowerShellPath, "Windows PowerShell.exe");
                ico = AppConfig.ResolvePath(cfg.PowerShellIcoPath, "powershell.ico");
            }
            else
            {
                exe = AppConfig.ResolvePath(cfg.FakeTerminalPath, "Terminal.exe", "Command Prompt.exe");
                ico = AppConfig.ResolvePath(cfg.CmdTerminalIcoPath, "terminal.ico", "cmdterminal.ico");
            }
            if (!File.Exists(exe)) return;

            string args = admin ? "--admin" : "";
            if (shellType == ShellType.PowerShell) args += " --shell ps";
            if (!string.IsNullOrEmpty(ico) && File.Exists(ico)) args += $" --icon \"{ico}\"";

            string launcher = AppConfig.ResolvePath(cfg.SeiwareLauncherPath, "DreamLandLauncher.exe", "SeiwareLauncher.exe");
            shellInterceptor.SuppressFor(2000);

            if (File.Exists(launcher))
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = launcher,
                    Arguments = $"\"{exe}\" {args}",
                    WorkingDirectory = Path.GetDirectoryName(launcher),
                    UseShellExecute = true,
                });
                if (p != null) shellInterceptor.RegisterExemptPid(p.Id);
            }
            else
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args.Trim(),
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = true,
                });
                if (p != null) shellInterceptor.RegisterExemptPid(p.Id);
            }
        }
        catch { }
    }

    // ════════════════════════════════════════════════════════════════════════
    // PIPE SERVER — FakeTerminal.exe connects here
    // Protocol constants MUST match FakeTerminal.cs:
    //   MSG_OUTPUT=0x01, MSG_CLEAR=0x02, MSG_SESSION_ENDED=0x03,
    //   MSG_CMD_FINISHED=0x04, MSG_COMMAND=0x10
    // ════════════════════════════════════════════════════════════════════════
    void StartPipeServer()
    {
        Task.Run(() => PipeServerLoop(pipeCts.Token));
    }

    async Task PipeServerLoop(CancellationToken ct)
    {
        const string PIPE_NAME = "SeiwareFakeTerminal";
        const byte MSG_OUTPUT = 0x01, MSG_CLEAR = 0x02, MSG_SESSION_ENDED = 0x03, MSG_CMD_FINISHED = 0x04, MSG_COMMAND = 0x10;

        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream server = null;
            try
            {
                server = new NamedPipeServerStream(PIPE_NAME, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);
                if (ct.IsCancellationRequested) { server.Dispose(); break; }

                // Use the shell type from the last intercept event
                ShellType st = lastInterceptedShellType;
                string workDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                var host = new HeadlessShellHost(st, censorRegex);
                shellHost = host;

                if (!host.Start(workDir))
                {
                    server.Dispose();
                    host.Dispose();
                    continue;
                }

                var reader = new BinaryReader(server, Encoding.UTF8, true);
                var writer = new BinaryWriter(server, Encoding.UTF8, true);

                host.OutputChunk += (text) =>
                {
                    try
                    {
                        if (!server.IsConnected) return;
                        byte[] data = Encoding.UTF8.GetBytes(text);
                        lock (writer) { writer.Write(MSG_OUTPUT); writer.Write(data.Length); writer.Write(data); writer.Flush(); }
                    }
                    catch { }
                };

                host.CommandFinished += () =>
                {
                    try
                    {
                        if (!server.IsConnected) return;
                        lock (writer) { writer.Write(MSG_CMD_FINISHED); writer.Flush(); }
                    }
                    catch { }
                };

                host.ScreenCleared += () =>
                {
                    try
                    {
                        if (!server.IsConnected) return;
                        lock (writer) { writer.Write(MSG_CLEAR); writer.Flush(); }
                    }
                    catch { }
                };

                _ = Task.Run(() =>
                {
                    try
                    {
                        while (server.IsConnected && !ct.IsCancellationRequested)
                        {
                            byte msgType = reader.ReadByte();
                            if (msgType == MSG_COMMAND)
                            {
                                int len = reader.ReadInt32();
                                byte[] buf = reader.ReadBytes(len);
                                string cmd = Encoding.UTF8.GetString(buf);
                                host.SendCommand(cmd);
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        try
                        {
                            if (server.IsConnected)
                                lock (writer) { writer.Write(MSG_SESSION_ENDED); writer.Flush(); }
                        }
                        catch { }
                        host.Dispose();
                        server.Dispose();
                    }
                });
            }
            catch (OperationCanceledException) { server?.Dispose(); break; }
            catch { server?.Dispose(); await Task.Delay(500, ct).ConfigureAwait(false); }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // UI → C# BRIDGE
    // ════════════════════════════════════════════════════════════════════════
    void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string raw = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(raw)) raw = e.WebMessageAsJson?.Trim('"');
            if (string.IsNullOrEmpty(raw)) return;
            if (raw.Contains("\\\"")) raw = raw.Replace("\\\"", "\"").Replace("\\\\", "\\");

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            string type = root.GetProperty("type").GetString() ?? "";
            JsonElement data = root.TryGetProperty("data", out var d) ? d : default;

            switch (type)
            {
                case "seiware:script:toggle": HandleScriptToggle(data); break;
                case "seiware:script:add": HandleScriptAdd(); break;
                case "seiware:script:rename": HandleScriptRename(data); break;
                case "seiware:terminal:action": HandleTerminalAction(data); break;
                case "seiware:config:toggle": HandleConfigToggle(data); break;
                case "seiware:config:action": HandleConfigAction(data); break;
                case "seiware:config:path:save": cfg.Save(); break;
                case "seiware:config:paths:copy": HandlePathsCopy(); break;
                case "seiware:config:paths:saveall": cfg.Save(); break;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[Bridge] {ex.Message}"); }
    }

    void HandleScriptToggle(JsonElement data)
    {
        string id = data.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
        var existing = running.FirstOrDefault(r => r.Name == id && r.IsAlive);
        if (existing != null) { existing.Stop(); running.Remove(existing); return; }

        string scriptPath = id switch
        {
            "1" => cfg.WorkerScript,
            "2" => cfg.MemoryScript,
            "3" => cfg.LolScript,
            "4" => cfg.HideFilesScript,
            _ => cfg.CustomScripts.FirstOrDefault(s => s.ScriptPath.Contains(id))?.ScriptPath ?? "",
        };

        if (!string.IsNullOrEmpty(scriptPath) && File.Exists(scriptPath))
        {
            try
            {
                var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? ExeDirectory,
                    UseShellExecute = false, CreateNoWindow = true,
                });
                if (proc != null) running.Add(new RunningScript(id, proc));
            }
            catch { }
        }
    }

    void HandleScriptAdd()
    {
        cfg.CustomScripts.Add(new CustomScript { Name = $"Custom Script {cfg.CustomScripts.Count + 1}" });
        cfg.Save();
    }

    void HandleScriptRename(JsonElement data)
    {
        string name = data.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        string id = data.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
        bool isCustom = data.TryGetProperty("isCustom", out var c) && c.GetBoolean();
        if (isCustom)
            foreach (var s in cfg.CustomScripts)
                if (s.Name.Contains(id) || cfg.CustomScripts.IndexOf(s).ToString() == id)
                { s.Name = name; break; }
        cfg.Save();
    }

    void HandleTerminalAction(JsonElement data)
    {
        string action = data.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
        Debug.WriteLine($"[Terminal] {action}");
    }

    void HandleConfigToggle(JsonElement data)
    {
        string key = data.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
        bool value = data.TryGetProperty("value", out var v) && v.GetBoolean();
        switch (key)
        {
            case "intercept":
                if (value) { shellInterceptor?.Start(); if (interceptMenuItem != null) interceptMenuItem.Text = "◆ Intercept: ON"; }
                else { shellInterceptor?.Stop(); if (interceptMenuItem != null) interceptMenuItem.Text = "◇ Intercept: OFF"; }
                break;
            case "startWithWindows":
                cfg.StartWithWindows = value;
                cfg.ApplyStartWithWindows();
                break;
        }
        cfg.Save();
    }

    void HandleConfigAction(JsonElement data)
    {
        string action = data.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
        Invoke((Action)(() =>
        {
            switch (action)
            {
                case "setup_wizard":
                    var wiz = new SetupWizard();
                    if (wiz.ShowDialog() == DialogResult.OK)
                    { cfg = AppConfig.Load(); RebuildRegex(); CommandGuard.SetBannedNames(cfg.BannedNames); }
                    break;
                case "manage_banned":
                    var sf = new SettingsForm(cfg.BannedNames);
                    if (sf.ShowDialog() == DialogResult.OK)
                    { cfg.BannedNames = sf.BannedNames; cfg.Save(); RebuildRegex(); CommandGuard.SetBannedNames(cfg.BannedNames); }
                    break;
            }
        }));
    }

    void HandlePathsCopy()
    {
        try
        {
            var paths = cfg.GetAllPaths();
            string text = string.Join(Environment.NewLine, paths.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
            Invoke((Action)(() => Clipboard.SetText(text)));
        }
        catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            pipeCts?.Cancel();
            shellHost?.Dispose();
            trayIcon?.Dispose();
            shellInterceptor?.Dispose();
            webView?.Dispose();
        }
        base.Dispose(disposing);
    }
}
