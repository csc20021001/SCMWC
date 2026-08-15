# Safe string replacement helper for localisation work.
#
# Keep this file pure ASCII: PowerShell 5.1 reads BOM-less .ps1 as system ANSI.
#
# WHY THE GUARDS EXIST
# PowerShell flattens @( @('a','b') ) into @('a','b') when there is exactly one
# inner array. A loop written as `foreach ($p in $pairs) { $t.Replace($p[0], $p[1]) }`
# then sees a plain string, and $p[0] is its FIRST CHARACTER. That turns an
# intended one-line edit into "replace every 't' with 'i' in the whole file".
# It happened. Three source files were wrecked before the build caught it.
#
# So: every pair is validated before anything is written.

function Invoke-Localise {
    param(
        [Parameter(Mandatory = $true)][string]$File,
        [Parameter(Mandatory = $true)][object[]]$Pairs
    )

    if (-not (Test-Path $File)) { throw "file not found: $File" }

    # Validate the whole batch first. Nothing is written unless all pairs are sane.
    foreach ($p in $Pairs) {
        if ($p -isnot [System.Array]) {
            throw "pair is not an array (PowerShell flattened it?). Use: ,@('from','to') or supply 2+ pairs."
        }
        if ($p.Count -ne 2) { throw "pair must have exactly 2 elements, got $($p.Count)" }
        if ($p[0] -isnot [string] -or $p[1] -isnot [string]) { throw "pair elements must be strings" }
        if ($p[0].Length -lt 8) { throw "search string too short, refusing: '$($p[0])'" }
    }

    $enc = New-Object System.Text.UTF8Encoding $false
    $text = [System.IO.File]::ReadAllText($File, [System.Text.Encoding]::UTF8)
    $before = $text.Length
    $missing = @()

    foreach ($p in $Pairs) {
        if ($text.Contains($p[0])) { $text = $text.Replace($p[0], $p[1]) }
        else { $missing += $p[0] }
    }

    [System.IO.File]::WriteAllText($File, $text, $enc)

    $name = Split-Path $File -Leaf
    if ($missing.Count -eq 0) {
        "  {0,-24} {1} replaced, {2} -> {3} chars" -f $name, $Pairs.Count, $before, $text.Length
    } else {
        "  {0,-24} {1}/{2} replaced, MISSED:" -f $name, ($Pairs.Count - $missing.Count), $Pairs.Count
        $missing | ForEach-Object { "      $_" }
    }
}
