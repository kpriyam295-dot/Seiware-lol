<# Paste this command to run it -> powershell -WindowStyle Hidden -ExecutionPolicy Bypass -File "C:\Users\izaan\Desktop\MuiCacheCleaner.ps1" #>
# MuiCache Cleaner - AHK-style Tray App + Regedit Find Spoofer (Win11 Native Look)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# Enable visual styles (CRITICAL for Win11 native checkbox/button rendering)
[System.Windows.Forms.Application]::EnableVisualStyles()
[System.Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)

# ── Gray-hover menu renderer ──────────────────────────────────────────────────
Add-Type -ReferencedAssemblies "System.Windows.Forms", "System.Drawing" -TypeDefinition @"
using System.Drawing;
using System.Windows.Forms;

public class GrayColorTable : ProfessionalColorTable {
    public override Color MenuItemSelected              { get { return Color.FromArgb(210, 210, 210); } }
    public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(210, 210, 210); } }
    public override Color MenuItemSelectedGradientEnd   { get { return Color.FromArgb(210, 210, 210); } }
    public override Color MenuItemPressedGradientBegin  { get { return Color.FromArgb(190, 190, 190); } }
    public override Color MenuItemPressedGradientMiddle { get { return Color.FromArgb(190, 190, 190); } }
    public override Color MenuItemPressedGradientEnd    { get { return Color.FromArgb(190, 190, 190); } }
    public override Color MenuItemBorder                { get { return Color.FromArgb(160, 160, 160); } }
    public override Color MenuBorder                    { get { return Color.FromArgb(160, 160, 160); } }
}
"@

# ── Native helpers ───────────────────────────────────────────────────────────
Add-Type -ReferencedAssemblies "System.Windows.Forms", "System.Drawing" -TypeDefinition @"
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public static class KeyHook {
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN     = 0x0100;
    public const int WM_SYSKEYDOWN  = 0x0104;
    public const int VK_CONTROL     = 0x11;
    public const int VK_F           = 0x46;

    public static IntPtr HookID = IntPtr.Zero;
    public static bool   Trigger = false;
    public static LowLevelKeyboardProc Proc = new LowLevelKeyboardProc(HookCallback);

    public static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)) {
            int vkCode = Marshal.ReadInt32(lParam);
            bool ctrl  = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            if (vkCode == VK_F && ctrl) {
                IntPtr fg = GetForegroundWindow();
                uint pid;
                GetWindowThreadProcessId(fg, out pid);
                try {
                    Process p = Process.GetProcessById((int)pid);
                    if (p.ProcessName.Equals("regedit", StringComparison.OrdinalIgnoreCase)) {
                        Trigger = true;
                        return (IntPtr)1;
                    }
                } catch { }
            }
        }
        return CallNextHookEx(HookID, nCode, wParam, lParam);
    }

    public static void Install() {
        HookID = SetWindowsHookEx(WH_KEYBOARD_LL, Proc, GetModuleHandle(null), 0);
    }

    public static void Uninstall() {
        if (HookID != IntPtr.Zero) {
            UnhookWindowsHookEx(HookID);
            HookID = IntPtr.Zero;
        }
    }
}

public static class WinApi {
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public const int SW_RESTORE = 9;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public static readonly IntPtr HWND_TOP = IntPtr.Zero;

