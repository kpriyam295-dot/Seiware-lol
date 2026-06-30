// Seiware.UI.cs — HeadlessShellHost, FakeTerminalSession, CmdTerminal, MainForm, SettingsForm
// ORIGINAL working logic preserved. UI redesigned with gradients + modern look.
using System; using System.Collections.Generic; using System.Diagnostics; using System.Drawing; using System.Drawing.Drawing2D; using System.IO; using System.IO.Pipes; using System.Linq; using System.Runtime.InteropServices; using System.Text; using System.Text.Json; using System.Text.RegularExpressions; using System.Threading; using System.Threading.Tasks; using System.Windows.Forms; using System.Management;

// ════════════════════════════════════════════════════════════════════════════
// HEADLESS SHELL HOST — ORIGINAL, UNCHANGED
// ════════════════════════════════════════════════════════════════════════════
class HeadlessShellHost : IDisposable
{
    private Process shell;private bool shellRunning,disposed;private readonly string sentinel="__SEIWARE_DONE_"+Guid.NewGuid().ToString("N")+"__";
    private Regex censorRegex;private int bannerSkipCount;private readonly ShellType shellType;
    private string currentDir=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);private string pendingSanitizePath;
    public int ShellPid=>shell!=null&&!shell.HasExited?shell.Id:-1;
    public event Action<string> OutputChunk;public event Action CommandFinished;public event Action ScreenCleared;
    static readonly Regex AnsiStrip=new(@"\x1b(\[[0-9;]*[A-Za-z]|\][^\x07]*\x07|\[=[0-9]+[hI])",RegexOptions.Compiled);
    static readonly Regex CmdPromptStrip=new(@"[A-Za-z]:\\[^\r\n>]*>",RegexOptions.Compiled);
    static readonly Regex PsPromptStrip=new(@"PS [A-Za-z]:\\[^\r\n>]*>",RegexOptions.Compiled);
    public HeadlessShellHost(ShellType shellType,Regex censor){this.shellType=shellType;this.censorRegex=censor??new Regex("(?!)");this.bannerSkipCount=shellType==ShellType.PowerShell?5:3;}
    public void SetCensorRegex(Regex r)=>censorRegex=r??new Regex("(?!)");
    static bool IsCmdBanner(string t){if(string.IsNullOrEmpty(t))return true;if(t.StartsWith("Microsoft Windows [Version",StringComparison.OrdinalIgnoreCase))return true;if(t.StartsWith("(c) Microsoft",StringComparison.OrdinalIgnoreCase))return true;return false;}
    static bool IsPsBanner(string t){if(string.IsNullOrEmpty(t))return true;if(t.StartsWith("Windows PowerShell",StringComparison.OrdinalIgnoreCase))return true;if(t.StartsWith("PowerShell",StringComparison.OrdinalIgnoreCase))return true;if(t.StartsWith("Copyright",StringComparison.OrdinalIgnoreCase))return true;if(t.Contains("https://aka.ms",StringComparison.OrdinalIgnoreCase))return true;if(t.StartsWith("Loading personal",StringComparison.OrdinalIgnoreCase))return true;return false;}
    bool IsBanner(string t)=>shellType==ShellType.PowerShell?IsPsBanner(t):IsCmdBanner(t);
    void PowerShellSetExitCode(int code){try{if(shell==null||shell.HasExited)return;shell.StandardInput.WriteLine($"$global:LASTEXITCODE = {code}");shell.StandardInput.Flush();}catch{}}
    public bool Start(string workingDir){Stop();currentDir=workingDir;pendingSanitizePath=null;try{var psi=new ProcessStartInfo{WorkingDirectory=workingDir,UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};if(shellType==ShellType.PowerShell){psi.FileName="powershell.exe";psi.Arguments="-NoLogo -NoProfile -ExecutionPolicy Bypass";}else{psi.FileName="cmd.exe";psi.Arguments="/Q /A";}shell=Process.Start(psi);if(shell==null)return false;shellRunning=true;if(shellType==ShellType.Cmd){shell.StandardInput.WriteLine("@echo off");shell.StandardInput.WriteLine("prompt $P$G");shell.StandardInput.Flush();}Task.Run(()=>ReadStream(shell.StandardOutput));Task.Run(()=>{try{shell.WaitForExit();}catch{}shellRunning=false;});return true;}catch{return false;}}
    public void SendCommand(string input){if(!shellRunning||shell==null){CommandFinished?.Invoke();return;}if(input.Trim().Equals("exit",StringComparison.OrdinalIgnoreCase)){Stop();return;}if(CommandGuard.ShouldBlock(input)){string failOut=FailureEmulator.BuildOutput(shellType,input);if(!string.IsNullOrEmpty(failOut)){string normalized=failOut.Replace("\r\n","\n").Replace("\n","\r\n");if(!normalized.EndsWith("\r\n"))normalized+="\r\n";OutputChunk?.Invoke(normalized);}if(shellType==ShellType.PowerShell)PowerShellSetExitCode(1);CommandFinished?.Invoke();return;}string tc=input.Trim().ToLowerInvariant();if(tc=="cls"||tc=="clear"||tc=="clear-host"){ScreenCleared?.Invoke();CommandFinished?.Invoke();return;}pendingSanitizePath=StorageUtil.TryGetRedirectTarget(input,currentDir);string openedFile=StorageUtil.TryGetOpenedFileTarget(input,currentDir);if(!string.IsNullOrWhiteSpace(openedFile))StorageUtil.SanitizeFileEventually(openedFile,censorRegex);try{var parts=input.Trim().Split(' ',2,StringSplitOptions.RemoveEmptyEntries);if(parts.Length>0){string cmd=parts[0].ToLowerInvariant();if((cmd=="cd"||cmd=="chdir"||cmd=="set-location"||cmd=="sl"||cmd=="pushd")&&parts.Length>1){string t=parts[1].Trim().Trim('"');if(t=="..")currentDir=Path.GetDirectoryName(currentDir)??currentDir;else if(t=="\\"||t=="/")currentDir=Path.GetPathRoot(currentDir)??currentDir;else if(Path.IsPathRooted(t))currentDir=Path.GetFullPath(t);else currentDir=Path.GetFullPath(Path.Combine(currentDir,t));}}}catch{}try{if(shellType==ShellType.Cmd&&!input.Contains("2>&1"))shell.StandardInput.WriteLine(input+" 2>&1");else shell.StandardInput.WriteLine(input);if(shellType==ShellType.PowerShell)shell.StandardInput.WriteLine($"Write-Host '{sentinel}'");else shell.StandardInput.WriteLine("echo "+sentinel);shell.StandardInput.Flush();}catch{CommandFinished?.Invoke();}}
    public void SendCtrlC(){try{if(shell!=null&&!shell.HasExited){NativeWin32.AttachConsole((uint)shell.Id);NativeWin32.SetConsoleCtrlHandler(IntPtr.Zero,true);NativeWin32.GenerateConsoleCtrlEvent(0,0);Thread.Sleep(100);NativeWin32.FreeConsole();NativeWin32.SetConsoleCtrlHandler(IntPtr.Zero,false);}}catch{}}
    void ReadStream(StreamReader reader){try{var buf=new StringBuilder();int ch;while((ch=reader.Read())!=-1){buf.Append((char)ch);if((char)ch=='\n'||buf.Length>512){ProcessChunk(buf.ToString());buf.Clear();}}if(buf.Length>0)ProcessChunk(buf.ToString());}catch{}}
    void ProcessChunk(string raw){string clean=AnsiStrip.Replace(raw,"");clean=clean.Replace("\r\n","\n").Replace("\r","\n").Replace("\n","\r\n");bool finished=false;if(clean.Contains(sentinel)){finished=true;if(shellType==ShellType.PowerShell)clean=clean.Replace($"Write-Host '{sentinel}'","");else clean=clean.Replace("echo "+sentinel,"");clean=clean.Replace(sentinel,"");}if(!string.IsNullOrEmpty(clean)&&clean.Trim().Length>0){if(bannerSkipCount>0&&IsBanner(clean.Trim())){bannerSkipCount--;}else{bannerSkipCount=0;string relay=CmdPromptStrip.Replace(clean,"");relay=PsPromptStrip.Replace(relay,"");if(!string.IsNullOrWhiteSpace(relay)){if(!relay.EndsWith("\r\n")&&!relay.EndsWith("\n"))relay+="\r\n";OutputChunk?.Invoke(relay);}}}if(finished){StorageUtil.SanitizeFileEventually(pendingSanitizePath,censorRegex);pendingSanitizePath=null;CommandFinished?.Invoke();}}
    public void Stop(){try{if(shell!=null&&!shell.HasExited){shell.StandardInput.Close();shell.Kill();}}catch{}shell=null;shellRunning=false;}
    public void Dispose(){if(disposed)return;disposed=true;Stop();}
}

