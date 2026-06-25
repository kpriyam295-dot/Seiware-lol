# HideFiles.ps1 - Make files invisible in Explorer and Search
# HOW TO RUN (as Administrator):
#   powershell -ExecutionPolicy Bypass -File "HideFiles.ps1"

# ADD YOUR FILE/FOLDER PATHS HERE
$targets = @(
    "C:\Users\ACER\Downloads\TGMacro.v3.051.Portable",
    "C:\Users\ACER\Downloads\TGMacro.v3.052.Portable",
    "C:\matcha"
)

function Hide-Items {
    foreach ($path in $targets) {
        $item = Get-Item -Path $path -Force -ErrorAction SilentlyContinue
        if ($item) {
            attrib +h +s $path
            attrib +h +s "$path\*.*" /S /D
            Write-Host "  [HIDDEN]  $path" -ForegroundColor Yellow
        } else {
            Write-Host "  [SKIP]    Not found: $path" -ForegroundColor DarkGray
        }
    }
}

function Show-Items {
    foreach ($path in $targets) {
        $item = Get-Item -Path $path -Force -ErrorAction SilentlyContinue
        if ($item) {
            attrib -h -s $path
            attrib -h -s "$path\*.*" /S /D
            Write-Host "  [VISIBLE] $path" -ForegroundColor Green
        } else {
            Write-Host "  [SKIP]    Not found: $path" -ForegroundColor DarkGray
        }
    }
}

function Exclude-FromIndex {
    foreach ($path in $targets) {
        $item = Get-Item -Path $path -Force -ErrorAction SilentlyContinue
        if ($item) {
            try {
                $regPath = "HKCU:\SOFTWARE\Microsoft\Windows Search\Gather\Windows\SystemIndex\Paths"
                if (-not (Test-Path $regPath)) {
                    New-Item -Path $regPath -Force | Out-Null
                }
                Set-ItemProperty -Path $regPath -Name $path -Value "exclude" -Force
                Write-Host "  [EXCLUDED FROM INDEX] $path" -ForegroundColor Cyan
            } catch {
                Write-Host "  [INDEX ERROR] $path - $_" -ForegroundColor Red
            }
        } else {
            Write-Host "  [SKIP] Not found: $path" -ForegroundColor DarkGray
        }
    }
    Restart-Service -Name "WSearch" -Force -ErrorAction SilentlyContinue
    Write-Host "  [SEARCH SERVICE RESTARTED]" -ForegroundColor Cyan
}

function Include-InIndex {
    foreach ($path in $targets) {
        try {
            $regPath = "HKCU:\SOFTWARE\Microsoft\Windows Search\Gather\Windows\SystemIndex\Paths"
            Remove-ItemProperty -Path $regPath -Name $path -ErrorAction SilentlyContinue
            Write-Host "  [RESTORED TO INDEX] $path" -ForegroundColor Green
        } catch {
            Write-Host "  [INDEX ERROR] $path - $_" -ForegroundColor Red
        }
    }
    Restart-Service -Name "WSearch" -Force -ErrorAction SilentlyContinue
    Write-Host "  [SEARCH SERVICE RESTARTED]" -ForegroundColor Cyan
}

function Get-Status {
    Write-Host ""
    Write-Host "Current status:" -ForegroundColor Cyan
    foreach ($path in $targets) {
        $item = Get-Item -Path $path -Force -ErrorAction SilentlyContinue
        if ($item) {
            $isHidden = $item.Attributes -band [System.IO.FileAttributes]::Hidden
            $isSystem = $item.Attributes -band [System.IO.FileAttributes]::System
            if ($isHidden -and $isSystem) {
                Write-Host "  [FULLY HIDDEN]  $path" -ForegroundColor Yellow
            } elseif ($isHidden) {
                Write-Host "  [PARTLY HIDDEN] $path" -ForegroundColor DarkYellow
            } else {
                Write-Host "  [VISIBLE]       $path" -ForegroundColor Green
            }
        } else {
            Write-Host "  [MISSING]       $path" -ForegroundColor Red
        }
    }
    Write-Host ""
}

Clear-Host
Write-Host "======================================" -ForegroundColor Cyan
Write-Host "      File Visibility Manager         " -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  [1]  Hide files + exclude from search index"
Write-Host "  [2]  Unhide files + restore to search index"
Write-Host "  [3]  Show current status"
Write-Host "  [Q]  Quit"
Write-Host ""
Write-Host "  NOTE: Must run as Administrator" -ForegroundColor DarkYellow
Write-Host ""

$choice = Read-Host "Choose an option"
switch ($choice.ToUpper()) {
    "1" {
        Write-Host ""
        Write-Host "Hiding files..." -ForegroundColor Yellow
        Hide-Items
        Write-Host ""
        Write-Host "Excluding from search index..." -ForegroundColor Yellow
        Exclude-FromIndex
        Write-Host ""
        Write-Host "Done!" -ForegroundColor Green
        Write-Host ""
    }
    "2" {
        Write-Host ""
        Write-Host "Unhiding files..." -ForegroundColor Yellow
        Show-Items
        Write-Host ""
        Write-Host "Restoring to search index..." -ForegroundColor Yellow
        Include-InIndex
        Write-Host ""
        Write-Host "Done!" -ForegroundColor Green
        Write-Host ""
    }
    "3" { Get-Status }
    "Q" { Write-Host ""; Write-Host "Bye!"; Write-Host ""; exit }
    default { Write-Host ""; Write-Host "Invalid option. Run the script again." -ForegroundColor Red; Write-Host "" }
}

Read-Host "Press Enter to close"