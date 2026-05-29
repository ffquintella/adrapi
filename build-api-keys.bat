@echo off
REM build-api-keys.bat - publish the adrapi-api-keys client as a standalone,
REM self-contained single-file executable (no .NET runtime required to run it).
REM
REM Usage:
REM   build-api-keys.bat [rid]
REM
REM `rid` is a .NET Runtime Identifier. Defaults to win-x64 if omitted.
REM Common values: win-x64  win-arm64  linux-x64  osx-arm64  osx-x64
REM
REM The executable is written to:
REM   artifacts\api-keys\<rid>\adrapi-api-keys.exe

setlocal
set "SCRIPT_DIR=%~dp0"
set "PROJECT=%SCRIPT_DIR%tools\AdrapiApiKeys\AdrapiApiKeys.csproj"
set "RID=%~1"
if "%RID%"=="" set "RID=win-x64"

set "OUT=%SCRIPT_DIR%artifacts\api-keys\%RID%"

echo Publishing adrapi-api-keys for %RID% -^> %OUT%
dotnet publish "%PROJECT%" -c Release -r %RID% --self-contained true -o "%OUT%"
if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%

echo.
echo Done. Run it directly:
echo   %OUT%\adrapi-api-keys.exe help
