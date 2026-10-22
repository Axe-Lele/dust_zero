$ErrorActionPreference = 'Stop'
$npmPath = 'C:\Program Files\nodejs\npm.cmd'
Push-Location (Join-Path $PSScriptRoot 'mcp-runtime')
try {
    & $npmPath ci --ignore-scripts --no-fund --no-audit
    if ($LASTEXITCODE -ne 0) { throw 'Playwright MCP restore failed.' }
} finally {
    Pop-Location
}
