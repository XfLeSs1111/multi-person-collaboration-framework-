<#
.SYNOPSIS
Runs the Socket.Multiplayer test layers from the command line.

.DESCRIPTION
Layer 1: pure-logic dotnet tests (seconds; no editor required).
Layer 2: Unity EditMode (or PlayMode) tests via batchmode.

The Unity result is judged from the -testResults XML, NOT the process exit
code: Unity batchmode is known to exit 0 even when tests fail.

.PARAMETER PureOnly
Run only the dotnet layer (used by pre-commit).

.PARAMETER PlayMode
Switch the Unity layer from EditMode to PlayMode.
#>
param(
    [switch]$PureOnly,
    [switch]$PlayMode,
    [string]$UnityPath = "H:/unity2022.3.16f1/2022.3.16f1/Editor/Unity.exe",
    [string]$ProjectRoot = (Resolve-Path "$PSScriptRoot/..").Path
)

$ErrorActionPreference = 'Stop'

Write-Host '== Pure-logic tests (dotnet) =='
& dotnet test "$PSScriptRoot/PureLogic.Tests/PureLogic.Tests.csproj" --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host 'FAILED: pure-logic tests'; exit 1 }
if ($PureOnly) { exit 0 }

if (Test-Path "$ProjectRoot/Temp/UnityLockfile") {
    Write-Host 'FAILED: the Unity editor has this project open (Temp/UnityLockfile). Close it and retry.'
    exit 1
}

$platform = if ($PlayMode) { 'PlayMode' } else { 'EditMode' }
$results = Join-Path $ProjectRoot "TestResults/$platform.xml"
$log = Join-Path $ProjectRoot "TestResults/$platform.log"
New-Item -ItemType Directory -Force -Path (Join-Path $ProjectRoot 'TestResults') | Out-Null
Remove-Item $results -ErrorAction SilentlyContinue

Write-Host "== Unity $platform tests (batchmode) =="
& $UnityPath -batchmode -nographics -quit -projectPath $ProjectRoot `
    -runTests -testPlatform $platform -testResults $results -logFile $log

if (!(Test-Path $results)) {
    Write-Host "FAILED: Unity produced no testResults XML (compile error?). Log tail:"
    Get-Content $log -Tail 40
    exit 1
}

[xml]$xml = Get-Content $results
$run = $xml.'test-run'
$failed = $xml.SelectNodes('//test-case[@result="Failed"]')
Write-Host ("result={0} total={1} passed={2} failed={3}" -f $run.result, $run.total, $run.passed, $run.failed)

if ($run.result -ne 'Passed' -or $failed.Count -gt 0 -or [int]$run.total -eq 0) {
    foreach ($case in $failed) { Write-Host ('  FAILED: ' + $case.fullname) }
    exit 1
}

Write-Host 'All test layers passed.'
exit 0
