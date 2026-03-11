# == install.ps1 — install or update VsBridge == //
# Safe to run multiple times. Handles first install and updates identically.
# Usage:  .\install.ps1

$installDir   = "$env:USERPROFILE\.claude\VsBridge"
$settingsDir  = "$env:USERPROFILE\.claude"
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

# == Register MCP server globally via claude CLI == //
# The claude mcp add command writes to ~/.claude.json (user scope),
# making the server available in every Claude Code session.
Write-Host "  Registering MCP server (user scope)..." -ForegroundColor Cyan
claude mcp add --transport stdio --scope user vs-debugger -- $exePath 2>&1 | Out-Null

if ($LASTEXITCODE -eq 0)
{
    Write-Host "  MCP server registered globally" -ForegroundColor Green
}
else
{
    Write-Host "  Warning: claude mcp add failed. Register manually:" -ForegroundColor Yellow
    Write-Host "    claude mcp add --transport stdio --scope user vs-debugger -- `"$exePath`"" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "  Done. Restart Claude Code to pick up changes." -ForegroundColor Green
Write-Host ""