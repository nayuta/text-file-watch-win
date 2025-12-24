param(
    [switch]$Fix
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet not found. Install the .NET SDK (Windows recommended for this WinForms app)."
}

dotnet build TextFileWatch.csproj -c Release

try {
    dotnet format --version | Out-Null
    if ($Fix) {
        dotnet format TextFileWatch.csproj
    }
    else {
        dotnet format TextFileWatch.csproj --verify-no-changes
    }
}
catch {
    Write-Warning "'dotnet format' not found; skipping formatting."
    Write-Warning "Install it with: dotnet tool install -g dotnet-format"
}
