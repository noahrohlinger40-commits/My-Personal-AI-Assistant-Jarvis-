@echo off
setlocal

set "ROOT=%~dp0"
set "CONFIG=Release"
set "DOTNET_ROOT=%ROOT%tools\dotnet"
set "DOTNET_EXE=%DOTNET_ROOT%\dotnet.exe"
set "DOTNET_MULTILEVEL_LOOKUP=0"
set "DOTNET_CLI_HOME=%ROOT%.dotnet"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "NUGET_PACKAGES=%ROOT%.nuget\packages"

echo Ensuring local .NET SDK...
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%ROOT%scripts\Ensure-DotNetSdk.ps1"
if errorlevel 1 (
  echo Failed to provision the local .NET SDK.
  exit /b 1
)

if not exist "%DOTNET_EXE%" (
  echo Local dotnet executable not found at "%DOTNET_EXE%".
  exit /b 1
)

echo Building Jarvis solution...
"%DOTNET_EXE%" build "%ROOT%Jarvis.sln" -c %CONFIG% -m:1 /p:UseSharedCompilation=false %*
exit /b %errorlevel%
