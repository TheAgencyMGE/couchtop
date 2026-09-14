<#
  Builds a distributable, self-contained Couchtop package:
    artifacts\Couchtop-<version>-win-x64\        (run Couchtop.Setup.exe from here)
    artifacts\Couchtop-<version>-win-x64.zip
  Usage:  powershell -ExecutionPolicy Bypass -File build\publish.ps1 [-SkipTests] [-Runtime win-x64|win-arm64]
#>
param(
    [string]$Runtime = 'win-x64',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    [xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
    $group = $props.Project.PropertyGroup | Select-Object -First 1
    $version = if ($group.VersionSuffix) { "$($group.VersionPrefix)-$($group.VersionSuffix)" } else { $group.VersionPrefix }
    if (-not $version.StartsWith('0.')) { throw "Pre-release builds must use a 0.x version (found '$version')." }
    $name = "Couchtop-$version-$Runtime"
    $out = Join-Path $root "artifacts\$name"
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    New-Item -ItemType Directory -Force $out | Out-Null

    if (-not (Test-Path (Join-Path $root 'assets\Couchtop.ico'))) { & (Join-Path $PSScriptRoot 'make-icon.ps1') }

    if (-not $SkipTests) {
        dotnet build Couchtop.slnx -c Release -nologo
        if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
        dotnet test tests\Couchtop.Tests\Couchtop.Tests.csproj -c Release --no-build -nologo
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed; package not created.' }
    }

    $projects = @(
        @{ Path = 'src\Couchtop.App\Couchtop.App.csproj'; R2R = 'true' },
        @{ Path = 'src\Couchtop.Guardian\Couchtop.Guardian.csproj'; R2R = 'true' },
        @{ Path = 'src\Couchtop.Recovery\Couchtop.Recovery.csproj'; R2R = 'false' },
        @{ Path = 'src\Couchtop.Setup\Couchtop.Setup.csproj'; R2R = 'false' }
    )
    foreach ($p in $projects) {
        Write-Host "Publishing $($p.Path)..."
        dotnet publish $p.Path -c Release -r $Runtime --self-contained true -p:PublishReadyToRun=$($p.R2R) -p:DebugType=none -o $out -nologo
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($p.Path)" }
    }

    Copy-Item README.md, LICENSE, THIRD-PARTY-NOTICES.md -Destination $out -ErrorAction SilentlyContinue
    foreach ($required in 'Couchtop.exe', 'Couchtop.Guardian.exe', 'Couchtop.Recovery.exe', 'Couchtop.Setup.exe', 'Recover-Explorer.cmd') {
        if (-not (Test-Path (Join-Path $out $required))) { throw "Package is missing $required" }
    }

    $zip = Join-Path $root "artifacts\$name.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path "$out\*" -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "Package ready: $zip"

    # Portable package: same files minus Setup, plus the marker that keeps data in .\Data beside the exe.
    $portableName = "$name-portable"
    $portableOut = Join-Path $root "artifacts\$portableName"
    if (Test-Path $portableOut) { Remove-Item $portableOut -Recurse -Force }
    Copy-Item $out $portableOut -Recurse
    Get-ChildItem $portableOut -Filter 'Couchtop.Setup*' | Remove-Item -Force
    Set-Content -LiteralPath (Join-Path $portableOut 'portable.txt') -Encoding utf8 -Value @(
        'Couchtop portable',
        '',
        'This file makes Couchtop keep its channels, settings and logs in the Data folder next to Couchtop.exe.',
        'Delete it to use %LOCALAPPDATA%\Couchtop instead. Shell mode is only available in the installed version.'
    )
    if (Test-Path (Join-Path $portableOut 'Couchtop.Setup.exe')) { throw 'Portable package must not contain Setup' }

    $portableZip = Join-Path $root "artifacts\$portableName.zip"
    if (Test-Path $portableZip) { Remove-Item $portableZip -Force }
    Compress-Archive -Path "$portableOut\*" -DestinationPath $portableZip -CompressionLevel Optimal
    Write-Host "Portable package ready: $portableZip"
}
finally {
    Pop-Location
}
