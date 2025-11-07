@echo off
setlocal

net session >nul 2>&1 || (
  echo [!] Please run this script as Administrator.
  pause & exit /b 1
)

set "DOTNET_DIR=%ProgramFiles%\dotnet"
set "SCRIPT=%TEMP%\dotnet-install.ps1"

echo Downloading installer script...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile '%SCRIPT%'"

if not exist "%SCRIPT%" (
  echo [!] Download failed.
  exit /b 1
)

echo Installing .NET 8 SDK (system-wide)...
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -Channel 8.0 -InstallDir "%DOTNET_DIR%"

echo Adding to PATH for future sessions...
setx PATH "%DOTNET_DIR%;%PATH%" >nul

echo Verifying...
"%DOTNET_DIR%\dotnet.exe" --info

echo Done.
pause
endlocal

