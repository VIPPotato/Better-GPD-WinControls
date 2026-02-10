param(
    [string]$RunDir = "",
    [string]$Operator = "",
    [string]$Date = "",
    [switch]$OpenAfterSave
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($RunDir))
{
    $latestRun = Get-ChildItem -Path "device-results" -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latestRun -eq $null)
    {
        throw "No run directories found under device-results."
    }
    $RunDir = $latestRun.FullName
}

if (-not (Test-Path $RunDir))
{
    throw "Run directory not found: $RunDir"
}

$checklistPath = Join-Path $RunDir "40_gui_manual_checklist.txt"

if ([string]::IsNullOrWhiteSpace($Date))
{
    $Date = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
}

if ([string]::IsNullOrWhiteSpace($Operator))
{
    $Operator = $env:USERNAME
}

$checks = @(
    "Launch GpdGui.exe successfully",
    "Reload from Device works",
    "Apply Changes works (no crash)",
    "Reset to Defaults loads from default_mappings.txt and writes to device",
    "New Profile with normal name works",
    "New Profile with invalid name (e.g. ..\bad) is rejected",
    "Load Profile writes to device",
    "Macro delay field accepts numeric values",
    "Macro delay field rejects non-numeric values with visible error",
    "Unknown/hex keycode value (example 0xEA) can be entered without silent failure",
    "LED mode and color changes apply"
)

function Read-CheckMark([string]$label)
{
    while ($true)
    {
        $answer = (Read-Host "$label`n  Enter y = pass, n = fail, s = skip").Trim().ToLowerInvariant()
        if ($answer -eq "y") { return "x" }
        if ($answer -eq "n") { return "!" }
        if ($answer -eq "s") { return " " }
        Write-Host "Invalid input. Please enter y, n, or s."
    }
}

$results = @()
$notes = @()

foreach ($check in $checks)
{
    $mark = Read-CheckMark $check
    $results += [PSCustomObject]@{
        Mark = $mark
        Text = $check
    }

    if ($mark -eq "!")
    {
        $note = Read-Host "  Failure note (required)"
        if ([string]::IsNullOrWhiteSpace($note))
        {
            $note = "No note provided."
        }
        $notes += "- $check : $note"
    }
}

$extraNotes = Read-Host "Additional notes (optional, press Enter to skip)"
if (-not [string]::IsNullOrWhiteSpace($extraNotes))
{
    $notes += "- $extraNotes"
}

$lines = @()
$lines += "GUI Manual Checklist"
$lines += "===================="
$lines += ""
$lines += "Date: $Date"
$lines += "Operator: $Operator"
$lines += ""

foreach ($result in $results)
{
    $lines += "[" + $result.Mark + "] " + $result.Text
}

$lines += ""
$lines += "Notes:"
if ($notes.Count -eq 0)
{
    $lines += "- None"
}
else
{
    $lines += $notes
}

Set-Content -Path $checklistPath -Value $lines
Write-Host "Checklist saved to: $checklistPath"

if ($OpenAfterSave)
{
    Start-Process -FilePath "notepad.exe" -ArgumentList $checklistPath
}
