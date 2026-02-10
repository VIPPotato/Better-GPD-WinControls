param(
    [string]$OutputDir = "",
    [switch]$IncludeWriteTests,
    [switch]$LaunchGui
)

$ErrorActionPreference = "Continue"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($OutputDir))
{
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputDir = Join-Path $repoRoot ("device-results\" + $stamp)
}

New-Item -Path $OutputDir -ItemType Directory -Force | Out-Null

function Write-MetaFile {
    $metaPath = Join-Path $OutputDir "00_environment.txt"
    $gitCommit = (git rev-parse --short HEAD 2>$null)
    if ([string]::IsNullOrWhiteSpace($gitCommit)) { $gitCommit = "unknown" }

    @(
        "timestamp_utc=$([DateTime]::UtcNow.ToString('o'))"
        "computer_name=$env:COMPUTERNAME"
        "user_name=$env:USERNAME"
        "git_commit=$gitCommit"
        "include_write_tests=$IncludeWriteTests"
    ) | Set-Content -Path $metaPath
}

function Run-GpdControl {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string[]]$Args = @()
    )

    $stdoutPath = Join-Path $OutputDir ($Name + ".stdout.txt")
    $stderrPath = Join-Path $OutputDir ($Name + ".stderr.txt")
    $metaPath = Join-Path $OutputDir ($Name + ".meta.txt")

    & ".\GpdControl.exe" @Args 1> $stdoutPath 2> $stderrPath
    $exitCode = $LASTEXITCODE

    @(
        "command=GpdControl.exe " + ($Args -join " ")
        "exit_code=$exitCode"
    ) | Set-Content -Path $metaPath
}

function Write-GuiChecklist {
    $path = Join-Path $OutputDir "40_gui_manual_checklist.txt"
    @"
GUI Manual Checklist
====================

Date:
Operator:

[ ] Launch GpdGui.exe successfully
[ ] Reload from Device works
[ ] Apply Changes works (no crash)
[ ] Reset to Defaults loads from default_mappings.txt and writes to device
[ ] New Profile with normal name works
[ ] New Profile with invalid name (e.g. ..\bad) is rejected
[ ] Load Profile writes to device
[ ] Macro delay field accepts numeric values
[ ] Macro delay field rejects non-numeric values with visible error
[ ] Unknown/hex keycode value (example 0xEA) can be entered without silent failure
[ ] LED mode and color changes apply

Notes:
"@ | Set-Content -Path $path
}

Write-MetaFile

# Non-destructive baseline captures
Run-GpdControl -Name "10_usage" -Args @()
Run-GpdControl -Name "20_list" -Args @("list")
Run-GpdControl -Name "21_listdump" -Args @("listdump", (Join-Path $OutputDir "21_listdump.txt"))
Run-GpdControl -Name "22_dumpraw" -Args @("dumpraw", (Join-Path $OutputDir "22_config.bin"))

if ($IncludeWriteTests)
{
    # These commands write to firmware config.
    Run-GpdControl -Name "30_set_ledmode" -Args @("set", "ledmode", "solid")
    Run-GpdControl -Name "31_set_colour" -Args @("set", "colour", "112233")
    Run-GpdControl -Name "32_set_macro_delay" -Args @("set", "l4delay1", "123")
    Run-GpdControl -Name "33_set_hex_key" -Args @("set", "l41", "0xEA")
    Run-GpdControl -Name "34_list_after_write_tests" -Args @("listdump", (Join-Path $OutputDir "34_list_after_write_tests.txt"))
}

Write-GuiChecklist

if ($LaunchGui)
{
    Start-Process -FilePath (Join-Path $repoRoot "GpdGui.exe")
}

Write-Host "Device suite complete. Results in: $OutputDir"
