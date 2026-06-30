// FakeTerminal.cs — Pipe-based fake terminal
// Shared code for BOTH "Command Prompt.exe" (Terminal.exe) AND "Windows PowerShell.exe"
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CommandPrompt
{
    public enum ShellType { Cmd, PowerShell }

    public sealed class FakeTerminal : IDisposable
    {
        const byte MSG_OUTPUT=0x01, MSG_CLEAR=0x02, MSG_SESSION_ENDED=0x03, MSG_CMD_FINISHED=0x04, MSG_COMMAND=0x10;

        [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr GetStdHandle(int n);
        [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern bool SetConsoleTitle(string t);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetConsoleMode(IntPtr h,uint m);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetConsoleMode(IntPtr h,out uint m);
        [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll",SetLastError=true)] static extern bool SetWindowDisplayAffinity(IntPtr h,uint a);
        [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
        [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern IntPtr LoadImage(IntPtr inst,string name,uint type,int cx,int cy,uint flags);

        readonly bool _admin;
        readonly ShellType _shellType;
        readonly string? _img;
        readonly Regex _censor;
        readonly string? _iconPath;
        readonly string _pipeName;
        NamedPipeClientStream? _pipe;
        BinaryReader? _pr;
        BinaryWriter? _pw;
        readonly object _lock = new();
        string _cwd;
        readonly StringBuilder _buf = new();
        int _cur;
        readonly List<string> _hist = new();
        int _hi = -1;
        string _saved = "";
        bool _run, _disposed, _wait;
        readonly CancellationTokenSource _cts = new();
        int _bannerSkip = 0;
        bool _lastOutputEndedWithNewline = true;

        bool IsPS => _shellType == ShellType.PowerShell;

        static readonly Regex CmdPromptStrip = new(@"[A-Za-z]:\\[^\r\n>]*>", RegexOptions.Compiled);
        static readonly Regex PsPromptStrip = new(@"PS [A-Za-z]:\\[^\r\n>]*>", RegexOptions.Compiled);
        static readonly Regex NeverMatch = new(@"(?!)", RegexOptions.Compiled);

        static bool IsCmdBanner(string t) { if(string.IsNullOrEmpty(t))return true; if(t.StartsWith("Microsoft Windows [Version",StringComparison.OrdinalIgnoreCase))return true; if(t.StartsWith("(c) Microsoft",StringComparison.OrdinalIgnoreCase))return true; return false; }
        static bool IsPsBanner(string t) { if(string.IsNullOrEmpty(t))return true; if(t.StartsWith("Windows PowerShell",StringComparison.OrdinalIgnoreCase))return true; if(t.StartsWith("PowerShell",StringComparison.OrdinalIgnoreCase))return true; if(t.StartsWith("Copyright",StringComparison.OrdinalIgnoreCase))return true; if(t.Contains("https://aka.ms",StringComparison.OrdinalIgnoreCase))return true; if(t.StartsWith("Loading personal",StringComparison.OrdinalIgnoreCase))return true; if(t.StartsWith("Try the new",StringComparison.OrdinalIgnoreCase))return true; return false; }
        bool IsBanner(string t) => IsPS ? IsPsBanner(t) : IsCmdBanner(t);

        public FakeTerminal(bool admin, ShellType shellType, string? img, Regex? censor, string? iconPath = null, string? pipeName = null)
        {
            _admin = admin; _shellType = shellType; _img = img;
            _censor = censor ?? NeverMatch; _iconPath = iconPath;
            _pipeName = pipeName ?? "SeiwareFakeTerminal";
            _cwd = admin ? @"C:\Windows\System32" : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        public int Run() { try { InitConsole(); return RunPipe(); } finally { Dispose(); } }

        // ═══ PIPE ════════════════════════════════════════════════════════
        int RunPipe()
        {
            if (!ConnPipe()) { Msg("Failed to connect to DreamLand backend.", true); Msg("Make sure DreamLand is running.", true); Msg(""); Msg("Press any key to exit..."); Console.ReadKey(true); return 1; }
            _bannerSkip = IsPS ? 5 : 3;
            Banner();
            var t = Task.Run(PipeRead);
            _run = true;
            InputLoop();
            _cts.Cancel();
            try { t.Wait(1000); } catch { }
            return 0;
        }

        bool ConnPipe() { try { _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous); _pipe.Connect(10000); _pr = new BinaryReader(_pipe, Encoding.UTF8, true); _pw = new BinaryWriter(_pipe, Encoding.UTF8, true); return _pipe.IsConnected; } catch { return false; } }

        void PipeSend(string cmd) { if (_pipe == null || !_pipe.IsConnected) return; try { byte[] d = Encoding.UTF8.GetBytes(cmd); lock (_pw!) { _pw.Write(MSG_COMMAND); _pw.Write(d.Length); _pw.Write(d); _pw.Flush(); } } catch { _run = false; } }

        void PipeRead()
        {
            try { while (_pipe != null && _pipe.IsConnected && !_cts.Token.IsCancellationRequested) { byte mt; try { mt = _pr!.ReadByte(); } catch { break; }
                switch (mt) { case MSG_OUTPUT: int l = _pr.ReadInt32(); byte[] d = _pr.ReadBytes(l); PipeOutput(Encoding.UTF8.GetString(d)); break;
                    case MSG_CLEAR: Console.Clear(); _lastOutputEndedWithNewline = true; break;
                    case MSG_SESSION_ENDED: Out("\r\n[Session ended]\r\n"); _run = false; break;
                    case MSG_CMD_FINISHED: if (_wait) { _wait = false; Prompt(); } break; } } } catch { } _run = false;
        }

        void PipeOutput(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_bannerSkip > 0) { var sb = new StringBuilder(); foreach (var ln in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)) { if (_bannerSkip > 0 && IsBanner(ln.Trim())) { _bannerSkip--; continue; } if (sb.Length > 0) sb.Append("\r\n"); sb.Append(ln); } text = sb.ToString(); if (string.IsNullOrEmpty(text)) return; }
            text = CmdPromptStrip.Replace(text, "");
            text = PsPromptStrip.Replace(text, "");
            if (!string.IsNullOrEmpty(text)) { Out(text); _lastOutputEndedWithNewline = text.EndsWith("\n") || text.EndsWith("\r\n"); }
        }

        // ═══ CONSOLE ═════════════════════════════════════════════════════
        void InitConsole()
        {
            try { Console.OutputEncoding = Encoding.UTF8; Console.InputEncoding = Encoding.UTF8; Console.BackgroundColor = ConsoleColor.Black; Console.ForegroundColor = IsPS ? ConsoleColor.White : ConsoleColor.Gray; Console.Clear();
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { try { Console.BufferWidth=120;Console.BufferHeight=9001;Console.WindowWidth=Math.Min(120,Console.LargestWindowWidth);Console.WindowHeight=Math.Min(30,Console.LargestWindowHeight); } catch { }
                    try { var h=GetStdHandle(-11);if(GetConsoleMode(h,out uint m))SetConsoleMode(h,m|0x0004); } catch { }
                    try { var w=GetConsoleWindow();if(w!=IntPtr.Zero){SetWindowDisplayAffinity(w,0x11);SetIcon(w);} } catch { } }
                string title = IsPS ? (_admin ? "Administrator: Windows PowerShell" : "Windows PowerShell") : (_admin ? "Administrator: Command Prompt" : "Command Prompt");
                SetConsoleTitle(title); Console.CursorVisible = true; Console.CancelKeyPress += (s, e) => { e.Cancel = true; CtrlC(); };
            } catch { }
        }

        void SetIcon(IntPtr hwnd)
        {
            try { string ico = "";
                if (!string.IsNullOrEmpty(_iconPath) && File.Exists(_iconPath)) { ico = _iconPath; }
                else { string exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "";
                    // CMD (including admin) uses cmdterminal.ico, PowerShell uses powershell.ico
                    string icoName = IsPS ? "powershell.ico" : "cmdterminal.ico";
                    ico = Path.Combine(exeDir, icoName);
                    if (!File.Exists(ico)) ico = Path.Combine(exeDir, "terminal.ico");
                    if (!File.Exists(ico)) ico = Path.Combine(AppContext.BaseDirectory, icoName);
                    if (!File.Exists(ico)) ico = Path.Combine(AppContext.BaseDirectory, "terminal.ico"); }
                if (!File.Exists(ico)) return;
                IntPtr h = LoadImage(IntPtr.Zero, ico, 1, 0, 0, 0x10);
                if (h != IntPtr.Zero) { SendMessage(hwnd, 0x80, IntPtr.Zero, h); SendMessage(hwnd, 0x80, (IntPtr)1, h); }
            } catch { }
        }

        void Banner()
        {
            Console.ForegroundColor = IsPS ? ConsoleColor.White : ConsoleColor.Gray;
            if (IsPS) { Console.WriteLine("Windows PowerShell"); Console.WriteLine("Copyright (C) Microsoft Corporation. All rights reserved."); Console.WriteLine(); Console.WriteLine("Install the latest PowerShell for new features and improvements! https://aka.ms/PSWindows"); Console.WriteLine(); }
            else { Console.WriteLine("Microsoft Windows [Version 10.0.26200.8457]"); Console.WriteLine("(c) Microsoft Corporation. All rights reserved."); Console.WriteLine(); }
            Prompt();
        }

        void Prompt() { if (_wait) return; if (!_lastOutputEndedWithNewline) { Console.WriteLine(); _lastOutputEndedWithNewline = true; } Console.ForegroundColor = IsPS ? ConsoleColor.White : ConsoleColor.Gray; Console.Write(PS()); _buf.Clear(); _cur = 0; _hi = -1; }
        string PS() => IsPS ? $"PS {_cwd}> " : $"{_cwd}>";
        void Out(string t) { lock (_lock) { Console.ForegroundColor = IsPS ? ConsoleColor.White : ConsoleColor.Gray; Console.Write(t); } }
        void Msg(string t, bool err = false) { Console.ForegroundColor = err ? ConsoleColor.Red : (IsPS ? ConsoleColor.White : ConsoleColor.Gray); Console.WriteLine(t); }

        // ═══ INPUT ═══════════════════════════════════════════════════════
        void InputLoop() { while (_run) { if (!Console.KeyAvailable) { Thread.Sleep(10); continue; } Key(Console.ReadKey(true)); } }

        void Key(ConsoleKeyInfo k)
        {
            if (_wait) { if (k.Key == ConsoleKey.C && (k.Modifiers & ConsoleModifiers.Control) != 0) CtrlC(); return; }
            switch (k.Key) { case ConsoleKey.Enter: Enter(); break;
                case ConsoleKey.Backspace: if (_cur > 0) { _buf.Remove(_cur-1,1); _cur--; Redraw(); } break;
                case ConsoleKey.Delete: if (_cur < _buf.Length) { _buf.Remove(_cur,1); Redraw(); } break;
                case ConsoleKey.LeftArrow: if ((k.Modifiers&ConsoleModifiers.Control)!=0) WL(); else if (_cur > 0){_cur--;UCur();} break;
                case ConsoleKey.RightArrow: if ((k.Modifiers&ConsoleModifiers.Control)!=0) WR(); else if (_cur < _buf.Length){_cur++;UCur();} break;
                case ConsoleKey.Home: _cur=0;UCur(); break; case ConsoleKey.End: _cur=_buf.Length;UCur(); break;
                case ConsoleKey.UpArrow: Hist(true); break; case ConsoleKey.DownArrow: Hist(false); break;
                case ConsoleKey.Escape: ClrIn(); break;
                case ConsoleKey.L: if ((k.Modifiers&ConsoleModifiers.Control)!=0){Console.Clear();_lastOutputEndedWithNewline=true;Prompt();}else Ins(k.KeyChar); break;
                default: if (k.KeyChar >= ' ') Ins(k.KeyChar); break; }
        }

        void Enter()
        {
            string cmd = _buf.ToString(); Console.WriteLine();
            if (!string.IsNullOrWhiteSpace(cmd)) { if (_hist.Count==0||_hist[^1]!=cmd)_hist.Add(cmd); if (_hist.Count>500)_hist.RemoveAt(0); }
            if (cmd.Trim().Equals("exit",StringComparison.OrdinalIgnoreCase)) { _run=false; return; }
            string cn = cmd.Trim().Split(' ',2)[0].ToLowerInvariant();
            if (cn=="cd"||cn=="chdir"||cn=="set-location"||cn=="sl"||cn=="pushd") { var p=cmd.Trim().Split(' ',2); if(p.Length>=2) UpdDir(p[1].Trim().Trim('"')); }
            Submit(cmd); _buf.Clear(); _cur=0;
        }

        void Submit(string cmd) { _wait = true; string tc = cmd.Trim().ToLowerInvariant(); if (tc=="cls"||tc=="clear"||tc=="clear-host") { Console.Clear(); _lastOutputEndedWithNewline=true; _wait=false; Prompt(); } PipeSend(cmd); }

        void UpdDir(string t) { try { if(t=="..")_cwd=Path.GetDirectoryName(_cwd)??""; else if(t=="\\"||t=="/")_cwd=Path.GetPathRoot(_cwd)??""; else if(Path.IsPathRooted(t))_cwd=t; else _cwd=Path.Combine(_cwd,t); _cwd=Path.GetFullPath(_cwd); } catch{} }

        void Ins(char c) { _buf.Insert(_cur,c);_cur++;if(_cur==_buf.Length)Console.Write(c);else Redraw(); }
        void WL() { if(_cur<=0)return;int p=_cur-1;while(p>0&&_buf[p]==' ')p--;while(p>0&&_buf[p-1]!=' ')p--;_cur=p;UCur(); }
        void WR() { if(_cur>=_buf.Length)return;int p=_cur;while(p<_buf.Length&&_buf[p]==' ')p++;while(p<_buf.Length&&_buf[p]!=' ')p++;_cur=p;UCur(); }
        void UCur() { int pl=PS().Length;try{Console.SetCursorPosition(pl+_cur,Console.CursorTop);}catch{} }
        void Hist(bool up) { if(_hist.Count==0)return; if(up){if(_hi==-1){_saved=_buf.ToString();_hi=_hist.Count;}if(_hi>0)_hi--;SetIn(_hist[_hi]);}else{if(_hi==-1)return;if(_hi<_hist.Count-1){_hi++;SetIn(_hist[_hi]);}else{_hi=-1;SetIn(_saved);}} }
        void SetIn(string t) { ClrVis();_buf.Clear();_buf.Append(t);_cur=t.Length;Console.Write(t); }
        void ClrIn() { ClrVis();_buf.Clear();_cur=0; }
        void ClrVis() { int pl=PS().Length;try{Console.SetCursorPosition(pl,Console.CursorTop);Console.Write(new string(' ',_buf.Length+2));Console.SetCursorPosition(pl,Console.CursorTop);}catch{} }
        void Redraw() { int pl=PS().Length;try{Console.SetCursorPosition(pl,Console.CursorTop);Console.Write(_buf.ToString()+" ");Console.SetCursorPosition(pl+_cur,Console.CursorTop);}catch{} }

        void CtrlC() { Console.ForegroundColor = IsPS ? ConsoleColor.White : ConsoleColor.Gray; Console.Write("^C"); Console.WriteLine(); _lastOutputEndedWithNewline = true; PipeSend("\x03"); _buf.Clear(); _cur=0; _wait=false; Prompt(); }

        public void Dispose() { if (_disposed) return; _disposed=true; _run=false; _cts.Cancel(); try{_pr?.Close();}catch{} try{_pw?.Close();}catch{} try{_pipe?.Close();}catch{} _pr=null;_pw=null;_pipe=null; try{Console.ResetColor();Console.CursorVisible=true;}catch{} _cts.Dispose(); }
    }
}