// ════════════════════════════════════════════════════════════════════════════
// FAKE TERMINAL SESSION — ORIGINAL, UNCHANGED
// ════════════════════════════════════════════════════════════════════════════
class FakeTerminalSession : IDisposable
{
    public const byte MSG_OUTPUT=0x01,MSG_CLEAR=0x02,MSG_SESSION_ENDED=0x03,MSG_CMD_FINISHED=0x04,MSG_COMMAND=0x10;
    private readonly string pipeName;private NamedPipeServerStream pipeServer;private BinaryReader pipeReader;private BinaryWriter pipeWriter;
    private Process fakeTermProc;private Thread pipeAcceptThread;private bool connected=false,disposed=false;
    public event Action<string> CommandReceived;public event Action CtrlCReceived;public event Action FakeTermClosed;
    public bool IsConnected=>connected&&pipeServer!=null&&pipeServer.IsConnected;public bool IsRunning=>fakeTermProc!=null&&!fakeTermProc.HasExited;
    public int FakeTermPid=>fakeTermProc!=null&&!fakeTermProc.HasExited?fakeTermProc.Id:-1;
    public FakeTerminalSession(string pipeName=null){this.pipeName=pipeName??"SeiwareFakeTerminal";}
    static bool IsRunningElevated(){try{using var id=System.Security.Principal.WindowsIdentity.GetCurrent();return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);}catch{return false;}}
    public void Start(string fakeTermExePath,string workingDir,string launcherExePath=null,bool elevated=false,string extraArgs="")
    {Dispose();disposed=false;var pipeSec=new System.IO.Pipes.PipeSecurity();pipeSec.AddAccessRule(new System.IO.Pipes.PipeAccessRule(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid,null),System.IO.Pipes.PipeAccessRights.FullControl,System.Security.AccessControl.AccessControlType.Allow));pipeServer=NamedPipeServerStreamAcl.Create(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,0,0,pipeSec);pipeAcceptThread=new Thread(()=>{try{pipeServer.WaitForConnection();pipeReader=new BinaryReader(pipeServer,Encoding.UTF8,true);pipeWriter=new BinaryWriter(pipeServer,Encoding.UTF8,true);connected=true;ReadLoop();}catch{connected=false;}}){IsBackground=true,Name="PipeAccept_"+pipeName};pipeAcceptThread.Start();string args=(extraArgs??"").Trim();if(pipeName!="SeiwareFakeTerminal")args=(args+$" --pipe \"{pipeName}\"").Trim();string exeDir=Path.GetDirectoryName(fakeTermExePath)??workingDir;
    try{string launcher=launcherExePath??Path.Combine(exeDir,"SeiwareLauncher.exe");if(!StorageUtil.FileExists(launcher))launcher=Path.Combine(exeDir,"DreamLandLauncher.exe");string exeBase=Path.GetFileNameWithoutExtension(fakeTermExePath).ToLowerInvariant();bool isPS=exeBase.Contains("powershell")||exeBase.Contains("pwsh");string title=isPS?(elevated?"Administrator: Windows PowerShell":"Windows PowerShell"):(elevated?"Administrator: Command Prompt":"Command Prompt");
        if(StorageUtil.FileExists(launcher)){string uid="sw_"+Guid.NewGuid().ToString("N").Substring(0,8);string tempLauncher=Path.Combine(Path.GetTempPath(),uid+".exe");string targetFile=tempLauncher+".target";File.Copy(launcher,tempLauncher,true);File.WriteAllText(targetFile,fakeTermExePath+"\n"+args+"\n"+title);
            if(elevated)Process.Start(new ProcessStartInfo{FileName=tempLauncher,Verb="runas",UseShellExecute=true,WorkingDirectory=Path.GetTempPath()});
            else if(IsRunningElevated())Process.Start("explorer.exe",$"\"{tempLauncher}\"");
            else Process.Start(new ProcessStartInfo{FileName=tempLauncher,UseShellExecute=true,WorkingDirectory=Path.GetTempPath()});
            Task.Run(async()=>{await Task.Delay(15000);try{File.Delete(tempLauncher);}catch{}try{File.Delete(targetFile);}catch{}});}
        else{fakeTermProc=Process.Start(new ProcessStartInfo{FileName=fakeTermExePath,Arguments=args,UseShellExecute=true,WorkingDirectory=exeDir,Verb=elevated?"runas":""});}
        if(fakeTermProc!=null){fakeTermProc.EnableRaisingEvents=true;fakeTermProc.Exited+=(s,e)=>{connected=false;FakeTermClosed?.Invoke();};}
    }catch(Exception ex){throw new Exception($"Could not launch: {ex.Message}",ex);}}
    public void SendOutput(string text){if(!IsConnected||pipeWriter==null)return;try{byte[]d=Encoding.UTF8.GetBytes(text);lock(pipeWriter){pipeWriter.Write(MSG_OUTPUT);pipeWriter.Write(d.Length);pipeWriter.Write(d);pipeWriter.Flush();}}catch{connected=false;}}
    public void SendClear(){if(!IsConnected||pipeWriter==null)return;try{lock(pipeWriter){pipeWriter.Write(MSG_CLEAR);pipeWriter.Flush();}}catch{}}
    public void SendSessionEnded(){if(!IsConnected||pipeWriter==null)return;try{lock(pipeWriter){pipeWriter.Write(MSG_SESSION_ENDED);pipeWriter.Flush();}}catch{}}
    public void SendCommandFinished(){if(!IsConnected||pipeWriter==null)return;try{lock(pipeWriter){pipeWriter.Write(MSG_CMD_FINISHED);pipeWriter.Flush();}}catch{}}
    private void ReadLoop(){try{while(pipeServer!=null&&pipeServer.IsConnected&&!disposed&&pipeReader!=null){byte mt=pipeReader.ReadByte();if(mt==MSG_COMMAND){int len=pipeReader.ReadInt32();byte[]d=pipeReader.ReadBytes(len);string cmd=Encoding.UTF8.GetString(d);if(cmd=="\x03")CtrlCReceived?.Invoke();else CommandReceived?.Invoke(cmd);}}}catch{}connected=false;FakeTermClosed?.Invoke();}
    public void Dispose(){if(disposed)return;disposed=true;connected=false;try{fakeTermProc?.Kill();}catch{}try{pipeServer?.Close();}catch{}fakeTermProc=null;pipeServer=null;pipeReader=null;pipeWriter=null;}
}