    public static void ForceForeground(IntPtr hWnd) {
        ShowWindow(hWnd, SW_RESTORE);
        SetWindowPos(hWnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        BringWindowToTop(hWnd);
        SetForegroundWindow(hWnd);
    }

    public static void EnableRoundedCorners(IntPtr hWnd) {
        int pref = 2; // DWMWCP_ROUND
        try { DwmSetWindowAttribute(hWnd, 33, ref pref, sizeof(int)); } catch { }
    }
}
"@

function Get-RegeditWindow {
    $proc = Get-Process regedit -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($proc -and $proc.MainWindowHandle -ne 0) {
        return $proc.MainWindowHandle
    }
    return [IntPtr]::Zero
}

# ── Registry cleanup logic ────────────────────────────────────────────────────
function Invoke-MuiCacheCleanup {
    $paths = @(
        "HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache",
        "Registry::HKEY_CLASSES_ROOT\Local Settings\Software\Microsoft\Windows\Shell\MuiCache"
    )
    $targets  = @("app.exe", "loader.exe", "updater.exe", "map.exe", "newui.exe", "auth.exe")
    $deleted  = 0
    $hitNames = [System.Collections.Generic.List[string]]::new()

    foreach ($path in $paths) {
        if (Test-Path $path) {
            $props = Get-ItemProperty -Path $path
            foreach ($entry in $props.PSObject.Properties) {
                if ($entry.TypeNameOfValue -eq "System.String" -and $targets -contains $entry.Value) {
                    try {
                        Remove-ItemProperty -Path $path -Name $entry.Name -ErrorAction Stop
                        if (-not $hitNames.Contains($entry.Value)) { $hitNames.Add($entry.Value) }
                        $deleted++
                    } catch { }
                }
            }
        }
    }
    return $deleted, $hitNames
}

function Invoke-PrefetchCleanup {
    $prefetchDir = "C:\Windows\Prefetch"
    $keywords = @("mtx", "launcher", "extern", "update", "launch", "tm", "app", "load", "kr", "matrix", "matcha", "newui", "map", "auth")
    $deleted  = 0
    $hitNames = [System.Collections.Generic.List[string]]::new()

    $allFiles = Get-ChildItem -Path $prefetchDir -Filter "*.pf" -ErrorAction SilentlyContinue

    foreach ($file in $allFiles) {
        $baseName = ($file.Name -replace '-[^-]+\.pf$', '').ToLower()
        $matchedKeyword = $keywords | Where-Object { $baseName -like "*$_*" } | Select-Object -First 1

        if ($matchedKeyword) {
            try {
                Remove-Item -Path $file.FullName -Force -ErrorAction Stop
                if (-not $hitNames.Contains($file.Name)) { $hitNames.Add($file.Name) }
                $deleted++
            } catch { }
        }
    }

    return $deleted, $hitNames
}

function Invoke-RecentCleanup {
    $recentDir = [System.Environment]::GetFolderPath("Recent")
    $keywords  = @("mtx", "launcher", "extern", "update", "launch", "tm", "app", "load", "kr", "matrix", "matcha", "newui", "map", "auth", "config", "cfg", "version", ".dat", ".cfg", ".json", ".ps1", "auth", ".init", ".waypoint", ".cfg", ".png")
    $deleted   = 0
    $hitNames  = [System.Collections.Generic.List[string]]::new()

    $allFiles = Get-ChildItem -Path $recentDir -ErrorAction SilentlyContinue

    foreach ($file in $allFiles) {
        $baseName = ($file.Name -replace '\.lnk$', '').ToLower()
        $matched  = $keywords | Where-Object { $baseName -like "*$_*" } | Select-Object -First 1

        if ($matched) {
            try {
                Remove-Item -Path $file.FullName -Force -ErrorAction Stop
                if (-not $hitNames.Contains($baseName)) { $hitNames.Add($baseName) }
                $deleted++
            } catch { }
        }
    }

    return $deleted, $hitNames
}

function Get-Abbrev([string]$name) {
    ($name -split " " | ForEach-Object { $_[0].ToString().ToLower() }) -join ""
}

# ── Win11 font ───────────────────────────────────────────────────────────────
$win11Font = New-Object System.Drawing.Font("Segoe UI", 9)

# ── Load the custom find image from disk (no file-lock) ──────────────────────
$findImagePath = "C:\Users\izaan\Downloads\image-removebg-preview.png"
$findImage     = $null

if (Test-Path $findImagePath) {
    try {
        $bytes  = [System.IO.File]::ReadAllBytes($findImagePath)
        $stream = New-Object System.IO.MemoryStream(,$bytes)
        $findImage = [System.Drawing.Image]::FromStream($stream)
        $script:__findImageStream = $stream
    } catch {
        $findImage = $null
    }
}

# ── Find dialog ──────────────────────────────────────────────────────────────
function Show-FakeFindDialog {

    $find                 = New-Object System.Windows.Forms.Form
    $find.Text            = "Find"
    $find.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
    $find.MaximizeBox     = $false
    $find.MinimizeBox     = $false
    $find.StartPosition   = [System.Windows.Forms.FormStartPosition]::CenterScreen
    $find.Font            = $win11Font
    $find.BackColor       = [System.Drawing.Color]::FromArgb(243,243,243)
    $find.ShowInTaskbar   = $false
    $find.TopMost         = $false
    $find.ClientSize      = New-Object System.Drawing.Size(420,195)
    $find.KeyPreview      = $true

    $lbl = New-Object System.Windows.Forms.Label
    $lbl.Text = "Fi&nd what:"
    $lbl.UseMnemonic = $true
    $lbl.Location = New-Object System.Drawing.Point(14,17)
    $lbl.AutoSize = $true
    $find.Controls.Add($lbl)

    $tb = New-Object System.Windows.Forms.TextBox
    $tb.Location = New-Object System.Drawing.Point(82,13)
    $tb.Size = New-Object System.Drawing.Size(210,23)
    $find.Controls.Add($tb)

    $gb = New-Object System.Windows.Forms.GroupBox
    $gb.Text = "Look at"
    $gb.Location = New-Object System.Drawing.Point(14,46)
    $gb.Size = New-Object System.Drawing.Size(278,100)
    $gb.Font = $win11Font
    $find.Controls.Add($gb)

    $cbKeys = New-Object System.Windows.Forms.CheckBox
    $cbKeys.Text = "&Keys"
    $cbKeys.UseMnemonic = $true
    $cbKeys.Checked = $true
    $cbKeys.Location = New-Object System.Drawing.Point(14,22)
    $cbKeys.AutoSize = $true
    $cbKeys.FlatStyle = [System.Windows.Forms.FlatStyle]::System
    $gb.Controls.Add($cbKeys)

    $cbVals = New-Object System.Windows.Forms.CheckBox
    $cbVals.Text = "&Values"
    $cbVals.UseMnemonic = $true
    $cbVals.Checked = $true
    $cbVals.Location = New-Object System.Drawing.Point(14,46)
    $cbVals.AutoSize = $true
    $cbVals.FlatStyle = [System.Windows.Forms.FlatStyle]::System
    $gb.Controls.Add($cbVals)

    $cbData = New-Object System.Windows.Forms.CheckBox
    $cbData.Text = "&Data"
    $cbData.UseMnemonic = $true
    $cbData.Checked = $true
    $cbData.Location = New-Object System.Drawing.Point(14,70)
    $cbData.AutoSize = $true
    $cbData.FlatStyle = [System.Windows.Forms.FlatStyle]::System
    $gb.Controls.Add($cbData)

    $cbWhole = New-Object System.Windows.Forms.CheckBox
    $cbWhole.Text = "Match &whole string only"
    $cbWhole.UseMnemonic = $true
    $cbWhole.Location = New-Object System.Drawing.Point(14,160)
    $cbWhole.AutoSize = $true
    $cbWhole.FlatStyle = [System.Windows.Forms.FlatStyle]::System
    $find.Controls.Add($cbWhole)

    $cbSub = New-Object System.Windows.Forms.CheckBox
    $cbSub.Text = "Search in &subtree"
    $cbSub.UseMnemonic = $true
    $cbSub.Checked = $true
    $cbSub.Location = New-Object System.Drawing.Point(190,160)
    $cbSub.AutoSize = $true
    $cbSub.FlatStyle = [System.Windows.Forms.FlatStyle]::System
    $find.Controls.Add($cbSub)

    $btnFind = New-Object System.Windows.Forms.Button
    $btnFind.Text = "&Find Next"
    $btnFind.UseMnemonic = $true
    $btnFind.Location = New-Object System.Drawing.Point(305,11)
    $btnFind.Size = New-Object System.Drawing.Size(100,25)
    $btnFind.FlatStyle = [System.Windows.Forms.FlatStyle]::System
    $find.Controls.Add($btnFind)

    $find.AcceptButton = $btnFind

    $btnCancel = New-Object System.Windows.Forms.Button
    $btnCancel.Text = "Cancel"
    $btnCancel.Location = New-Object System.Drawing.Point(305,41)
    $btnCancel.Size = New-Object System.Drawing.Size(100,25)
    $btnCancel.FlatStyle = [System.Windows.Forms.FlatStyle]::System
    $btnCancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $find.Controls.Add($btnCancel)

    $find.CancelButton = $btnCancel

    $script:findClicked = $false

    $btnFind.Add_Click({
        $script:findClicked = $true
        $find.Close()
    })

    $find.Add_Shown({
        $tb.Focus() | Out-Null
        [WinApi]::EnableRoundedCorners($find.Handle)
    })

    $regHwnd = Get-RegeditWindow

    if ($regHwnd -ne [IntPtr]::Zero) {
        $owner = New-Object System.Windows.Forms.NativeWindow
        $owner.AssignHandle($regHwnd)
        [WinApi]::ForceForeground($regHwnd)
        [void]$find.ShowDialog($owner)
        $owner.ReleaseHandle()
    } else {
        [void]$find.ShowDialog()
    }

    $find.Dispose()
    return $script:findClicked
}

# ── Searching dialog (1–2 min random) ────────────────────────────────────────
function Show-FakeSearchingDialog {

    $s                 = New-Object System.Windows.Forms.Form
    $s.Text            = "Find"
    $s.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
    $s.MaximizeBox     = $false
    $s.MinimizeBox     = $false
    $s.StartPosition   = [System.Windows.Forms.FormStartPosition]::CenterScreen
    $s.Font            = $win11Font
    $s.BackColor       = [System.Drawing.Color]::FromArgb(243,243,243)
    $s.ShowInTaskbar   = $false
    $s.TopMost         = $false
    $s.ClientSize      = New-Object System.Drawing.Size(355,130)

    $picBox = New-Object System.Windows.Forms.PictureBox
    $picBox.Location  = New-Object System.Drawing.Point(14,16)
    $picBox.Size      = New-Object System.Drawing.Size(74,74)
    $picBox.SizeMode  = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
    $picBox.BackColor = [System.Drawing.Color]::Transparent

    if ($findImage) {
        $bmp  = New-Object System.Drawing.Bitmap $findImage
        $minX = $bmp.Width; $minY = $bmp.Height; $maxX = 0; $maxY = 0

        for ($x = 0; $x -lt $bmp.Width; $x++) {
            for ($y = 0; $y -lt $bmp.Height; $y++) {
                $pixel = $bmp.GetPixel($x, $y)
                if ($pixel.A -gt 10) {
                    if ($x -lt $minX) { $minX = $x }
                    if ($y -lt $minY) { $minY = $y }
                    if ($x -gt $maxX) { $maxX = $x }
                    if ($y -gt $maxY) { $maxY = $y }
                }
            }
        }

        $cropRect = New-Object System.Drawing.Rectangle($minX, $minY, ($maxX - $minX + 1), ($maxY - $minY + 1))
        $cropped  = $bmp.Clone($cropRect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $picBox.Image = $cropped
    } else {
        $picBox.Image = [System.Drawing.SystemIcons]::Information.ToBitmap()
    }

    $s.Controls.Add($picBox)

    $lbl          = New-Object System.Windows.Forms.Label
    $lbl.Text     = "Searching the registry..."
    $lbl.Location = New-Object System.Drawing.Point(90,34)
    $lbl.Size     = New-Object System.Drawing.Size(170,20)
    $lbl.Font     = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Regular)
    $lbl.TextAlign = [System.Drawing.ContentAlignment]::MiddleLeft
    $s.Controls.Add($lbl)

    $btn          = New-Object System.Windows.Forms.Button
    $btn.Text     = "Cancel"
    $btn.Location = New-Object System.Drawing.Point(248,90)
    $btn.Size     = New-Object System.Drawing.Size(85,25)
    $btn.FlatStyle = [System.Windows.Forms.FlatStyle]::System
    $s.Controls.Add($btn)

    $duration = Get-Random -Minimum 60000 -Maximum 120001
    $script:searchCompleted = $false

    $timer          = New-Object System.Windows.Forms.Timer
    $timer.Interval = $duration

    $timer.Add_Tick({
        $timer.Stop()
        $script:searchCompleted = $true
        $s.Close()
    })

    $btn.Add_Click({
        $timer.Stop()
        $script:searchCompleted = $false
        $s.Close()
    })

    $s.Add_FormClosing({ $timer.Stop() })

    $s.Add_Shown({
        $timer.Start()
        [WinApi]::EnableRoundedCorners($s.Handle)
    })

    $regHwnd = Get-RegeditWindow

    if ($regHwnd -ne [IntPtr]::Zero) {
        $owner = New-Object System.Windows.Forms.NativeWindow
        $owner.AssignHandle($regHwnd)
        [WinApi]::ForceForeground($regHwnd)
        [void]$s.ShowDialog($owner)
        $owner.ReleaseHandle()
    } else {
        [void]$s.ShowDialog()
    }

    $timer.Dispose()
    $s.Dispose()
    return $script:searchCompleted
}

# ── Finished dialog ──────────────────────────────────────────────────────────
function Show-FakeFinishedDialog {

    $regHwnd = Get-RegeditWindow

    if ($regHwnd -ne [IntPtr]::Zero) {
        $owner = New-Object System.Windows.Forms.NativeWindow
        $owner.AssignHandle($regHwnd)
        [WinApi]::ForceForeground($regHwnd)

        [System.Windows.Forms.MessageBox]::Show(
            $owner,
            "Finished searching through the registry.",
            "Registry Editor",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Information
        ) | Out-Null

        $owner.ReleaseHandle()
    } else {
        [System.Windows.Forms.MessageBox]::Show(
            "Finished searching through the registry.",
            "Registry Editor",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Information
        ) | Out-Null
    }
}

# ── Orchestrator ─────────────────────────────────────────────────────────────
function Invoke-FakeRegeditFindSequence {
    if (-not (Show-FakeFindDialog))      { return }
    if (-not (Show-FakeSearchingDialog)) { return }
    Show-FakeFinishedDialog
}

# ── Tray icon ────────────────────────────────────────────────────────────────
$ahkExe = "C:\Program Files\AutoHotkey\UX\AutoHotkeyUX.exe"
$icon   = [System.Drawing.Icon]::ExtractAssociatedIcon($ahkExe)

$tray         = New-Object System.Windows.Forms.NotifyIcon
$tray.Icon    = $icon
$tray.Text    = "seiware.ahk"
$tray.Visible = $true

$fNormal = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Regular)
$fBold   = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)

