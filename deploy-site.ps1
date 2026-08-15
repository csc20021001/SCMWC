# Deploys site\ to Cloudflare Pages.
#
# Keep this file pure ASCII: PowerShell 5.1 reads BOM-less .ps1 as system ANSI.
#
# First run opens a browser to authorise your Cloudflare account, then remembers it.
# Nothing here needs a paid plan: Pages' free tier allows commercial use and has
# unlimited bandwidth for static assets.
#
#   .\deploy-site.ps1                 # deploy to project "scmwc"  -> scmwc.pages.dev
#   .\deploy-site.ps1 -Project myname # deploy to a different project name
#   .\deploy-site.ps1 -Preview        # preview branch instead of production
#
# The old project "screenwatch" (screenwatch-8in.pages.dev) is still live, but it
# now serves nothing except a redirect to scmwc.pages.dev - see site-redirect\.
# Deploy that one with:  .\deploy-site.ps1 -Project screenwatch  is WRONG, it would
# publish the real site there again. Use deploy-redirect.ps1 instead.

[CmdletBinding()]
param(
    [string]$Project = 'scmwc',
    [switch]$Preview
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$site = Join-Path $root 'site'

if (-not (Test-Path (Join-Path $site 'index.html'))) { throw "site\index.html not found" }

Get-ChildItem $site -File -Force |
    Select-Object Name, @{n = 'KB'; e = { [math]::Round($_.Length / 1KB, 1) } } |
    Format-Table -AutoSize

$branch = if ($Preview) { 'preview' } else { 'main' }
"deploying $site  ->  project '$Project'  (branch $branch)"
""

npx --yes wrangler@latest pages deploy $site --project-name $Project --branch $branch
