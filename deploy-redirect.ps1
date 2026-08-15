# Deploys site-redirect\ to the OLD Cloudflare Pages project.
#
# Keep this file pure ASCII: PowerShell 5.1 reads BOM-less .ps1 as system ANSI.
#
# The product was renamed ScreenWatch -> SCMWC, and the site now lives at
# scmwc.pages.dev. The old project is deliberately kept alive serving nothing
# but a 301 to the new one, so that links posted before the rename - including
# any we cannot edit - still reach the site.
#
# Only run this if the redirect itself changes. The real site is deployed with
# deploy-site.ps1, which targets the new project.

[CmdletBinding()]
param(
    [string]$Project = 'screenwatch'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dir  = Join-Path $root 'site-redirect'

if (-not (Test-Path (Join-Path $dir '_redirects'))) { throw "site-redirect\_redirects not found" }

# Guard: publishing the real site here would undo the redirect.
if (Test-Path (Join-Path $dir 'zh.html')) {
    throw "site-redirect\ looks like the real site, refusing to deploy"
}

"deploying redirect stub  ->  project '$Project'"
npx --yes wrangler@latest pages deploy $dir --project-name $Project --branch main
