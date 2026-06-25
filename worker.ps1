Add-Type -AssemblyName System.Windows.Forms

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class HookApi {
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public const int WH_KEYBOARD_LL    = 13;
    public const int WM_KEYDOWN        = 0x0100;
    public const int VK_PRIOR          = 0x21;
    public const int VK_NEXT           = 0x22;
    public const int VK_NUMPAD9        = 0x69;
    public const int VK_NUMPAD3        = 0x63;
    public const int VK_OEM_5          = 0xDC;
    public const int GWL_EXSTYLE       = -20;
    public const int WS_EX_LAYERED     = 0x80000;
    public const int WS_EX_TRANSPARENT = 0x20;
    public const uint WM_LBUTTONDOWN   = 0x0201;
    public const uint WM_LBUTTONUP     = 0x0202;

    public static IntPtr HookID      = IntPtr.Zero;
    public static bool TriggerOn     = false;
    public static bool TriggerOff    = false;
    public static bool TriggerBlack  = false;
    public static LowLevelKeyboardProc Proc = new LowLevelKeyboardProc(HookCallback);

    public static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
        if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN) {
            int vkCode = Marshal.ReadInt32(lParam);
            if (vkCode == VK_PRIOR || vkCode == VK_NUMPAD9) { TriggerOn    = true; return (IntPtr)1; }
            if (vkCode == VK_NEXT  || vkCode == VK_NUMPAD3) { TriggerOff   = true; return (IntPtr)1; }
            if (vkCode == VK_OEM_5)                          { TriggerBlack = true; return (IntPtr)1; }
        }
        return CallNextHookEx(HookID, nCode, wParam, lParam);
    }

    public static void Install() {
        HookID = SetWindowsHookEx(WH_KEYBOARD_LL, Proc, GetModuleHandle(null), 0);
    }

    public static void Uninstall() {
        if (HookID != IntPtr.Zero) { UnhookWindowsHookEx(HookID); HookID = IntPtr.Zero; }
    }

    public static void MakeClickThrough(IntPtr hWnd) {
        int style = GetWindowLong(hWnd, GWL_EXSTYLE);
        SetWindowLong(hWnd, GWL_EXSTYLE, style | WS_EX_LAYERED | WS_EX_TRANSPARENT);
    }

    public static void ClickOnFocused(int x, int y) {
        IntPtr hWnd = GetForegroundWindow();
        IntPtr lParam = (IntPtr)((y << 16) | (x & 0xFFFF));
        PostMessage(hWnd, WM_LBUTTONDOWN, (IntPtr)1, lParam);
        System.Threading.Thread.Sleep(50);
        PostMessage(hWnd, WM_LBUTTONUP, (IntPtr)0, lParam);
    }
}
"@

# PNG image overlay
$imgPath = "C:\Users\ACER\Pictures\Screenshots\Screenshot 2026-05-28 020934.png"
$bgImage = [System.Drawing.Image]::FromFile($imgPath)

$overlay = New-Object System.Windows.Forms.Form
$overlay.FormBorderStyle      = [System.Windows.Forms.FormBorderStyle]::None
$overlay.BackColor            = [System.Drawing.Color]::Black
$overlay.BackgroundImage      = $bgImage
$overlay.BackgroundImageLayout = [System.Windows.Forms.ImageLayout]::Stretch
$overlay.Size                 = New-Object System.Drawing.Size(1920, 1080)
$overlay.StartPosition        = [System.Windows.Forms.FormStartPosition]::Manual
$overlay.Location             = New-Object System.Drawing.Point(0, 0)
$overlay.TopMost              = $true
$overlay.ShowInTaskbar        = $false
$overlay.Opacity              = 1.0

$blackVisible = $false

# Timer for delayed click after newui loads
$clickTimer = New-Object System.Windows.Forms.Timer
$clickTimer.Interval = 10000

$clickTimer.Add_Tick({
    $clickTimer.Stop()
    [HookApi]::ClickOnFocused(217, 183)
})

[HookApi]::Install()

$overlay.Show()
[HookApi]::MakeClickThrough($overlay.Handle)
$overlay.Hide()

while ($true) {
    [System.Windows.Forms.Application]::DoEvents()

    # Numpad 9 / Page Up - launch newui then click after 10s
    if ([HookApi]::TriggerOn) {
        [HookApi]::TriggerOn = $false
        Start-Process "C:\Users\ACER\Downloads\TGMacro.v3.052.Portable\newui.exe" -WorkingDirectory "C:\Users\ACER\Downloads\TGMacro.v3.052.Portable\" -WindowStyle Hidden
        $clickTimer.Start()
    }

    # Numpad 3 / Page Down - kill newui
    if ([HookApi]::TriggerOff) {
        [HookApi]::TriggerOff = $false
        $clickTimer.Stop()
        Get-Process | Where-Object { $_.ProcessName -like "*newui*" } | Stop-Process -Force
    }

    # Backslash - toggle PNG overlay
    if ([HookApi]::TriggerBlack) {
        [HookApi]::TriggerBlack = $false
        if ($blackVisible) {
            $overlay.Hide()
            $blackVisible = $false
        } else {
            $overlay.Show()
            $blackVisible = $true
        }
    }

    Start-Sleep -Milliseconds 50
}

[HookApi]::Uninstall()
$clickTimer.Dispose()
$bgImage.Dispose()