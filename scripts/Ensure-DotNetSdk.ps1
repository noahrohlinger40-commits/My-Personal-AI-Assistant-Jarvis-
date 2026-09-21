param(
    [string]$SdkVersion = "8.0.400",
    [string]$InstallDir = (Join-Path $PSScriptRoot "..\tools\dotnet")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedInstallDir = if ([System.IO.Path]::IsPathRooted($InstallDir))
{
    [System.IO.Path]::GetFullPath($InstallDir)
}
else
{
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $InstallDir))
}

$dotnetExe = Join-Path $resolvedInstallDir "dotnet.exe"

if (Test-Path $dotnetExe)
{
    $installed = & $dotnetExe --list-sdks 2>$null | Where-Object { $_ -like "$SdkVersion *" }
    if ($LASTEXITCODE -eq 0 -and $installed)
    {
        Write-Host ".NET SDK $SdkVersion already available at $resolvedInstallDir."
        exit 0
    }
}

$tmpDir = Join-Path $repoRoot ".tmpbuild"
$installScript = Join-Path $tmpDir "dotnet-install.ps1"

New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
New-Item -ItemType Directory -Force -Path $resolvedInstallDir | Out-Null

Write-Host "Downloading .NET SDK $SdkVersion bootstrapper..."
Invoke-WebRequest "https://dot.net/v1/dotnet-install.ps1" -OutFile $installScript

Write-Host "Installing .NET SDK $SdkVersion into $resolvedInstallDir..."
& $installScript -Version $SdkVersion -InstallDir $resolvedInstallDir -Architecture x64 -NoPath

if (-not (Test-Path $dotnetExe))
{
    throw "dotnet.exe was not created in $resolvedInstallDir."
}

$installed = & $dotnetExe --list-sdks 2>$null | Where-Object { $_ -like "$SdkVersion *" }
if ($LASTEXITCODE -ne 0 -or -not $installed)
{
    throw "The local .NET SDK install did not expose SDK $SdkVersion."
}

Write-Host ".NET SDK $SdkVersion is ready at $resolvedInstallDir."
