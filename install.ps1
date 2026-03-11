# == install.ps1 — install or update VsBridge == //
# Safe to run multiple times. Handles first install and updates identically.
# Usage:  .\install.ps1

$installDir   = "$env:USERPROFILE\.claude\VsBridge"
$settingsDir  = "$env:USERPROFILE\.claude"
$mcpFile      = "$settingsDir\.mcp.json"
$skillsDir    = "$settingsDir\skills"
$exePath      = "$installDir\VsBridge.exe"
$csproj       = "$PSScriptRoot\VsBridge\VsBridge.csproj"
$skillSrc     = "$PSScriptRoot\SKILL.md"
$skillDest    = "$skillsDir\vs-debugger.md"

Write-Host ""
Write-Host "=== VsBridge Install / Update ===" -ForegroundColor Cyan

# == Stop any running VsBridge before overwriting the exe == //
$running = Get-Process -Name "VsBridge" -ErrorAction SilentlyContinue
if ($running)
{
    Write-Host "  Stopping running VsBridge (PID $($running.Id))..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

# == Publish exe — dotnet publish creates the output dir automatically == //
Write-Host "  Publishing exe to $installDir ..." -ForegroundColor Cyan
dotnet publish $csproj -c Release -o $installDir --nologo -v quiet

if ($LASTEXITCODE -ne 0)
{
    Write-Host "  Publish failed. See errors above." -ForegroundColor Red
    exit 1
}

Write-Host "  Exe ready:     $exePath" -ForegroundColor Green

# == Copy SKILL.md — create skills dir if this is a first install == //
if (-not (Test-Path $skillsDir)) { New-Item -ItemType Directory -Force $skillsDir | Out-Null }
Copy-Item $skillSrc $skillDest -Force
Write-Host "  Skill updated: $skillDest" -ForegroundColor Green

# == Register in .mcp.json — the global MCP config Claude Code reads == //
# Read existing config or start from an empty object
if (Test-Path $mcpFile)
{
    $mcpConfig = Get-Content $mcpFile -Raw | ConvertFrom-Json
}
else
{
    Write-Host "  No .mcp.json found, creating one..." -ForegroundColor Yellow
    $mcpConfig = [PSCustomObject]@{}
}

# Add mcpServers node if missing
if (-not ($mcpConfig.PSObject.Properties.Name -contains "mcpServers"))
{
    $mcpConfig | Add-Member -NotePropertyName "mcpServers" -NotePropertyValue ([PSCustomObject]@{})
}

# Add or overwrite the vs-debugger entry
$entry = [PSCustomObject]@{ command = $exePath; args = @() }
if ($mcpConfig.mcpServers.PSObject.Properties.Name -contains "vs-debugger")
{
    $mcpConfig.mcpServers."vs-debugger" = $entry
    Write-Host "  MCP entry updated in .mcp.json" -ForegroundColor Green
}
else
{
    $mcpConfig.mcpServers | Add-Member -NotePropertyName "vs-debugger" -NotePropertyValue $entry
    Write-Host "  MCP entry added to .mcp.json" -ForegroundColor Green
}

# Write back with readable indentation
$mcpConfig | ConvertTo-Json -Depth 10 | Set-Content $mcpFile -Encoding UTF8

Write-Host ""
Write-Host "  Done. Restart Claude Code to pick up changes." -ForegroundColor Green
Write-Host ""