// ════════════════════════════════════════════════════════════════════════════
// CMD TERMINAL CONTROL — ORIGINAL, UNCHANGED
// ════════════════════════════════════════════════════════════════════════════
class CmdTerminal : RichTextBox
{
    Process shell;string workDir;bool shellRunning;int inputStart=-1;List<string>history=new();int historyPos=-1;Regex censorRegex;
    public int ShellPid=>shell!=null&&!shell.HasExited?shell.Id:-1;
    readonly string sentinel="__SEIWARE_DONE_"+Guid.NewGuid().ToString("N")+"__";int _bannerSkipCount=0;string _pendingSanitizePath;
    public event Action<string> OutputChunk;public event Action ScreenCleared;public event Action CommandFinished;
    static readonly Color BG=Color.FromArgb(12,12,12),FG=Color.FromArgb(204,204,204),FG_ERR=Color.FromArgb(255,140,60),FG_PROMPT=Color.FromArgb(204,204,204),FG_INPUT=Color.FromArgb(255,255,255);
    static readonly Regex AnsiStrip=new(@"\x1b(\[[0-9;]*[A-Za-z]|\][^\x07]*\x07|\[=[0-9]+[hI])",RegexOptions.Compiled);
    static readonly Regex PromptStrip=new(@"[A-Za-z]:\\[^\r\n>]*>",RegexOptions.Compiled);
    static readonly Regex PromptDetect=new(@"^([A-Za-z]:\\[^\r\n>]*)>",RegexOptions.Multiline);
    public CmdTerminal(){BackColor=BG;ForeColor=FG;Font=new Font("Consolas",10f);WordWrap=false;ScrollBars=RichTextBoxScrollBars.Both;BorderStyle=BorderStyle.None;ReadOnly=false;ShortcutsEnabled=true;workDir=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);censorRegex=new Regex("(?!)");}
    public void SetCensorRegex(Regex r)=>censorRegex=r??new Regex("(?!)");
    static bool IsBannerLine(string t){if(string.IsNullOrEmpty(t))return true;if(t.StartsWith("Microsoft Windows [Version",StringComparison.OrdinalIgnoreCase))return true;if(t.StartsWith("(c) Microsoft Corporation",StringComparison.OrdinalIgnoreCase))return true;return false;}
    public void StartShell(){StopShell();var psi=new ProcessStartInfo{FileName="cmd.exe",Arguments="/Q /A",WorkingDirectory=workDir,UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};try{shell=Process.Start(psi);}catch(Exception ex){AppendText($"\r\n[Error: {ex.Message}]\r\n",FG_ERR);return;}if(shell==null){AppendText("\r\n[Error: shell is null]\r\n",FG_ERR);return;}shellRunning=true;_bannerSkipCount=3;shell.StandardInput.WriteLine("@echo off");shell.StandardInput.Flush();AppendText("Microsoft Windows [Embedded Terminal]\r\nType 'exit' to close.\r\n\r\n",FG);ShowPrompt();Task.Run(()=>ReadStream(shell.StandardOutput,false));Task.Run(()=>ReadStream(shell.StandardError,true));Task.Run(()=>{try{shell.WaitForExit();}catch{}shellRunning=false;if(!IsDisposed)BeginInvoke((Action)(()=>AppendText("\r\n[Shell exited]\r\n",FG_ERR)));});}
    public void StopShell(){try{if(shell!=null&&!shell.HasExited){shell.StandardInput.Close();shell.Kill();}}catch{}shell=null;shellRunning=false;inputStart=-1;}
    public void SendCommand(string input){if(!shellRunning||shell==null){CommandFinished?.Invoke();return;}BeginInvoke((Action)(()=>{if(inputStart>=0&&TextLength>inputStart){Select(inputStart,TextLength-inputStart);SelectedText="";}SelectionColor=FG_INPUT;AppendText(input);AppendText("\r\n");inputStart=-1;}));if(input.Trim().Length>0){history.Insert(0,input);if(history.Count>200)history.RemoveAt(history.Count-1);}if(input.Trim().Equals("exit",StringComparison.OrdinalIgnoreCase)){StopShell();BeginInvoke((Action)(()=>AppendText("[Session ended.]\r\n",FG_ERR)));return;}if(CommandGuard.ShouldBlock(input)){string failOut=FailureEmulator.BuildOutput(ShellType.Cmd,input);if(!string.IsNullOrEmpty(failOut)){string n=failOut.Replace("\r\n","\n").Replace("\n","\r\n");if(!n.EndsWith("\r\n"))n+="\r\n";OutputChunk?.Invoke(n);}CommandFinished?.Invoke();return;}if(input.Trim().Equals("cls",StringComparison.OrdinalIgnoreCase)){BeginInvoke((Action)(()=>ClearScreen()));return;}_pendingSanitizePath=StorageUtil.TryGetRedirectTarget(input,workDir);var _openFile=StorageUtil.TryGetOpenedFileTarget(input,workDir);if(!string.IsNullOrWhiteSpace(_openFile))StorageUtil.SanitizeFileEventually(_openFile,censorRegex);try{shell.StandardInput.WriteLine(input);shell.StandardInput.WriteLine("echo "+sentinel);shell.StandardInput.Flush();}catch{BeginInvoke((Action)(()=>AppendText("[Shell not running]\r\n",FG_ERR)));CommandFinished?.Invoke();}}
    public void ExternalCtrlC(){try{if(shell!=null&&!shell.HasExited){NativeWin32.AttachConsole((uint)shell.Id);NativeWin32.SetConsoleCtrlHandler(IntPtr.Zero,true);NativeWin32.GenerateConsoleCtrlEvent(0,0);Thread.Sleep(100);NativeWin32.FreeConsole();NativeWin32.SetConsoleCtrlHandler(IntPtr.Zero,false);}}catch{}if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)(()=>{AppendText("^C\r\n",FG_ERR);inputStart=-1;ShowPrompt();}));}
    void ReadStream(StreamReader reader,bool isError){try{int ch;var buf=new StringBuilder();while((ch=reader.Read())!=-1){buf.Append((char)ch);if((char)ch=='\n'||buf.Length>512){ProcessChunk(buf.ToString(),isError);buf.Clear();}}if(buf.Length>0)ProcessChunk(buf.ToString(),isError);}catch{}}
    void ProcessChunk(string raw,bool isError){string clean=AnsiStrip.Replace(raw,"");clean=clean.Replace("\r\n","\n").Replace("\r","\n").Replace("\n","\r\n");bool finished=false;if(clean.Contains(sentinel)){finished=true;clean=clean.Replace("echo "+sentinel,"");clean=clean.Replace(sentinel,"");}if(!IsDisposed){string toSend=clean;BeginInvoke((Action)(()=>{TryParsePrompt(toSend);AppendTextBeforeInput(toSend,isError?FG_ERR:FG);if(!string.IsNullOrEmpty(toSend)&&toSend.Trim().Length>0){if(_bannerSkipCount>0&&IsBannerLine(toSend.Trim())){_bannerSkipCount--;}else{_bannerSkipCount=0;string relay=PromptStrip.Replace(toSend,"");if(!string.IsNullOrWhiteSpace(relay)){if(!relay.EndsWith("\r\n")&&!relay.EndsWith("\n"))relay+="\r\n";OutputChunk?.Invoke(relay);}}}if(finished){StorageUtil.SanitizeFileEventually(_pendingSanitizePath,censorRegex);_pendingSanitizePath=null;if(inputStart<0)ShowPrompt();CommandFinished?.Invoke();}}));}}
    void TryParsePrompt(string text){var m=PromptDetect.Match(text);if(m.Success){string c=m.Groups[1].Value.Trim();if(Directory.Exists(c))workDir=c;}}
    void AppendText(string text,Color col){SelectionStart=TextLength;SelectionLength=0;SelectionColor=col;base.AppendText(text);ScrollToCaret();}
    void AppendTextBeforeInput(string text,Color col){if(string.IsNullOrEmpty(text))return;if(inputStart<0){AppendText(text,col);return;}string ci=TextLength>inputStart?Text.Substring(inputStart):"";Select(inputStart,TextLength-inputStart);SelectedText="";AppendText(text,col);inputStart=TextLength;SelectionColor=FG_INPUT;base.AppendText(ci);Select(TextLength,0);ScrollToCaret();}
    void ShowPrompt(){if(IsDisposed)return;AppendText($"{workDir}>",FG_PROMPT);inputStart=TextLength;historyPos=-1;}
    protected override bool IsInputKey(Keys k){if(k==Keys.Up||k==Keys.Down||k==Keys.Left||k==Keys.Right||k==Keys.Tab)return true;return base.IsInputKey(k);}
    protected override void OnKeyDown(KeyEventArgs e){switch(e.KeyCode){case Keys.Enter:e.SuppressKeyPress=true;SendCurrentLine();return;case Keys.Back:if(SelectionStart<=inputStart&&SelectionLength==0){e.SuppressKeyPress=true;return;}break;case Keys.Up:e.SuppressKeyPress=true;NavHist(+1);return;case Keys.Down:e.SuppressKeyPress=true;NavHist(-1);return;case Keys.Left:case Keys.Home:if(SelectionStart<=inputStart){e.SuppressKeyPress=true;return;}break;case Keys.C when e.Control:if(SelectionLength==0){e.SuppressKeyPress=true;SendCtrlC();return;}break;case Keys.L when e.Control:e.SuppressKeyPress=true;ClearScreen();return;}if(SelectionStart<inputStart&&e.KeyCode!=Keys.C&&e.KeyCode!=Keys.A)Select(TextLength,0);base.OnKeyDown(e);}
    protected override void OnKeyPress(KeyPressEventArgs e){if(SelectionStart<inputStart)Select(TextLength,0);base.OnKeyPress(e);}
    void SendCurrentLine(){if(!shellRunning||shell==null||inputStart<0)return;string input=TextLength>inputStart?Text.Substring(inputStart).TrimEnd('\r','\n'):"";SelectionColor=FG_INPUT;AppendText("\r\n");inputStart=-1;if(input.Trim().Length>0){history.Insert(0,input);if(history.Count>200)history.RemoveAt(history.Count-1);}historyPos=-1;if(input.Trim().Equals("exit",StringComparison.OrdinalIgnoreCase)){StopShell();AppendText("[Session ended.]\r\n",FG_ERR);return;}if(CommandGuard.ShouldBlock(input)){string failOut=FailureEmulator.BuildOutput(ShellType.Cmd,input);if(!string.IsNullOrEmpty(failOut)){string n=failOut.Replace("\r\n","\n").Replace("\n","\r\n");if(!n.EndsWith("\r\n"))n+="\r\n";OutputChunk?.Invoke(n);}ShowPrompt();CommandFinished?.Invoke();return;}if(input.Trim().Equals("cls",StringComparison.OrdinalIgnoreCase)){ClearScreen();return;}_pendingSanitizePath=StorageUtil.TryGetRedirectTarget(input,workDir);var _openFile=StorageUtil.TryGetOpenedFileTarget(input,workDir);if(!string.IsNullOrWhiteSpace(_openFile))StorageUtil.SanitizeFileEventually(_openFile,censorRegex);try{shell.StandardInput.WriteLine(input);shell.StandardInput.WriteLine("echo "+sentinel);shell.StandardInput.Flush();}catch{AppendText("[Shell not running]\r\n",FG_ERR);ShowPrompt();}}
    void NavHist(int dir){if(history.Count==0)return;historyPos=Math.Max(-1,Math.Min(history.Count-1,historyPos+dir));string t=historyPos>=0?history[historyPos]:"";if(inputStart>=0){Select(inputStart,TextLength-inputStart);SelectionColor=FG_INPUT;SelectedText=t;Select(TextLength,0);}}
    void SendCtrlC(){try{if(shell!=null&&!shell.HasExited){NativeWin32.AttachConsole((uint)shell.Id);NativeWin32.SetConsoleCtrlHandler(IntPtr.Zero,true);NativeWin32.GenerateConsoleCtrlEvent(0,0);Thread.Sleep(100);NativeWin32.FreeConsole();NativeWin32.SetConsoleCtrlHandler(IntPtr.Zero,false);}}catch{}AppendText("^C\r\n",FG_ERR);inputStart=-1;ShowPrompt();}
    void ClearScreen(){Clear();inputStart=-1;ShowPrompt();ScreenCleared?.Invoke();}
}

// ════════════════════════════════════════════════════════════════════════════
// GRADIENT PANEL — for modern UI sections
// ════════════════════════════════════════════════════════════════════════════
class GradientPanel : Panel
{
    public Color Color1{get;set;}=Color.FromArgb(22,16,44);
    public Color Color2{get;set;}=Color.FromArgb(10,8,22);
    public float Angle{get;set;}=135f;
    protected override void OnPaintBackground(PaintEventArgs e){using var b=new LinearGradientBrush(ClientRectangle,Color1,Color2,Angle);e.Graphics.FillRectangle(b,ClientRectangle);}
}

