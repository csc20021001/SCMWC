$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out  = Join-Path $root 'bin\ScreenWatch.exe'

New-Item -ItemType Directory -Force (Join-Path $root 'bin') | Out-Null

# csc cannot overwrite a running exe, so stop our own processes first.
$running = Get-Process ScreenWatch, ProgressDemo -ErrorAction SilentlyContinue
if ($running) {
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 700
    "stopped: " + (($running | ForEach-Object { $_.ProcessName }) -join ', ')
}
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }

# Keep this script pure ASCII: PowerShell 5.1 reads BOM-less .ps1 as system ANSI,
# so non-ASCII comments here can corrupt line continuations.
# /codepage:65001 tells the old csc.exe the C# sources are UTF-8; without it,
# Chinese string literals break on machines whose ANSI codepage is not UTF-8.
& $csc /nologo /target:winexe /optimize+ /warn:3 /codepage:65001 `
    /out:$out `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    $sources

if ($LASTEXITCODE -ne 0) { "build FAILED ($LASTEXITCODE)"; exit 1 }
"built  $out"

# Test target: a window with a running progress bar.
$demoOut = Join-Path $root 'bin\ProgressDemo.exe'
& $csc /nologo /target:winexe /optimize+ /warn:3 /codepage:65001 `
    /out:$demoOut `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    (Join-Path $root 'demo\ProgressDemo.cs')

if ($LASTEXITCODE -ne 0) { "demo build FAILED ($LASTEXITCODE)"; exit 1 }
"built  $demoOut"

# Single-file installer with the two exes and the docs embedded as resources.
$setupOut = Join-Path $root 'dist\ScreenWatch-Setup.exe'
New-Item -ItemType Directory -Force (Join-Path $root 'dist') | Out-Null
& $csc /nologo /target:winexe /optimize+ /warn:3 /codepage:65001 `
    /out:$setupOut `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    "/resource:$out,ScreenWatch.exe" `
    "/resource:$(Join-Path $root 'installer\EULA.txt'),EULA.txt" `
    "/resource:$(Join-Path $root 'installer\EULA-en.txt'),EULA-en.txt" `
    (Join-Path $root 'installer\Setup.cs')

if ($LASTEXITCODE -eq 0) {
    $kb = [math]::Round((Get-Item $setupOut).Length / 1KB)
    "built  $setupOut  ($kb KB)"
} else { "setup build FAILED ($LASTEXITCODE)" }
