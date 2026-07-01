@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

echo ================================================
echo Building DreamLand Suite
echo ================================================
echo.

if not exist "build" mkdir "build"
set "BUILD_DIR=%~dp0build"
set "FAIL=0"

REM Copy icons into project folders for embedding
if exist "terminal.ico" copy /y "terminal.ico" "src\" >nul 2>&1
if exist "cmdterminal.ico" copy /y "cmdterminal.ico" "src\" >nul 2>&1
if exist "powershell.ico" copy /y "powershell.ico" "powershell\" >nul 2>&1

REM Copy UI into seiware project
if not exist "seiware\ui" mkdir "seiware\ui"
if exist "ui\index.html" (
    copy /y "ui\index.html" "seiware\ui\index.html" >nul 2>&1
    echo [UI] Copied ui\index.html
) else (
    if exist "seiware\ui\index.html" (
        echo [UI] seiware\ui\index.html already exists
    ) else (
        echo [UI ERROR] ui\index.html NOT FOUND
        set "FAIL=1"
    )
)
echo.

REM 1. Terminal.exe
echo [1/4] Terminal.exe ...
cd /d "%~dp0src"
dotnet publish "CommandPrompt.csproj" -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o "%BUILD_DIR%" >nul 2>&1
if !ERRORLEVEL! neq 0 (
    echo   FAILED
    set "FAIL=1"
) else (
    echo   OK
)
echo.

REM 2. Windows PowerShell.exe
echo [2/4] Windows PowerShell.exe ...
cd /d "%~dp0"
if exist "powershell\WindowsPowerShell.csproj" (
    cd /d "%~dp0powershell"
    dotnet publish "WindowsPowerShell.csproj" -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o "%BUILD_DIR%" >nul 2>&1
    if !ERRORLEVEL! neq 0 (
        echo   Build failed, copying from Terminal.exe...
        goto :ps_copy
    ) else (
        echo   OK
        goto :ps_done
    )
) else (
    echo   No project, copying from Terminal.exe...
)
:ps_copy
cd /d "%~dp0"
if exist "%BUILD_DIR%\Terminal.exe" (
    copy /y "%BUILD_DIR%\Terminal.exe" "%BUILD_DIR%\Windows PowerShell.exe" >nul 2>&1
    echo   OK - copied from Terminal.exe
) else (
    echo   FAILED
    set "FAIL=1"
)
:ps_done
echo.

REM 3. DreamLandLauncher.exe
echo [3/4] DreamLandLauncher.exe ...
cd /d "%~dp0"
if exist "launcher\DreamLandLauncher.csproj" (
    cd /d "%~dp0launcher"
    dotnet publish "DreamLandLauncher.csproj" -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o "%BUILD_DIR%" >nul 2>&1
    if !ERRORLEVEL! neq 0 (
        echo   FAILED
        set "FAIL=1"
    ) else (
        echo   OK
    )
) else (
    echo   Skipped
)
echo.

REM 4. DreamLand.exe
echo [4/4] DreamLand.exe ...
cd /d "%~dp0seiware"
dotnet publish "Seiware.csproj" -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o "%BUILD_DIR%"
if !ERRORLEVEL! neq 0 (
    echo   FAILED
    set "FAIL=1"
) else (
    echo   OK
)
echo.

REM 5. Copy assets
echo Copying assets ...
cd /d "%~dp0"
if exist "*.ps1" copy /y "*.ps1" "%BUILD_DIR%\" >nul 2>&1 && echo   .ps1 scripts
if exist "terminal.ico" copy /y "terminal.ico" "%BUILD_DIR%\" >nul 2>&1 && echo   terminal.ico
if exist "cmdterminal.ico" copy /y "cmdterminal.ico" "%BUILD_DIR%\" >nul 2>&1 && echo   cmdterminal.ico
if exist "powershell.ico" copy /y "powershell.ico" "%BUILD_DIR%\" >nul 2>&1 && echo   powershell.ico
if exist "DreamLand.ico" copy /y "DreamLand.ico" "%BUILD_DIR%\" >nul 2>&1 && echo   DreamLand.ico
if exist "Seiware.ico" copy /y "Seiware.ico" "%BUILD_DIR%\" >nul 2>&1 && echo   Seiware.ico
if not exist "%BUILD_DIR%\ui" mkdir "%BUILD_DIR%\ui"
if exist "seiware\ui\index.html" (
    copy /y "seiware\ui\index.html" "%BUILD_DIR%\ui\index.html" >nul 2>&1
    echo   ui\index.html
)
echo.

REM Results
echo ================================================
if "!FAIL!"=="1" (
    echo BUILD HAD ERRORS
) else (
    echo BUILD COMPLETE
)
echo ================================================
echo.
if exist "%BUILD_DIR%\Terminal.exe" ( echo   [OK] Terminal.exe ) else ( echo   [--] Terminal.exe )
if exist "%BUILD_DIR%\Windows PowerShell.exe" ( echo   [OK] Windows PowerShell.exe ) else ( echo   [--] Windows PowerShell.exe )
if exist "%BUILD_DIR%\DreamLandLauncher.exe" ( echo   [OK] DreamLandLauncher.exe ) else ( echo   [--] DreamLandLauncher.exe )
if exist "%BUILD_DIR%\DreamLand.exe" ( echo   [OK] DreamLand.exe ) else ( echo   [--] DreamLand.exe )
if exist "%BUILD_DIR%\ui\index.html" ( echo   [OK] ui\index.html ) else ( echo   [--] ui\index.html )
echo.
echo Output: %BUILD_DIR%\
echo.
echo Press any key to close...
pause >nul