// ════════════════════════════════════════════════════════════════════════════
// MAIN FORM — ORIGINAL LOGIC + REDESIGNED UI
// ════════════════════════════════════════════════════════════════════════════
class MainForm : Form
{
    AppConfig cfg;bool silentStart;List<RunningScript>running=new();Regex censorRegex;
    FakeTerminalSession ftSession=new();ShellInterceptor shellInterceptor=new();
    readonly List<(HeadlessShellHost host,FakeTerminalSession session)>headlessSessions=new();
    TabControl tabs;CmdTerminal terminal;Label statusLabel,sessionStatusLabel,connStatusLabel;Panel runningPanel,customScriptsPanel;
    NotifyIcon trayIcon;ContextMenuStrip trayMenu;System.Windows.Forms.Timer refreshTimer;
    int uiModalCount=0;

    // ═══ COLOR PALETTE — dark slate/blue like web dashboard ═══
    static readonly Color C_BG=Color.FromArgb(15,23,42);        // slate-900
    static readonly Color C_BG2=Color.FromArgb(20,28,50);
    static readonly Color C_FG=Color.FromArgb(226,232,240);      // slate-200
    static readonly Color C_FG_DIM=Color.FromArgb(100,116,139);  // slate-500
    static readonly Color C_ACCENT=Color.FromArgb(129,140,248);  // indigo-400
    static readonly Color C_ACCENT2=Color.FromArgb(34,211,238);  // cyan-400
    static readonly Color C_GREEN=Color.FromArgb(74,222,128);    // green-400
    static readonly Color C_RED=Color.FromArgb(248,113,113);     // red-400
    static readonly Color C_AMBER=Color.FromArgb(251,191,36);    // amber-400
    static readonly Color C_BORDER=Color.FromArgb(51,65,85);     // slate-700
    static readonly Color C_BTN_BG=Color.FromArgb(30,41,59);     // slate-800
    static readonly Color C_BTN_HOV=Color.FromArgb(51,65,85);    // slate-700
    static readonly Color C_CARD=Color.FromArgb(24,33,55);       // darker card

    // Centralized path resolution — uses AppConfig.ResolvePath with fallback names
    string FakeTerminalExePath => AppConfig.ResolvePath(cfg.FakeTerminalPath, "Terminal.exe", "Command Prompt.exe");
    string FakePowerShellExePath => AppConfig.ResolvePath(cfg.FakePowerShellPath, "Windows PowerShell.exe");
    string LauncherExePath => AppConfig.ResolvePath(cfg.SeiwareLauncherPath, "DreamLandLauncher.exe", "SeiwareLauncher.exe");
    string CmdIcoPath => AppConfig.ResolvePath(cfg.CmdTerminalIcoPath, "terminal.ico", "cmdterminal.ico");
    string CmdAdminIcoPath => AppConfig.ResolvePath(cfg.CmdTerminalIcoPath, "cmdterminal.ico", "terminal.ico");
    string PsIcoPath => AppConfig.ResolvePath(cfg.PowerShellIcoPath, "powershell.ico");
    string FakeExeFor(ShellType st)=>st==ShellType.PowerShell?FakePowerShellExePath:FakeTerminalExePath;
    string IcoFor(ShellType st)=>st==ShellType.PowerShell?PsIcoPath:CmdIcoPath;

