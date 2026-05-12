# GeneXus MCP - Release Script (maintainer-only)
# ==========================================
# Usage:
#   .\scripts\release.ps1 patch   # 2.1.2 -> 2.1.3
#   .\scripts\release.ps1 minor   # 2.1.2 -> 2.2.0
#   .\scripts\release.ps1 major   # 2.1.2 -> 3.0.0
#
# Pre-reqs: .NET 8 SDK, GeneXus 18 installed, `gh auth status` ok,
# clean working tree, on main branch.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({
        if ($_ -in @('patch', 'minor', 'major')) { return $true }
        if ($_ -match '^\d+\.\d+\.\d+(-[\w.\-]+)?$') { return $true }
        throw "BumpType must be 'patch', 'minor', 'major', or an explicit semver like '2.1.2'."
    })]
    [string]$BumpType
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
Push-Location $root

try {
    Write-Host "[release] Pre-flight checks..." -ForegroundColor Cyan

    if ((git rev-parse --abbrev-ref HEAD) -ne 'main') {
        throw "Must be on 'main' branch."
    }
    if ((git status --porcelain)) {
        throw "Working tree not clean. Commit or stash changes first."
    }
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI (gh) not found in PATH."
    }
    & gh auth status 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "gh not authenticated. Run 'gh auth login'." }

    Write-Host "[release] Pulling latest main..." -ForegroundColor Cyan
    git pull --ff-only origin main

    Write-Host "[release] Bumping version ($BumpType)..." -ForegroundColor Cyan
    npm version $BumpType --no-git-tag-version | Out-Null
    $newVersion = (Get-Content "$root\package.json" -Raw | ConvertFrom-Json).version
    $tag = "v$newVersion"
    Write-Host "   > New version: $newVersion" -ForegroundColor Gray

    Write-Host "[release] Building .NET artifacts (build.ps1)..." -ForegroundColor Cyan
    & "$root\build.ps1"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[release] Build failed; reverting version bump." -ForegroundColor Red
        git checkout -- package.json 2>$null
        if (Test-Path "$root\package-lock.json") { git checkout -- package-lock.json 2>$null }
        throw "build.ps1 failed."
    }

    $publishDir = Join-Path $root 'publish'
    $gatewayExe = Join-Path $publishDir 'GxMcp.Gateway.exe'
    if (-not (Test-Path $gatewayExe)) {
        throw "Expected $gatewayExe after build; aborting."
    }

    Write-Host "[release] Creating publish.zip..." -ForegroundColor Cyan
    $zipPath = Join-Path $root 'publish.zip'
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force
    $zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
    Write-Host "   > publish.zip: $zipSize MB" -ForegroundColor Gray

    Write-Host "[release] Committing version bump and tagging..." -ForegroundColor Cyan
    git add package.json
    if (Test-Path "$root\package-lock.json") { git add package-lock.json }
    git commit -m "chore(release): $tag"
    git tag $tag
    git push origin main --follow-tags

    Write-Host "[release] Creating GitHub Release with asset..." -ForegroundColor Cyan
    & gh release create $tag $zipPath --title $tag --generate-notes
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed." }

    Write-Host "[release] Release created. Workflow 'release.yml' will publish to npm with provenance." -ForegroundColor Green
    Write-Host "   > Watch:  gh run watch" -ForegroundColor Gray
    Write-Host "   > Verify: npm view genexus-mcp@$newVersion dist" -ForegroundColor Gray

    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
}
finally {
    Pop-Location
}
