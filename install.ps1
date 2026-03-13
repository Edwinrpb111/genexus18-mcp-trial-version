# GeneXus 18 MCP Server - Universal Installer
# =========================================
# This script automates the build, extension packaging, and zero-config
# MCP setup for Claude Desktop, Cursor, and VS Code.

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "🚀 Starting Universal GeneXus MCP Installation..." -ForegroundColor Green

# 1. Build Backend
Write-Host "`n[1/4] Building MCP Backend (.NET 8 and .NET 4.8 STA)..." -ForegroundColor Cyan
try {
    & "$root\build.ps1"
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
} catch {
    Write-Host "❌ Failed to compile MCP backend. Ensure you have .NET 8 SDK installed." -ForegroundColor Red
    exit 1
}

# 2. Setup Extension (npm)
$extDir = Join-Path $root "src\nexus-ide"
Write-Host "`n[2/4] Packaging and Installing VS Code Extension..." -ForegroundColor Cyan
try {
    Set-Location $extDir

    Write-Host "   > Running npm install..."
    npm install --silent

    Write-Host "   > Compiling extension (vsce)..."
    npx vsce package --out nexus-ide.vsix

    Write-Host "   > Installing extension in VS Code..."
    code --install-extension nexus-ide.vsix --force

    Set-Location $root
    Write-Host "✅ Extension installed successfully!" -ForegroundColor Green
} catch {
    Write-Host "⚠️ Failed to install VS Code extension automatically (Node.js/VS Code CLI might be missing)." -ForegroundColor Yellow
    Write-Host "   You can still use the MCP Server purely for Claude/Cursor."
    Set-Location $root
}

# 3. Setup Claude Desktop
Write-Host "`n[3/4] Configuring Claude Desktop..." -ForegroundColor Cyan
$claudeConfig = "$env:APPDATA\Claude\claude_desktop_config.json"
$mcpBatPath = Join-Path $root "publish\start_mcp.bat"

if (Test-Path $mcpBatPath) {
    # Ensure Claude config file exists
    if (-not (Test-Path $claudeConfig)) {
        $claudeDir = Split-Path $claudeConfig
        if (-not (Test-Path $claudeDir)) { New-Item -ItemType Directory -Path $claudeDir | Out-Null }
        Set-Content $claudeConfig "{`"mcpServers`":{}}"
    }

    try {
        $configObj = Get-Content $claudeConfig | ConvertFrom-Json
        if (-not $configObj.mcpServers) {
            $configObj | Add-Member -MemberType NoteProperty -Name "mcpServers" -Value @{}
        }
        
        $configObj.mcpServers | Add-Member -MemberType NoteProperty -Name "genexus18" -Value @{
            "command" = $mcpBatPath
            "args" = @()
        } -Force

        $configObj | ConvertTo-Json -Depth 4 | Set-Content $claudeConfig
        Write-Host "✅ Claude Desktop configured successfully! Please restart Claude." -ForegroundColor Green
    } catch {
        Write-Host "⚠️ Failed to parse or update Claude Desktop config." -ForegroundColor Yellow
    }
}

# 4. Final Instructions
Write-Host "`n[4/4] Installation Complete!" -ForegroundColor Green
Write-Host "`nIf using Cursor/Cline, simply paste this snippet into your MCP setup:" -ForegroundColor Cyan
Write-Host "--------------------------------------------------------"
Write-Host "{`n  `"command`": `"$($mcpBatPath -replace '\\', '\\')`",`n  `"args`": []`n}"
Write-Host "--------------------------------------------------------"
Write-Host "`nDone! You can now open a GeneXus KB folder in VS Code or chat with Claude.`n"