    // ═══ ORIGINAL LaunchShell — UNCHANGED ═══
    void LaunchShell(ShellType shellType,bool admin)
    {string pn="SeiwareFT_"+Guid.NewGuid().ToString("N").Substring(0,8);var ftSess=new FakeTerminalSession(pn);var host=new HeadlessShellHost(shellType,censorRegex);
        host.OutputChunk+=t=>ftSess.SendOutput(t);host.CommandFinished+=()=>ftSess.SendCommandFinished();host.ScreenCleared+=()=>ftSess.SendClear();
        ftSess.CommandReceived+=cmd=>host.SendCommand(cmd);ftSess.CtrlCReceived+=()=>host.SendCtrlC();ftSess.FakeTermClosed+=()=>{host.Stop();host.Dispose();};
        string fakeExe=FakeExeFor(shellType);string workDir=admin?@"C:\Windows\System32":Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        shellInterceptor.SuppressFor(3000);
        if(!host.Start(workDir)){string shellName=shellType==ShellType.PowerShell?"powershell.exe":"cmd.exe";ShowOwnedMessage($"Failed to start {shellName}\n\nMake sure \"{Path.GetFileName(fakeExe)}\" exists next to DreamLand.exe","DreamLand",MessageBoxButtons.OK,MessageBoxIcon.Error);host.Dispose();ftSess.Dispose();return;}
        if(host.ShellPid>0)shellInterceptor.RegisterExemptPid(host.ShellPid);
        string ba=cfg.BannedNames!=null&&cfg.BannedNames.Count>0?$" --banned \"{string.Join("|",cfg.BannedNames.Select(w=>w.Replace("\"","")))}\"":"";
        // Admin CMD should prefer cmdterminal.ico, normal CMD can prefer terminal.ico
        string ico = shellType==ShellType.Cmd && admin ? CmdAdminIcoPath : IcoFor(shellType);
        if(!StorageUtil.FileExists(ico)) ico = shellType==ShellType.Cmd ? CmdAdminIcoPath : PsIcoPath;
        string iconArg=StorageUtil.FileExists(ico)?$" --icon \"{ico}\"":"";
        string configArg=$" --config \"{AppConfig.ConfigPath}\"";string shellArg=shellType==ShellType.PowerShell?" --shell ps":"";string adminArg=admin?" --admin":"";
        string extraArgs=$"{adminArg}{shellArg}{ba}{iconArg}{configArg}".Trim();
        try{ftSess.Start(fakeExe,workDir,LauncherExePath,elevated:admin,extraArgs:extraArgs);if(ftSess.FakeTermPid>0)shellInterceptor.RegisterExemptPid(ftSess.FakeTermPid);headlessSessions.Add((host,ftSess));}
        catch(Exception ex){host.Dispose();ftSess.Dispose();if(!ex.Message.Contains("1223")&&!ex.Message.ToLower().Contains("cancelled"))ShowOwnedMessage($"Failed: {ex.Message}","DreamLand",MessageBoxButtons.OK,MessageBoxIcon.Error);}}

    public MainForm(bool silent)
    {silentStart=silent;cfg=AppConfig.Load();RebuildRegex();CommandGuard.SetBannedNames(cfg.BannedNames);BuildUI();BuildTray();WireInterceptor();
        CommandGuard.Blocked+=(name)=>{try{if(!IsDisposed)BeginInvoke((Action)(()=>CaptureProofNotify($"Blocked: {name}",isWarning:true)));}catch{}};
        if(silentStart){ShowInTaskbar=false;Visible=false;}
        refreshTimer=new System.Windows.Forms.Timer{Interval=2000};refreshTimer.Tick+=(s,e)=>RefreshRunningPanel();refreshTimer.Start();}

    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);NativeWin32.SetWindowDisplayAffinity(this.Handle,NativeWin32.WDA_EXCLUDEFROMCAPTURE);}

    // ═══ ORIGINAL WireInterceptor — UNCHANGED ═══
    void WireInterceptor()
    {shellInterceptor.OnInterceptNormal=(shellType)=>{if(IsDisposed)return;if(shellType==ShellType.PowerShell&&!cfg.InterceptPowerShell)return;
        BeginInvoke((Action)(()=>{if(shellType==ShellType.Cmd){ShowMain();if(tabs!=null&&tabs.TabPages.Count>1)tabs.SelectedIndex=1;if(!ftSession.IsRunning)StartSession();CaptureProofNotify("CMD intercepted");}
            else{LaunchShell(ShellType.PowerShell,admin:false);CaptureProofNotify("PowerShell intercepted");}}));};
        shellInterceptor.OnInterceptAdmin=(shellType)=>{if(IsDisposed)return;if(shellType==ShellType.PowerShell&&!cfg.InterceptPowerShell)return;
            BeginInvoke((Action)(()=>{LaunchShell(shellType,admin:true);string name=shellType==ShellType.PowerShell?"Admin PowerShell":"Admin CMD";CaptureProofNotify($"{name} intercepted",isWarning:true);}));};
        shellInterceptor.OnControlPanelBlocked=()=>{if(IsDisposed)return;BeginInvoke((Action)(()=>CaptureProofNotify("Log viewer blocked",isWarning:true)));};
        shellInterceptor.Start();UpdateStatus();}

    [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr h,int i);[DllImport("user32.dll")]static extern int SetWindowLong(IntPtr h,int i,int v);
    const int GWL_EXSTYLE=-20,WS_EX_TOOLWINDOW=0x80;

    void CaptureProofNotify(string msg,bool isWarning=false)
    {
        var ac=isWarning?C_AMBER:C_ACCENT2;
        // Wider toast so text doesn't get cut off
        int toastW=Math.Max(420,TextRenderer.MeasureText(msg,new Font("Segoe UI",9.5f,FontStyle.Bold)).Width+80);
        if(toastW>600)toastW=600;
        var n=new Form{FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,TopMost=true,StartPosition=FormStartPosition.Manual,Size=new Size(toastW,62),BackColor=Color.FromArgb(18,28,44),Opacity=0.97};
        var wa=Screen.PrimaryScreen?.WorkingArea??new Rectangle(0,0,1920,1080);
        n.Location=new Point(wa.Right-n.Width-16,wa.Bottom-n.Height-12);
        n.Paint+=(s,e)=>{
            using var br=new LinearGradientBrush(n.ClientRectangle,Color.FromArgb(24,34,56),Color.FromArgb(14,22,38),90f);
            e.Graphics.FillRectangle(br,n.ClientRectangle);
            using var pen=new Pen(Color.FromArgb(100,ac.R,ac.G,ac.B));
            e.Graphics.DrawRectangle(pen,0,0,n.Width-1,n.Height-1);
        };
        n.Controls.Add(new Panel{Location=new Point(0,0),Size=new Size(4,n.Height),BackColor=ac});
        n.Controls.Add(new Label{Text=isWarning?"⚠":"◈",AutoSize=true,Font=new Font("Segoe UI",16f),ForeColor=ac,Location=new Point(14,14),BackColor=Color.Transparent});
        n.Controls.Add(new Label{Text=msg,AutoSize=false,Font=new Font("Segoe UI",9.5f,FontStyle.Bold),ForeColor=C_FG,Location=new Point(48,0),Size=new Size(n.Width-60,n.Height),TextAlign=ContentAlignment.MiddleLeft,BackColor=Color.Transparent});
        n.HandleCreated+=(s,e)=>{int ex=GetWindowLong(n.Handle,GWL_EXSTYLE);SetWindowLong(n.Handle,GWL_EXSTYLE,ex|WS_EX_TOOLWINDOW);NativeWin32.SetWindowDisplayAffinity(n.Handle,NativeWin32.WDA_EXCLUDEFROMCAPTURE);};
        n.Show(this);
        var t=new System.Windows.Forms.Timer{Interval=3500};t.Tick+=(s,e)=>{t.Stop();t.Dispose();if(!n.IsDisposed)n.Close();};t.Start();
    }

    void BuildTray(){trayMenu=new ContextMenuStrip();trayMenu.Items.Add("Open DreamLand").Click+=(s,e)=>ShowMain();trayMenu.Items.Add("Stop All").Click+=(s,e)=>StopAllScripts();trayMenu.Items.Add(new ToolStripSeparator());var ii=new ToolStripMenuItem("Intercept: ...");ii.Click+=(s,e)=>{if(shellInterceptor.IsEnabled)shellInterceptor.Stop();else shellInterceptor.Start();ii.Text=shellInterceptor.IsEnabled?"Intercept On":"Intercept Off";UpdateStatus();};trayMenu.Opening+=(s,e)=>{ii.Text=shellInterceptor.IsEnabled?"Intercept On":"Intercept Off";};trayMenu.Items.Add(ii);trayMenu.Items.Add(new ToolStripSeparator());trayMenu.Items.Add("Exit").Click+=(s,e)=>FullExit();Icon ic;try{string p=Path.Combine(AppContext.BaseDirectory,"Seiware.ico");if(!File.Exists(p))p=Path.Combine(AppContext.BaseDirectory,"DreamLand.ico");ic=File.Exists(p)?new Icon(p):SystemIcons.Application;}catch{ic=SystemIcons.Application;}trayIcon=new NotifyIcon{Icon=ic,Text="DreamLand",Visible=true,ContextMenuStrip=trayMenu};trayIcon.DoubleClick+=(s,e)=>ShowMain();}
    void ShowMain(){Show();ShowInTaskbar=true;WindowState=FormWindowState.Normal;BringToFront();Activate();}
    void FullExit(){shellInterceptor?.Stop();shellInterceptor?.Dispose();StopAllScripts();trayIcon.Visible=false;Application.Exit();}
    DialogResult ShowOwnedMessage(string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon){uiModalCount++;try{return MessageBox.Show(this,text,caption,buttons,icon);}finally{uiModalCount--;}}
    DialogResult ShowOwnedFileDialog(FileDialog dlg){uiModalCount++;try{return dlg.ShowDialog(this);}finally{uiModalCount--;}}
    DialogResult ShowOwnedDialog(Form dlg){uiModalCount++;try{return dlg.ShowDialog(this);}finally{uiModalCount--;}}
    protected override void OnFormClosing(FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();ShowInTaskbar=false;}else{shellInterceptor?.Dispose();trayIcon.Visible=false;}base.OnFormClosing(e);}

    // ═══ CLEAN BUILD UI — no overlapping, proper dock order ═══
    void BuildUI()
    {
        Text="DreamLand";Size=new Size(940,680);StartPosition=FormStartPosition.CenterScreen;BackColor=C_BG;ForeColor=C_FG;Font=new Font("Segoe UI",9.5f);MinimumSize=new Size(800,550);

        // Status bar FIRST (Bottom dock)
        statusLabel=new Label{Dock=DockStyle.Bottom,Height=28,BackColor=Color.FromArgb(10,18,32),ForeColor=C_FG_DIM,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(12,0,0,0),Font=new Font("Segoe UI",8.5f)};
        statusLabel.Paint+=(s,e)=>{using var pen=new Pen(C_BORDER);e.Graphics.DrawLine(pen,0,0,statusLabel.Width,0);};
        Controls.Add(statusLabel);

        // Tabs fill the rest
        tabs=new TabControl{Dock=DockStyle.Fill,Font=new Font("Segoe UI",10f)};
        tabs.DrawMode=TabDrawMode.OwnerDrawFixed;tabs.SizeMode=TabSizeMode.Fixed;tabs.ItemSize=new Size(130,36);
        tabs.DrawItem+=(s,e)=>{
            var g=e.Graphics;var r=tabs.GetTabRect(e.Index);bool sel=e.Index==tabs.SelectedIndex;
            using var bg=new SolidBrush(sel?Color.FromArgb(30,41,59):Color.FromArgb(15,23,42));g.FillRectangle(bg,r);
            using var f=new Font("Segoe UI",9.5f,sel?FontStyle.Bold:FontStyle.Regular);
            TextRenderer.DrawText(g,tabs.TabPages[e.Index].Text,f,r,sel?C_ACCENT:C_FG_DIM,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            if(sel){using var pen=new Pen(C_ACCENT,2);g.DrawLine(pen,r.Left+4,r.Bottom-2,r.Right-4,r.Bottom-2);}};
        Controls.Add(tabs);
        BuildScriptsTab();BuildTerminalTab();BuildConfigTab();
    }

    // ═══ SCRIPTS TAB — with descriptions + custom scripts ═══
    void BuildScriptsTab()
    {var page=new TabPage(" ⚡ Scripts "){BackColor=C_BG};tabs.TabPages.Add(page);
        var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=C_BG};page.Controls.Add(scroll);int y=16;
        scroll.Controls.Add(new Label{Text="⚡ PowerShell Scripts",AutoSize=true,Font=new Font("Segoe UI Semibold",14f),ForeColor=C_ACCENT,Location=new Point(20,y),BackColor=Color.Transparent});y+=42;
        var scripts=new(string n,string desc,Func<string>p)[]{
            ("worker.ps1","Background worker — persistent task automation",()=>cfg.WorkerScript),
            ("Memory_Optimizer.ps1","Memory optimization — cleans working sets & caches",()=>cfg.MemoryScript),
            ("LOL.ps1","LOL script — game-specific automation",()=>cfg.LolScript),
            ("HideFiles.ps1","File hiding — sets hidden+system attributes on targets",()=>cfg.HideFilesScript)};
        foreach(var(n,desc,gp)in scripts){string sp=gp();bool ex=StorageUtil.FileExists(sp);
            // Card-style row
            var card=new Panel{Location=new Point(20,y),Size=new Size(860,52),BackColor=C_CARD};card.Paint+=(s,e)=>{using var pen=new Pen(C_BORDER);e.Graphics.DrawRectangle(pen,0,0,card.Width-1,card.Height-1);};scroll.Controls.Add(card);
            card.Controls.Add(new Label{Text=ex?"✓":"✕",AutoSize=true,Font=new Font("Segoe UI",12f),ForeColor=ex?C_GREEN:C_RED,Location=new Point(12,12),BackColor=Color.Transparent});
            card.Controls.Add(new Label{Text=n,AutoSize=true,Font=new Font("Consolas",10f,FontStyle.Bold),ForeColor=C_FG,Location=new Point(40,6),BackColor=Color.Transparent});
            card.Controls.Add(new Label{Text=desc,AutoSize=true,Font=new Font("Segoe UI",8f),ForeColor=C_FG_DIM,Location=new Point(40,28),BackColor=Color.Transparent});
            var btn=new Button{Text="▶ Launch",Size=new Size(90,30),Location=new Point(755,10),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(25,80,45),ForeColor=Color.White,Font=new Font("Segoe UI",8.5f,FontStyle.Bold),Cursor=Cursors.Hand};btn.FlatAppearance.BorderSize=1;btn.FlatAppearance.BorderColor=Color.FromArgb(50,160,80);btn.FlatAppearance.MouseOverBackColor=Color.FromArgb(35,110,60);string cp=sp;string cn=n;btn.Click+=(s,e)=>LaunchScript(cn,cp,btn);card.Controls.Add(btn);y+=58;}
        // Running scripts panel
        y+=12;runningPanel=new Panel{Location=new Point(20,y),Size=new Size(860,40),BackColor=Color.FromArgb(16,12,30)};scroll.Controls.Add(runningPanel);y+=48;
        // Custom scripts section
        scroll.Controls.Add(new Label{Text="🔧 Custom Scripts",AutoSize=true,Font=new Font("Segoe UI Semibold",13f),ForeColor=C_ACCENT2,Location=new Point(20,y),BackColor=Color.Transparent});y+=38;
        var btnAdd=new Button{Text="➕ Add Custom Script",Size=new Size(200,32),Location=new Point(20,y),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(35,55,100),ForeColor=Color.White,Font=new Font("Segoe UI",9f,FontStyle.Bold),Cursor=Cursors.Hand};btnAdd.FlatAppearance.BorderSize=1;btnAdd.FlatAppearance.BorderColor=Color.FromArgb(70,110,180);btnAdd.FlatAppearance.MouseOverBackColor=Color.FromArgb(45,70,130);
        btnAdd.Click+=(s,e)=>{using var dlg=new CustomScriptDialog();if(dlg.ShowDialog(this)==DialogResult.OK){cfg.CustomScripts.Add(new CustomScript{Name=dlg.ScriptName,Description=dlg.ScriptDescription,ScriptPath=dlg.ScriptFilePath});cfg.Save();RefreshCustomScripts();}};scroll.Controls.Add(btnAdd);y+=40;
        customScriptsPanel=new Panel{Location=new Point(20,y),Size=new Size(860,200),BackColor=Color.Transparent};scroll.Controls.Add(customScriptsPanel);RefreshCustomScripts();}

    void RefreshCustomScripts(){if(customScriptsPanel==null)return;customScriptsPanel.Controls.Clear();int py=0;
        foreach(var cs in cfg.CustomScripts){bool ex=cs.Exists;
            var card=new Panel{Location=new Point(0,py),Size=new Size(860,48),BackColor=C_CARD};card.Paint+=(s,e)=>{using var pen=new Pen(C_BORDER);e.Graphics.DrawRectangle(pen,0,0,card.Width-1,card.Height-1);};customScriptsPanel.Controls.Add(card);
            card.Controls.Add(new Label{Text=ex?"✓":"✕",AutoSize=true,Font=new Font("Segoe UI",11f),ForeColor=ex?C_GREEN:C_RED,Location=new Point(12,10),BackColor=Color.Transparent});
            card.Controls.Add(new Label{Text=cs.Name,AutoSize=true,Font=new Font("Consolas",9.5f,FontStyle.Bold),ForeColor=C_FG,Location=new Point(38,6),BackColor=Color.Transparent});
            card.Controls.Add(new Label{Text=cs.Description,AutoSize=true,Font=new Font("Segoe UI",8f),ForeColor=C_FG_DIM,Location=new Point(38,26),BackColor=Color.Transparent});
            var btn=new Button{Text="▶",Size=new Size(36,28),Location=new Point(770,10),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(25,80,45),ForeColor=Color.White,Cursor=Cursors.Hand,Enabled=ex};btn.FlatAppearance.BorderSize=1;btn.FlatAppearance.BorderColor=Color.FromArgb(50,160,80);string cp=cs.ScriptPath;string cn=cs.Name;btn.Click+=(s,e)=>LaunchScript(cn,cp,btn);card.Controls.Add(btn);
            var btnDel=new Button{Text="✕",Size=new Size(28,28),Location=new Point(812,10),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(80,25,30),ForeColor=Color.FromArgb(255,140,140),Cursor=Cursors.Hand};btnDel.FlatAppearance.BorderSize=0;CustomScript captured=cs;btnDel.Click+=(s,e)=>{cfg.CustomScripts.Remove(captured);cfg.Save();RefreshCustomScripts();};card.Controls.Add(btnDel);py+=54;}
        if(cfg.CustomScripts.Count==0)customScriptsPanel.Controls.Add(new Label{Text="No custom scripts yet. Click ➕ to add one.",AutoSize=true,Font=new Font("Segoe UI",9.5f),ForeColor=C_FG_DIM,Location=new Point(8,10),BackColor=Color.Transparent});customScriptsPanel.Height=Math.Max(50,py+8);}

    // ═══ TERMINAL TAB ═══
    void BuildTerminalTab(){var page=new TabPage(" 🖥 Terminal "){BackColor=C_BG};tabs.TabPages.Add(page);
        var bar=new Panel{Dock=DockStyle.Top,Height=52,BackColor=Color.FromArgb(20,30,48)};bar.Paint+=(s,e)=>{using var pen=new Pen(C_BORDER);e.Graphics.DrawLine(pen,0,bar.Height-1,bar.Width,bar.Height-1);};int bx=12;
        Button CB(string t,Color bg,Color bd){var b=new Button{Text=t,Location=new Point(bx,10),Size=new Size(145,32),FlatStyle=FlatStyle.Flat,BackColor=bg,ForeColor=Color.White,Font=new Font("Segoe UI",8.5f,FontStyle.Bold),Cursor=Cursors.Hand};b.FlatAppearance.BorderSize=1;b.FlatAppearance.BorderColor=bd;b.FlatAppearance.MouseOverBackColor=ControlPaint.Light(bg,0.12f);bar.Controls.Add(b);bx+=153;return b;}
        var btnStart=CB("▶ Start Session",Color.FromArgb(22,85,48),Color.FromArgb(45,165,85));var btnStop=CB("■ End Session",Color.FromArgb(85,22,32),Color.FromArgb(165,50,60));var btnClear=CB("⌫ Clear",C_BTN_BG,C_BORDER);var btnAdmin=CB("⊞ Admin CMD",Color.FromArgb(75,50,12),Color.FromArgb(165,115,35));
        var sbar=new Panel{Dock=DockStyle.Top,Height=32,BackColor=Color.FromArgb(12,20,35)};sbar.Paint+=(s,e)=>{using var pen=new Pen(C_BORDER);e.Graphics.DrawLine(pen,0,sbar.Height-1,sbar.Width,sbar.Height-1);};
        sessionStatusLabel=new Label{Text="● Session: Inactive",AutoSize=true,Location=new Point(14,8),Font=new Font("Segoe UI",8.5f),ForeColor=C_FG_DIM,BackColor=Color.Transparent};connStatusLabel=new Label{Text="○ FakeTerminal: Disconnected",AutoSize=true,Location=new Point(250,8),Font=new Font("Segoe UI",8.5f),ForeColor=C_FG_DIM,BackColor=Color.Transparent};sbar.Controls.Add(sessionStatusLabel);sbar.Controls.Add(connStatusLabel);
        terminal=new CmdTerminal{Dock=DockStyle.Fill};terminal.SetCensorRegex(censorRegex);terminal.OutputChunk+=text=>ftSession.SendOutput(text);terminal.ScreenCleared+=()=>ftSession.SendClear();terminal.CommandFinished+=()=>ftSession.SendCommandFinished();ftSession.CommandReceived+=cmd=>{if(!terminal.IsDisposed)terminal.BeginInvoke((Action)(()=>terminal.SendCommand(cmd)));};ftSession.CtrlCReceived+=()=>{if(!terminal.IsDisposed)terminal.BeginInvoke((Action)(()=>terminal.ExternalCtrlC()));};ftSession.FakeTermClosed+=()=>{if(!connStatusLabel.IsDisposed)connStatusLabel.BeginInvoke((Action)(()=>UpdStat(true,false)));};
        btnStart.Click+=(s,e)=>StartSession();btnStop.Click+=(s,e)=>EndSession();btnClear.Click+=(s,e)=>{terminal.Clear();ftSession.SendClear();};btnAdmin.Click+=(s,e)=>LaunchShell(ShellType.Cmd,admin:true);
        bar.Controls.Add(new Label{Text="↑↓ history • Ctrl+C break • Ctrl+L clear",AutoSize=true,Font=new Font("Segoe UI",7.5f),ForeColor=C_FG_DIM,Location=new Point(bx+14,18),BackColor=Color.Transparent});
        page.Controls.Add(terminal);page.Controls.Add(sbar);page.Controls.Add(bar);bar.BringToFront();sbar.BringToFront();
        terminal.AppendText("─────────────────────────────────────────\r\n DreamLand │ Embedded CMD Terminal\r\n─────────────────────────────────────────\r\n Click ▶ Start Session to begin.\r\n PowerShell is intercepted automatically.\r\n\r\n");}

    void StartSession(){if(!StorageUtil.FileExists(FakeTerminalExePath)){ShowOwnedMessage($"Terminal not found at:\n{FakeTerminalExePath}","DreamLand",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}shellInterceptor.SuppressFor(3000);terminal.StartShell();if(terminal.ShellPid>0)shellInterceptor.RegisterExemptPid(terminal.ShellPid);try{string workDir=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);ftSession.Start(FakeTerminalExePath,workDir,LauncherExePath);if(ftSession.FakeTermPid>0)shellInterceptor.RegisterExemptPid(ftSession.FakeTermPid);UpdStat(true,false);var pt=new System.Windows.Forms.Timer{Interval=500};int polls=0;pt.Tick+=(s,e)=>{polls++;if(ftSession.IsConnected){pt.Stop();pt.Dispose();UpdStat(true,true);}else if(polls>30){pt.Stop();pt.Dispose();}};pt.Start();}catch(Exception ex){ShowOwnedMessage($"Failed:\n{ex.Message}","DreamLand",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
    void EndSession(){ftSession.SendSessionEnded();Thread.Sleep(300);terminal.StopShell();ftSession.Dispose();UpdStat(false,false);}
    void UpdStat(bool active,bool conn){if(sessionStatusLabel.IsDisposed)return;if(sessionStatusLabel.InvokeRequired){sessionStatusLabel.BeginInvoke((Action)(()=>UpdStat(active,conn)));return;}sessionStatusLabel.Text=active?"● Session: Active":"● Session: Inactive";sessionStatusLabel.ForeColor=active?C_GREEN:C_FG_DIM;connStatusLabel.Text=conn?"◉ FakeTerminal: Connected":(active?"○ Connecting...":"○ FakeTerminal: Disconnected");connStatusLabel.ForeColor=conn?C_ACCENT2:(active?C_AMBER:C_FG_DIM);}

    // ═══ CONFIG TAB — with paths and copy buttons ═══
    void BuildConfigTab(){var page=new TabPage(" ⚙ Config "){BackColor=C_BG};tabs.TabPages.Add(page);
        var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=C_BG};page.Controls.Add(scroll);int y=16;
        scroll.Controls.Add(new Label{Text="⚙ Configuration",AutoSize=true,Font=new Font("Segoe UI Semibold",14f),ForeColor=C_ACCENT,Location=new Point(20,y),BackColor=Color.Transparent});y+=44;
        var btnOpen=MkBtn("📝 Open Config",20,y,C_BTN_BG,C_BORDER);btnOpen.Click+=(s,e)=>{try{Process.Start("notepad.exe",AppConfig.ConfigPath);}catch{}};scroll.Controls.Add(btnOpen);
        var btnWizard=MkBtn("🔧 Setup Wizard",200,y,C_BTN_BG,C_BORDER);btnWizard.Click+=(s,e)=>{var wiz=new SetupWizard();if(ShowOwnedDialog(wiz)==DialogResult.OK){cfg=AppConfig.Load();RebuildRegex();terminal?.SetCensorRegex(censorRegex);UpdateStatus();}};scroll.Controls.Add(btnWizard);
        var btnBanned=MkBtn("🚫 Banned Names",400,y,C_BTN_BG,C_BORDER);btnBanned.Click+=(s,e)=>OpenSettings();scroll.Controls.Add(btnBanned);y+=46;
        // Checkboxes
        var cbPS=new CheckBox{Text=" Intercept PowerShell",AutoSize=true,Location=new Point(24,y),ForeColor=C_FG,FlatStyle=FlatStyle.System,Font=new Font("Segoe UI",9.5f),Checked=cfg.InterceptPowerShell};cbPS.CheckedChanged+=(s,e)=>{cfg.InterceptPowerShell=cbPS.Checked;cfg.Save();};scroll.Controls.Add(cbPS);y+=28;
        var cb=new CheckBox{Text=" Start with Windows",AutoSize=true,Location=new Point(24,y),ForeColor=C_FG,FlatStyle=FlatStyle.System,Font=new Font("Segoe UI",9.5f),Checked=cfg.StartWithWindows};cb.CheckedChanged+=(s,e)=>{cfg.StartWithWindows=cb.Checked;cfg.Save();cfg.ApplyStartWithWindows();};scroll.Controls.Add(cb);y+=28;
        var ci=new CheckBox{Text=" Intercept shells (requires Admin)",AutoSize=true,Location=new Point(24,y),ForeColor=C_FG,FlatStyle=FlatStyle.System,Font=new Font("Segoe UI",9.5f),Checked=shellInterceptor.IsEnabled};ci.CheckedChanged+=(s,e)=>{if(ci.Checked)shellInterceptor.Start();else shellInterceptor.Stop();UpdateStatus();};scroll.Controls.Add(ci);y+=40;
        // Component paths
        scroll.Controls.Add(new Label{Text="📁 Component Paths",AutoSize=true,Font=new Font("Segoe UI Semibold",13f),ForeColor=C_ACCENT2,Location=new Point(20,y),BackColor=Color.Transparent});y+=38;
        var btnCopyAll=MkBtn("📋 Copy All Paths",20,y,Color.FromArgb(35,55,100),Color.FromArgb(70,110,180));
        btnCopyAll.Click+=(s,e)=>{var paths=cfg.GetAllPaths();var sb=new StringBuilder();foreach(var kvp in paths)sb.AppendLine($"{kvp.Key}: {kvp.Value}");Clipboard.SetText(sb.ToString());btnCopyAll.Text="✓ Copied!";var tmr=new System.Windows.Forms.Timer{Interval=2000};tmr.Tick+=(ts,te)=>{tmr.Stop();tmr.Dispose();if(!btnCopyAll.IsDisposed)btnCopyAll.Text="📋 Copy All Paths";};tmr.Start();};scroll.Controls.Add(btnCopyAll);y+=38;
        // Editable path textboxes — keyed by config property name
        var pathEditors=new Dictionary<string,TextBox>();
        string[] editableKeys={"Terminal.exe","Windows PowerShell.exe","Launcher.exe","terminal.ico","powershell.ico"};
        string[] configKeys={"FakeTerminalPath","FakePowerShellPath","SeiwareLauncherPath","CmdTerminalIcoPath","PowerShellIcoPath"};

        foreach(var kvp in cfg.GetAllPaths()){bool ex=File.Exists(kvp.Value)||Directory.Exists(kvp.Value);
            var row=new Panel{Location=new Point(20,y),Size=new Size(860,26),BackColor=Color.Transparent};scroll.Controls.Add(row);
            row.Controls.Add(new Label{Text=ex?"✓":"✕",AutoSize=true,Font=new Font("Segoe UI",9f),ForeColor=ex?C_GREEN:C_RED,Location=new Point(0,2),BackColor=Color.Transparent});
            row.Controls.Add(new Label{Text=kvp.Key,AutoSize=true,Font=new Font("Consolas",8.5f,FontStyle.Bold),ForeColor=C_FG,Location=new Point(20,3),BackColor=Color.Transparent});
            // Editable for component paths, readonly for computed paths
            int idx=Array.IndexOf(editableKeys,kvp.Key);
            bool editable=idx>=0;
            var tb=new TextBox{Text=kvp.Value,Size=new Size(420,20),Location=new Point(210,2),BackColor=Color.FromArgb(20,30,45),ForeColor=editable?C_FG:Color.FromArgb(130,140,160),BorderStyle=BorderStyle.FixedSingle,Font=new Font("Consolas",7.5f),ReadOnly=!editable};row.Controls.Add(tb);
            if(editable)pathEditors[configKeys[idx]]=tb;
            // Browse button for editable paths
            if(editable){var br=new Button{Text="...",Size=new Size(28,20),Location=new Point(636,2),FlatStyle=FlatStyle.Flat,BackColor=C_BTN_BG,ForeColor=C_FG,Cursor=Cursors.Hand,Font=new Font("Segoe UI",7f)};br.FlatAppearance.BorderSize=1;br.FlatAppearance.BorderColor=C_BORDER;
                string captKey=kvp.Key;var captTb=tb;br.Click+=(s,e)=>{string filter=captKey.Contains(".ico")?"ICO|*.ico|All|*.*":"EXE|*.exe|All|*.*";using var ofd=new OpenFileDialog{Title=$"Locate {captKey}",Filter=filter};if(ShowOwnedFileDialog(ofd)==DialogResult.OK)captTb.Text=ofd.FileName;};row.Controls.Add(br);}
            // Copy button
            var bc=new Button{Text="📋",Size=new Size(28,20),Location=new Point(editable?668:636,2),FlatStyle=FlatStyle.Flat,BackColor=C_BTN_BG,ForeColor=C_FG,Cursor=Cursors.Hand,Font=new Font("Segoe UI",7f)};bc.FlatAppearance.BorderSize=1;bc.FlatAppearance.BorderColor=C_BORDER;string val=kvp.Value;bc.Click+=(s,e)=>{Clipboard.SetText(editable?tb.Text:val);bc.Text="✓";var tmr=new System.Windows.Forms.Timer{Interval=1500};tmr.Tick+=(ts,te)=>{tmr.Stop();tmr.Dispose();if(!bc.IsDisposed)bc.Text="📋";};tmr.Start();};row.Controls.Add(bc);y+=28;}

        // Save paths button
        y+=10;
        var btnSavePaths=MkBtn("💾 Save All Paths",20,y,Color.FromArgb(25,85,50),Color.FromArgb(45,160,80));
        btnSavePaths.Click+=(s,e)=>{
            if(pathEditors.ContainsKey("FakeTerminalPath"))cfg.FakeTerminalPath=pathEditors["FakeTerminalPath"].Text.Trim();
            if(pathEditors.ContainsKey("FakePowerShellPath"))cfg.FakePowerShellPath=pathEditors["FakePowerShellPath"].Text.Trim();
            if(pathEditors.ContainsKey("SeiwareLauncherPath"))cfg.SeiwareLauncherPath=pathEditors["SeiwareLauncherPath"].Text.Trim();
            if(pathEditors.ContainsKey("CmdTerminalIcoPath"))cfg.CmdTerminalIcoPath=pathEditors["CmdTerminalIcoPath"].Text.Trim();
            if(pathEditors.ContainsKey("PowerShellIcoPath"))cfg.PowerShellIcoPath=pathEditors["PowerShellIcoPath"].Text.Trim();
            cfg.Save();
            ShowOwnedMessage("All paths saved!\nRestart DreamLand if you changed exe paths.","DreamLand",MessageBoxButtons.OK,MessageBoxIcon.Information);
        };
        scroll.Controls.Add(btnSavePaths);y+=46;
        scroll.Controls.Add(new Label{Text=$"Config: {AppConfig.ConfigPath}",AutoSize=true,Font=new Font("Consolas",7.5f),ForeColor=C_FG_DIM,Location=new Point(24,y),BackColor=Color.Transparent});}

    Button MkBtn(string text,int x,int y,Color bg,Color bd){var b=new Button{Text=text,AutoSize=true,MinimumSize=new Size(165,34),Location=new Point(x,y),FlatStyle=FlatStyle.Flat,BackColor=bg,ForeColor=C_FG,Cursor=Cursors.Hand,Font=new Font("Segoe UI",9f,FontStyle.Bold)};b.FlatAppearance.BorderSize=1;b.FlatAppearance.BorderColor=bd;b.FlatAppearance.MouseOverBackColor=ControlPaint.Light(bg,0.1f);return b;}
    void OpenSettings(){var dlg=new SettingsForm(new List<string>(cfg.BannedNames));if(ShowOwnedDialog(dlg)==DialogResult.OK){cfg.BannedNames=dlg.BannedNames;cfg.Save();RebuildRegex();CommandGuard.SetBannedNames(cfg.BannedNames);terminal?.SetCensorRegex(censorRegex);foreach(var(h,_)in headlessSessions)h.SetCensorRegex(censorRegex);UpdateStatus();}}

    string ResolveLaunchScriptPath(string displayName,string configuredPath)
    {
        if(StorageUtil.FileExists(configuredPath)) return configuredPath;

        // Regenerate embedded built-ins if missing
        try
        {
            if(displayName.Equals("worker.ps1",StringComparison.OrdinalIgnoreCase) ||
               displayName.Equals("Memory_Optimizer.ps1",StringComparison.OrdinalIgnoreCase) ||
               displayName.Equals("HideFiles.ps1",StringComparison.OrdinalIgnoreCase))
                PsEmbedder.WriteAll();
            if(displayName.Equals("LOL.ps1",StringComparison.OrdinalIgnoreCase))
                PsEmbedder.WriteLol();
            if(StorageUtil.FileExists(configuredPath)) return configuredPath;
        }
        catch {}

        var candidates = new List<string>();
        if(!string.IsNullOrWhiteSpace(configuredPath))
        {
            string fileName = Path.GetFileName(configuredPath);
            candidates.Add(Path.Combine(AppConfig.ScriptsDir, fileName));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop", fileName));
        }

        // Friendly alias support
        if(displayName.Equals("Memory_Optimizer.ps1",StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(AppConfig.ScriptsDir, "Memory Optimizer.ps1"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Memory Optimizer.ps1"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop", "Memory Optimizer.ps1"));
        }

        foreach(var c in candidates)
            if(StorageUtil.FileExists(c)) return c;

        return configuredPath;
    }

    void LaunchScript(string file,string scriptPath,Button btn)
    {
        scriptPath = ResolveLaunchScriptPath(file, scriptPath);
        if(!StorageUtil.FileExists(scriptPath))
        {
            using var ofd=new OpenFileDialog{Title=$"Locate {file}",Filter="PowerShell Scripts (*.ps1)|*.ps1"};
            if(ShowOwnedFileDialog(ofd)!=DialogResult.OK)return;
            scriptPath=ofd.FileName;
        }

        bool interactive=file.Equals("HideFiles.ps1",StringComparison.OrdinalIgnoreCase);

        // EXACT pattern from original working Seiware — Verb=runas, UseShellExecute=true
        var psi=new ProcessStartInfo
        {
            FileName="powershell.exe",
            Arguments=$"-ExecutionPolicy Bypass -WindowStyle {(interactive?"Normal":"Hidden")} -File \"{scriptPath}\"",
            Verb="runas",
            UseShellExecute=true,
            WindowStyle=interactive?ProcessWindowStyle.Normal:ProcessWindowStyle.Hidden
        };

        try
        {
            // Suppress interceptor so it doesn't catch our own powershell.exe launch
            shellInterceptor.SuppressFor(15000);
            var proc=Process.Start(psi);
            running.Add(new RunningScript(file,proc));
            RefreshRunningPanel();
            var orig=btn.BackColor;btn.BackColor=Color.FromArgb(50,160,80);btn.Text="✔ Launched";
            var t=new System.Windows.Forms.Timer{Interval=2000};
            t.Tick+=(s,e)=>{t.Stop();t.Dispose();if(!btn.IsDisposed){btn.BackColor=orig;btn.Text="▶ Launch";}};
            t.Start();
        }
        catch(Exception ex)
        {
            string msg=ex.Message.Contains("1223")||ex.Message.ToLower().Contains("cancelled")?"[UAC cancelled]":$"[Error] {ex.Message}";
            ShowOwnedMessage(msg,"DreamLand",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
    }
    void StopAllScripts(){foreach(var r in running)r.Stop();running.Clear();RefreshRunningPanel();}
    void RefreshRunningPanel(){if(runningPanel==null)return;runningPanel.Controls.Clear();int py=2;foreach(var r in running.Where(r=>r.IsAlive)){runningPanel.Controls.Add(new Label{Text="● "+r.Name,AutoSize=true,Font=new Font("Consolas",8.5f),ForeColor=C_GREEN,Location=new Point(10,py),BackColor=Color.Transparent});py+=18;}if(py==2)runningPanel.Controls.Add(new Label{Text="No scripts running",AutoSize=true,Font=new Font("Segoe UI",8f),ForeColor=C_FG_DIM,Location=new Point(10,4),BackColor=Color.Transparent});runningPanel.Height=Math.Max(30,py+8);}
    void RebuildRegex(){if(cfg.BannedNames==null||cfg.BannedNames.Count==0){censorRegex=new Regex("(?!)");return;}censorRegex=new Regex(@"(?<![a-zA-Z0-9_])("+string.Join("|",cfg.BannedNames.Select(Regex.Escape))+@")(?![a-zA-Z0-9_])",RegexOptions.IgnoreCase);}
    void UpdateStatus(){int s=running.Count(r=>r.IsAlive);bool i=shellInterceptor?.IsEnabled??false;statusLabel.Text=$" ◈ {cfg.BannedNames.Count} censored │ {s} script(s) │ {cfg.CustomScripts.Count} custom │ Intercept: {(i?"ON ✓":"OFF")} │ Capture shield: ON";}
}

// ════════════════════════════════════════════════════════════════════════════
class SettingsForm : Form
{
    public List<string>BannedNames{get;private set;}ListBox listBox;TextBox nameInput;
    public SettingsForm(List<string>current){BannedNames=current;BuildUI();}
    void BuildUI(){Text="Manage Banned Names";Size=new Size(440,440);BackColor=Color.FromArgb(14,10,26);ForeColor=Color.FromArgb(220,215,240);Font=new Font("Segoe UI",9.5f);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterParent;Controls.Add(new Label{Text="🚫 Names/patterns to censor",Dock=DockStyle.Top,Height=40,Padding=new Padding(12,12,0,0),ForeColor=Color.FromArgb(160,130,255),Font=new Font("Segoe UI",10f,FontStyle.Bold),BackColor=Color.FromArgb(20,16,36)});listBox=new ListBox{Dock=DockStyle.Fill,BackColor=Color.FromArgb(12,10,22),ForeColor=Color.FromArgb(215,225,215),BorderStyle=BorderStyle.None,Font=new Font("Consolas",10f),SelectionMode=SelectionMode.MultiExtended};listBox.Items.AddRange(BannedNames.ToArray<object>());var ar=new Panel{Dock=DockStyle.Bottom,Height=40,BackColor=Color.FromArgb(20,16,36)};nameInput=new TextBox{PlaceholderText="Type a name...",BackColor=Color.FromArgb(28,20,48),ForeColor=Color.FromArgb(215,225,215),BorderStyle=BorderStyle.FixedSingle,Font=new Font("Consolas",9.5f),Location=new Point(10,8),Width=268};nameInput.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter)AddN();};var ab=AB("Add",new Point(286,8),Color.FromArgb(35,85,50));ab.Click+=(s,e)=>AddN();var rb=AB("Remove",new Point(356,8),Color.FromArgb(85,35,35));rb.Click+=(s,e)=>{foreach(var i in listBox.SelectedItems.Cast<string>().ToList())listBox.Items.Remove(i);};ar.Controls.AddRange(new Control[]{nameInput,ab,rb});var br=new Panel{Dock=DockStyle.Bottom,Height=46,BackColor=Color.FromArgb(16,12,28)};var ok=AB("Save",new Point(240,10),Color.FromArgb(35,75,120));ok.Click+=(s,e)=>{BannedNames=listBox.Items.Cast<string>().ToList();DialogResult=DialogResult.OK;Close();};var cc=AB("Cancel",new Point(318,10),Color.FromArgb(55,50,70));cc.Click+=(s,e)=>{DialogResult=DialogResult.Cancel;Close();};br.Controls.AddRange(new Control[]{ok,cc});Controls.Add(listBox);Controls.Add(ar);Controls.Add(br);}
    Button AB(string t,Point p,Color bg){var b=new Button{Text=t,FlatStyle=FlatStyle.Flat,BackColor=bg,ForeColor=Color.FromArgb(230,220,255),Size=new Size(68,26),Location=p,Cursor=Cursors.Hand,Font=new Font("Segoe UI",8.5f,FontStyle.Bold)};b.FlatAppearance.BorderSize=1;b.FlatAppearance.BorderColor=Color.FromArgb(80,70,140);b.FlatAppearance.MouseOverBackColor=ControlPaint.Light(bg,0.1f);return b;}
    void AddN(){string n=nameInput.Text.Trim();if(n.Length==0||listBox.Items.Contains(n))return;listBox.Items.Add(n);nameInput.Clear();}
}
