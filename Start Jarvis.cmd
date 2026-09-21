@echo off
setlocal

set "ROOT=%~dp0"
set "CONFIG=Release"
set "DOTNET_ROOT=%ROOT%tools\dotnet"
set "DOTNET_EXE=%DOTNET_ROOT%\dotnet.exe"
set "APP_EXE=%ROOT%src\Jarvis.App\bin\%CONFIG%\net8.0-windows\Jarvis.App.exe"

echo Ensuring local .NET SDK...
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%ROOT%scripts\Ensure-DotNetSdk.ps1"
if errorlevel 1 (
  echo Failed to provision the local .NET SDK.
  pause
  exit /b 1
)

if not exist "%DOTNET_EXE%" (
  echo Local dotnet executable not found at "%DOTNET_EXE%".
  pause
  exit /b 1
)

echo Building Jarvis desktop app...
set "DOTNET_ROOT=%ROOT%tools\dotnet"
set "DOTNET_MULTILEVEL_LOOKUP=0"
set "DOTNET_CLI_HOME=%ROOT%.dotnet"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "NUGET_PACKAGES=%ROOT%.nuget\packages"
"%DOTNET_EXE%" build "%ROOT%src\Jarvis.App\Jarvis.App.csproj" -c %CONFIG% -m:1 /p:UseSharedCompilation=false >nul
if errorlevel 1 (
  echo Build failed. Close any running Jarvis window and try again.
  pause
  exit /b 1
)

if not exist "%APP_EXE%" (
  echo Built app executable not found at "%APP_EXE%".
  pause
  exit /b 1
)

powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$env:DOTNET_ROOT='%DOTNET_ROOT%'; $env:DOTNET_MULTILEVEL_LOOKUP='0'; Start-Process -WorkingDirectory '%ROOT%' -FilePath '%APP_EXE%'"
if errorlevel 1 (
  echo Failed to launch Jarvis through the local .NET runtime.
  pause
  exit /b 1
)

exit /b 0
