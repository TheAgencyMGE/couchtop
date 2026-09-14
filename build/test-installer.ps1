<#
  End-to-end installer test for a published package (per-user, no admin).
  Installs into a temporary folder, verifies files/shortcuts/registration, uninstalls, verifies cleanup,
  and asserts the per-user Winlogon shell value was never changed.
  Usage: powershell -ExecutionPolicy Bypass -File build\test-installer.ps1 [-Package artifacts\Couchtop-1.0.0-win-x64]
#>
param([string]$Package)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Package) { $Package = Get-ChildItem (Join-Path $root 'artifacts') -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName }
$setup = Join-Path $Package 'Couchtop.Setup.exe'
if (-not (Test-Path $setup)) { throw "Setup not found in $Package" }

$failures = @()
function Check([bool]$condition, [string]$name) {
    if ($condition) { Write-Host "  PASS  $name" } else { Write-Host "  FAIL  $name" -ForegroundColor Red; $script:failures += $name }
}
function ShellValue { (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Winlogon' -Name Shell -ErrorAction SilentlyContinue).Shell }

$shellBefore = ShellValue
$target = Join-Path $env:TEMP ("CouchtopInstallTest-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Couchtop'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Couchtop'
if (Test-Path $uninstallKey) { throw 'Couchtop is already installed for this user; uninstall it before running this test.' }
if (Get-Process Couchtop, Couchtop.Guardian -ErrorAction SilentlyContinue) {
    throw 'Couchtop is running. Close it first: Setup stops running copies during install and uninstall.'
}

Write-Host "Installing to $target"
$p = Start-Process $setup -ArgumentList "--install --quiet --dir `"$target`" --no-desktop-shortcut --no-launch" -PassThru -Wait
Check ($p.ExitCode -eq 0) "install exit code 0 (was $($p.ExitCode))"
foreach ($file in 'Couchtop.exe', 'Couchtop.Guardian.exe', 'Couchtop.Recovery.exe', 'Couchtop.Setup.exe', 'Recover-Explorer.cmd') {
    Check (Test-Path (Join-Path $target $file)) "installed $file"
}
Check (Test-Path (Join-Path $startMenu 'Couchtop.lnk')) 'Start menu shortcut'
Check (Test-Path (Join-Path $startMenu 'Couchtop Recovery.lnk')) 'Recovery shortcut'
Check (Test-Path (Join-Path $startMenu 'Uninstall Couchtop.lnk')) 'Uninstall shortcut'
$entry = Get-ItemProperty $uninstallKey -ErrorAction SilentlyContinue
Check ($null -ne $entry -and $entry.InstallLocation -eq $target) 'Apps & features entry'
Check ((ShellValue) -eq $shellBefore) 'shell value unchanged after install'

$v = Start-Process (Join-Path $target 'Couchtop.Recovery.exe') -ArgumentList '--verify --quiet' -PassThru -Wait
Check ($v.ExitCode -eq 0) 'installed recovery tool self-test'

Write-Host 'Uninstalling'
$u = Start-Process (Join-Path $target 'Couchtop.Setup.exe') -ArgumentList '--uninstall --quiet' -PassThru -Wait
Check ($u.ExitCode -eq 0) "uninstall exit code 0 (was $($u.ExitCode))"
Check (-not (Test-Path $uninstallKey)) 'Apps & features entry removed'
Check (-not (Test-Path $startMenu)) 'Start menu folder removed'
Check ((ShellValue) -eq $shellBefore) 'shell value unchanged after uninstall'
$deadline = (Get-Date).AddSeconds(20)
while ((Test-Path $target) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
Check (-not (Test-Path $target)) 'program folder removed'

if ($failures.Count -gt 0) { Write-Host "$($failures.Count) installer check(s) failed" -ForegroundColor Red; exit 1 }
Write-Host 'Installer test passed' -ForegroundColor Green
