@echo off
REM serve-docs.bat - serve the ADRAPI documentation locally with docsify.
REM
REM Usage:
REM   serve-docs.bat [port]
REM
REM Defaults to port 3000. Requires Node.js. Uses a globally installed
REM docsify-cli if present, otherwise falls back to npx docsify-cli.

setlocal
set "SCRIPT_DIR=%~dp0"
set "DOCS_DIR=%SCRIPT_DIR%docs"
set "PORT=%~1"
if "%PORT%"=="" set "PORT=3000"

if not exist "%DOCS_DIR%" (
  echo error: docs directory not found at %DOCS_DIR% 1>&2
  exit /b 1
)

echo Serving ADRAPI docs from %DOCS_DIR% on http://localhost:%PORT%

where docsify >nul 2>&1
if %ERRORLEVEL%==0 (
  docsify serve "%DOCS_DIR%" --port %PORT%
  goto :eof
)

where npx >nul 2>&1
if %ERRORLEVEL%==0 (
  npx docsify-cli serve "%DOCS_DIR%" --port %PORT%
  goto :eof
)

echo error: neither 'docsify' nor 'npx' found on PATH. 1>&2
echo Install Node.js, then: npm i -g docsify-cli 1>&2
exit /b 1