function New-DeadItem([string]$text, [bool]$bold = $false) {
    $i      = New-Object System.Windows.Forms.ToolStripMenuItem
    $i.Text = $text
    $i.Font = if ($bold) { $fBold } else { $fNormal }
    return $i
}

$menu                 = New-Object System.Windows.Forms.ContextMenuStrip
$menu.ShowImageMargin = $false
$menu.Font            = $fNormal
$grayRenderer         = New-Object System.Windows.Forms.ToolStripProfessionalRenderer((New-Object GrayColorTable))
$menu.Renderer        = $grayRenderer

$iOpen   = New-DeadItem "Open" $true
$iHelp   = New-DeadItem "Help"
$sep1    = New-Object System.Windows.Forms.ToolStripSeparator
$iWinSpy = New-DeadItem "Window Spy"

$iReload      = New-Object System.Windows.Forms.ToolStripMenuItem
$iReload.Text = "Reload This Script"
$iReload.Font = $fNormal
$iReload.Add_Click({
    $mui = Invoke-MuiCacheCleanup
    $pre = Invoke-PrefetchCleanup
    $rec = Invoke-RecentCleanup

    $muiCount = $mui[0]
    $preCount = $pre[0]
    $recCount = $rec[0]

    $allNames = [System.Collections.Generic.List[string]]::new()
    foreach ($n in $mui[1]) { if (-not $allNames.Contains($n)) { $allNames.Add($n) } }
    foreach ($n in $pre[1]) { if (-not $allNames.Contains($n)) { $allNames.Add($n) } }
    foreach ($n in $rec[1]) { if (-not $allNames.Contains($n)) { $allNames.Add($n) } }

    if ($allNames.Count -gt 0) {
        $abbrevs = ($allNames | ForEach-Object { Get-Abbrev $_ }) -join ", "
        $msg     = "reloaded items ($muiCount)($preCount)($recCount) $abbrevs"
    } else {
        $msg = "reloaded items (0)(0)(0)"
    }

    [System.Windows.Forms.MessageBox]::Show($msg, "seiware.ahk",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
})

$iEdit    = New-DeadItem "Edit This Script"
$sep2     = New-Object System.Windows.Forms.ToolStripSeparator
$iSuspend = New-DeadItem "Suspend Hotkeys"
$iPause   = New-DeadItem "Pause Script"

$iExit      = New-Object System.Windows.Forms.ToolStripMenuItem
$iExit.Text = "Exit"
$iExit.Font = $fNormal

$script:running = $true
$iExit.Add_Click({ $script:running = $false })

$menu.Items.AddRange(@($iOpen, $iHelp, $sep1, $iWinSpy, $iReload, $iEdit, $sep2, $iSuspend, $iPause, $iExit))
$tray.ContextMenuStrip = $menu

# ── Install hook ─────────────────────────────────────────────────────────────
[KeyHook]::Install()

# ── Message loop ─────────────────────────────────────────────────────────────
while ($script:running) {
    [System.Windows.Forms.Application]::DoEvents()

    if ([KeyHook]::Trigger) {
        [KeyHook]::Trigger = $false
        Invoke-FakeRegeditFindSequence
    }

    Start-Sleep -Milliseconds 30
}

# ── Cleanup ──────────────────────────────────────────────────────────────────
[KeyHook]::Uninstall()
if ($findImage) { $findImage.Dispose() }
if ($script:__findImageStream) { $script:__findImageStream.Dispose() }
$tray.Visible = $false
$tray.Dispose()
$icon.Dispose()