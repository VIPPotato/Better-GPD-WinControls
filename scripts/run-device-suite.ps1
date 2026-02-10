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

$script:CommandResults = @{}

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

    $script:CommandResults[$Name] = [PSCustomObject]@{
        Name = $Name
        ExitCode = $exitCode
        StdoutPath = $stdoutPath
        StderrPath = $stderrPath
        MetaPath = $metaPath
    }
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

function Get-DumpFieldValue {
    param(
        [Parameter(Mandatory = $true)][string]$DumpPath,
        [Parameter(Mandatory = $true)][string]$FieldName
    )

    if (-not (Test-Path $DumpPath)) { return $null }
    $pattern = "^" + [Regex]::Escape($FieldName) + "\s*=\s*(.+?)\s+\("
    $line = Select-String -Path $DumpPath -Pattern $pattern | Select-Object -First 1
    if ($line -eq $null) { return $null }
    return $line.Matches[0].Groups[1].Value.Trim()
}

function Write-Assertions {
    $summaryPath = Join-Path $OutputDir "35_assertions.txt"
    $lines = New-Object System.Collections.Generic.List[string]
    $failures = 0

    $lines.Add("Assertions Summary")
    $lines.Add("==================")
    $lines.Add("timestamp_utc=$([DateTime]::UtcNow.ToString('o'))")
    $lines.Add("")

    foreach ($result in ($script:CommandResults.Values | Sort-Object Name))
    {
        if ($result.ExitCode -eq 0)
        {
            $lines.Add("[PASS] command '$($result.Name)' exit_code=0")
        }
        else
        {
            $lines.Add("[FAIL] command '$($result.Name)' exit_code=$($result.ExitCode)")
            $failures++
        }
    }

    if ($IncludeWriteTests)
    {
        $beforeDump = Join-Path $OutputDir "21_listdump.txt"
        $afterDump = Join-Path $OutputDir "34_list_after_write_tests.txt"
        $lines.Add("")
        $lines.Add("Write Persistence Checks")
        $lines.Add("------------------------")

        $checks = @(
            @{ Field = "ledmode"; Expected = "solid" },
            @{ Field = "colour"; Expected = "112233" },
            @{ Field = "l4delay1"; Expected = "123" },
            @{ Field = "l41"; Expected = "MOUSE_LEFT" }
        )

        foreach ($check in $checks)
        {
            $before = Get-DumpFieldValue -DumpPath $beforeDump -FieldName $check.Field
            $after = Get-DumpFieldValue -DumpPath $afterDump -FieldName $check.Field

            if ([string]::IsNullOrWhiteSpace($after))
            {
                $lines.Add("[FAIL] $($check.Field): not found in after dump")
                $failures++
                continue
            }

            if ($after -eq $check.Expected)
            {
                if ($before -ne $after)
                {
                    $lines.Add("[PASS] $($check.Field): '$before' -> '$after' (expected '$($check.Expected)')")
                }
                else
                {
                    $lines.Add("[PASS] $($check.Field): '$after' (already at expected value)")
                }
            }
            else
            {
                $lines.Add("[FAIL] $($check.Field): expected '$($check.Expected)', got '$after' (before '$before')")
                $failures++
            }
        }
    }

    $lines.Add("")
    if ($failures -eq 0)
    {
        $lines.Add("overall=PASS")
    }
    else
    {
        $lines.Add("overall=FAIL")
        $lines.Add("failure_count=$failures")
    }

    $lines | Set-Content -Path $summaryPath
    return $failures
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

if ((Write-Assertions) -gt 0)
{
    Write-Host "Assertions failed. See: $(Join-Path $OutputDir '35_assertions.txt')"
    if ($LaunchGui)
    {
        Start-Process -FilePath (Join-Path $repoRoot "GpdGui.exe")
    }
    Write-Host "Device suite complete. Results in: $OutputDir"
    Write-Host "After GUI checks, run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\fill-gui-checklist.ps1 -RunDir `"$OutputDir`""
    exit 1
}

Write-GuiChecklist

if ($LaunchGui)
{
    Start-Process -FilePath (Join-Path $repoRoot "GpdGui.exe")
}

Write-Host "Device suite complete. Results in: $OutputDir"
Write-Host "After GUI checks, run: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\fill-gui-checklist.ps1 -RunDir `"$OutputDir`""
