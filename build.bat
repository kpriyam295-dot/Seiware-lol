@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

echo ================================================
echo   Building DreamLand Suite
echo ================================================
echo.

if not exist "build" mkdir "build"
set "BUILD_DIR=%~dp0build"
set "FAIL=0"

:: ── 0. Copy icons into project folders so they can be embedded ──
if exist "terminal.ico" ( copy /y "terminal.ico" "src\" >nul 2>&1 )
if exist "cmdterminal.ico" ( copy /y "cmdterminal.ico" "src\" >nul 2>&1 & if not exist "src\terminal.ico" copy /y "cmdterminal.ico" "src\terminal.ico" >nul 2>&1 )
if exist "powershell.ico" ( copy /y "powershell.ico" "powershell\" >nul 2>&1 )

:: ── 1. Terminal.exe ──────────────────────────────
echo [1/5] Terminal.exe ...
cd /d "%~dp0src"
dotnet publish "CommandPrompt.csproj" -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o "%BUILD_DIR%"
if !ERRORLEVEL! neq 0 (
    echo.
    echo *** STEP 1 FAILED ***
    set "FAIL=1"
) else (
    echo        OK
)
echo.

:: ── 2. Windows PowerShell.exe ────────────────────
echo [2/5] Windows PowerShell.exe ...
cd /d "%~dp0"
if exist "powershell\WindowsPowerShell.csproj" (
    cd /d "%~dp0powershell"
    dotnet publish "WindowsPowerShell.csproj" -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o "%BUILD_DIR%"
    if !ERRORLEVEL! neq 0 (
        echo        Build failed, falling back to copy...
        goto :ps_copy
    ) else (
        echo        OK
        goto :ps_done
    )
) else (
    echo        No project found, falling back to copy...
)

:ps_copy
cd /d "%~dp0"
if exist "%BUILD_DIR%\Terminal.exe" (
    copy /y "%BUILD_DIR%\Terminal.exe" "%BUILD_DIR%\Windows PowerShell.exe" >nul 2>&1
    echo        OK - copied from Terminal.exe
) else (
    echo        FAILED - no Terminal.exe to copy from
    set "FAIL=1"
)

:ps_done
echo.

:: ── 3. DreamLandLauncher.exe ─────────────────────
echo [3/5] DreamLandLauncher.exe ...
cd /d "%~dp0launcher"
dotnet publish "DreamLandLauncher.csproj" -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o "%BUILD_DIR%"
if !ERRORLEVEL! neq 0 (
    echo.
    echo *** STEP 3 FAILED ***
    set "FAIL=1"
) else (
    echo        OK
)
echo.

:: ── 4. DreamLand.exe ─────────────────────────────
echo [4/5] DreamLand.exe ...
cd /d "%~dp0seiware"
dotnet publish "Seiware.csproj" -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o "%BUILD_DIR%"
if !ERRORLEVEL! neq 0 (
    echo.
    echo *** STEP 4 FAILED ***
    set "FAIL=1"
) else (
    echo        OK
)
echo.

:: ── 5. Copy assets ───────────────────────────────
echo [5/5] Copying scripts and assets ...
cd /d "%~dp0"
if exist "*.ps1" ( copy /y "*.ps1" "%BUILD_DIR%\" >nul 2>&1 && echo     PS1 scripts copied ) else ( echo     No .ps1 files found )
if exist "terminal.ico" ( copy /y "terminal.ico" "%BUILD_DIR%\" >nul 2>&1 && echo     terminal.ico copied )
if exist "cmdterminal.ico" ( copy /y "cmdterminal.ico" "%BUILD_DIR%\" >nul 2>&1 && echo     cmdterminal.ico copied )
if exist "powershell.ico" ( copy /y "powershell.ico" "%BUILD_DIR%\" >nul 2>&1 && echo     powershell.ico copied )
if exist "DreamLand.ico" ( copy /y "DreamLand.ico" "%BUILD_DIR%\" >nul 2>&1 && echo     DreamLand.ico copied )
if exist "Seiware.ico" ( copy /y "Seiware.ico" "%BUILD_DIR%\" >nul 2>&1 && echo     Seiware.ico copied )
echo.

:: ── Results ──────────────────────────────────────
echo ================================================
if !FAIL!==1 (
    echo   BUILD HAD ERRORS - check output above
) else (
    echo   BUILD COMPLETE
)
echo ================================================
echo.
for %%F in ("Terminal.exe" "Windows PowerShell.exe" "DreamLandLauncher.exe" "DreamLand.exe") do (
    if exist "%BUILD_DIR%\%%~F" (
        for %%A in ("%BUILD_DIR%\%%~F") do echo   [OK] %%~F  %%~zA bytes
    ) else (
        echo   [MISSING] %%~F
    )
)
echo.
echo   Output: %BUILD_DIR%\
echo.

:: THIS WILL NEVER CLOSE WITHOUT YOU SEEING IT
echo ================================================
echo   Press any key to close this window...
echo ================================================
pause >nul
