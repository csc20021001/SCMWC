$ErrorActionPreference = 'Stop'
$testRoot = $PSScriptRoot
$applicationPath = Join-Path $testRoot 'bin\SCMWC.exe'
if (-not (Test-Path -LiteralPath $applicationPath)) {
    throw 'Build SCMWC first with .\build.ps1.'
}
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testSourcePath = Join-Path $testRoot 'tests\TriggerRegression.cs'
$testOutputPath = Join-Path $testRoot 'bin\TriggerRegression.exe'
& $compilerPath /nologo /target:exe /warn:4 /warnaserror+ "/out:$testOutputPath" `
    "/r:$applicationPath" /r:System.Drawing.dll $testSourcePath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $testOutputPath
exit $LASTEXITCODE
