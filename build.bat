@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

echo ================================================
echo   Building Seiware Suite
echo ================================================
echo.

if not exist "build" mkdir "build"

set "BUILD_DIR=%~dp0build"
set "SRC_PROJ="
set "PS_PROJ="
set "LAUNCHER_PROJ="

:: ── Find source projects ──────────────────────────
for %%F in ("%~dp0src\*.csproj") do (
    if not defined SRC_PROJ set "SRC_PROJ=%%~fF"
)
for %%F in ("%~dp0powershell\*.csproj") do (
    if not defined PS_PROJ set "PS_PROJ=%%~fF"
)
if not defined PS_PROJ (
    for %%F in ("%~dp0powershell-project\*.csproj") do (
        if not defined PS_PROJ set "PS_PROJ=%%~fF"
    )
)
for %%F in ("%~dp0launcher\*.csproj") do (
    if not defined LAUNCHER_PROJ set "LAUNCHER_PROJ=%%~fF"
)

:: ── 1. Command Prompt.exe ────────────────────────
echo [1/5] Command Prompt.exe ...
if not defined SRC_PROJ (
    echo        FAILED - no .csproj found in src\
    echo        Expected something like src\CommandPrompt.csproj
    pause
    exit /b 1
)
cd /d "%~dp0src"
dotnet publish "%SRC_PROJ%" -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o "%BUILD_DIR%" >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo        BUILD FAILED - showing output below
    dotnet publish "%SRC_PROJ%" -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o "%BUILD_DIR%"
    echo.
    pause
    exit /b 1
)
echo        OK

:: ── 2. Windows PowerShell.exe ────────────────────
echo [2/5] Windows PowerShell.exe ...
set "PSLOG=%BUILD_DIR%\powershell-build.log"
if exist "%PSLOG%" del /q "%PSLOG%" >nul 2>&1

if defined PS_PROJ (
    :: Extract the clean directory path without quotes
    for %%D in ("%PS_PROJ%") do set "PS_PROJ_DIR=%%~dpD"
    
    :: Safely switch to that directory
    cd /d "%~dp0"
    cd /d "!PS_PROJ_DIR!"
    
    dotnet publish "%PS_PROJ%" -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o "%BUILD_DIR%" >"%PSLOG%" 2>&1
    if %ERRORLEVEL% neq 0 (
        echo        BUILD FAILED - log saved to:
        echo        %PSLOG%
        echo ------------------------------------------------
        type "%PSLOG%"
        echo ------------------------------------------------
        echo        Press any key to fall back to copying Command Prompt.exe...
        pause >nul
        goto :ps_copy
    )
    echo        OK - compiled with powershell.ico from !PS_PROJ_DIR!
    goto :ps_done
) else (
    echo        No PowerShell project found - falling back to copy
)

:ps_copy
cd /d "%~dp0"
if exist "%BUILD_DIR%\Command Prompt.exe" (
    copy /y "%BUILD_DIR%\Command Prompt.exe" "%BUILD_DIR%\Windows PowerShell.exe" >nul 2>&1
    if %ERRORLEVEL% neq 0 (
        echo        COPY FAILED
        pause
        exit /b 1
    )
    echo        OK - copied from Command Prompt.exe, same embedded icon
) else (
    echo        FAILED - Command Prompt.exe missing, cannot create Windows PowerShell.exe
    pause
    exit /b 1
)

:ps_done

:: ── 3. SeiwareLauncher.exe ───────────────────────
echo [3/5] SeiwareLauncher.exe ...
if not defined LAUNCHER_PROJ echo        FAILED - no .csproj in launcher folder & pause & exit /b 1
cd /d "%~dp0launcher"
dotnet publish "!LAUNCHER_PROJ!" -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o "%BUILD_DIR%" >nul 2>&1
if !ERRORLEVEL! neq 0 (
    echo        BUILD FAILED - showing output below
    dotnet publish "!LAUNCHER_PROJ!" -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o "%BUILD_DIR%"
    echo.
    pause
    exit /b 1
)
echo        OK

:: ── 4. Seiware.exe ───────────────────────────────
echo [4/5] Seiware.exe ...
cd /d "%~dp0seiware"
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o "%BUILD_DIR%" >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo        BUILD FAILED - showing output below
    dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o "%BUILD_DIR%"
    echo.
    pause
    exit /b 1
)
echo        OK
echo.

:: ── 5. Copy PS1 scripts + icons ──────────────────
echo [5/5] Copying scripts and assets ...
cd /d "%~dp0"
if exist "*.ps1" (
    copy /y "*.ps1" "%BUILD_DIR%\" >nul 2>&1
    echo     PS1 scripts copied
) else (
    echo     No .ps1 files found next to build.bat
)
if exist "cmdterminal.ico" (
    copy /y "cmdterminal.ico" "%BUILD_DIR%\" >nul 2>&1
    echo     cmdterminal.ico copied
) else (
    echo     cmdterminal.ico not found optional
)
if exist "powershell.ico" (
    copy /y "powershell.ico" "%BUILD_DIR%\" >nul 2>&1
    echo     powershell.ico copied
) else (
    echo     powershell.ico not found optional
)
if exist "Seiware.ico" (
    copy /y "Seiware.ico" "%BUILD_DIR%\" >nul 2>&1
    echo     Seiware.ico copied
)
echo.

:: ── Verify ───────────────────────────────────────
echo ================================================
echo   RESULTS
echo ================================================
echo.
for %%F in ("Command Prompt.exe" "Windows PowerShell.exe" "SeiwareLauncher.exe" "Seiware.exe") do (
    if exist "%BUILD_DIR%\%%~F" (
        for %%A in ("%BUILD_DIR%\%%~F") do echo   [OK] %%~F  %%~zA bytes
    ) else (
        echo   [MISSING] %%~F
    )
)
echo.
echo   Icons:
if exist "%BUILD_DIR%\cmdterminal.ico" ( echo   [OK] cmdterminal.ico ) else ( echo   [--] cmdterminal.ico )
if exist "%BUILD_DIR%\powershell.ico"  ( echo   [OK] powershell.ico ) else ( echo   [--] powershell.ico )
echo.
echo   Scripts:
dir /b "%BUILD_DIR%\*.ps1" 2>nul || echo   [--] No PS1 scripts
echo.
echo   Output: %BUILD_DIR%\
if exist "%PSLOG%" echo   PowerShell build log: %PSLOG%
echo   Run Seiware.exe as Administrator.
echo.
cd /d "%~dp0"
pause
