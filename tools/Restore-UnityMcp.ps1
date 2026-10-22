$ErrorActionPreference = 'Stop'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$runtimePath = Join-Path $workspaceRoot 'build\unity-mcp-tools'
$runtimePython = Join-Path $runtimePath 'Scripts\python.exe'
if (!(Test-Path -LiteralPath $runtimePython)) {
    & 'C:\Users\Master\AppData\Local\Programs\Python\Python312\python.exe' -m venv $runtimePath
    if ($LASTEXITCODE -ne 0) { throw 'Could not create MCP runtime.' }
}
& $runtimePython -m pip install --disable-pip-version-check --retries 1 --timeout 30 -r (Join-Path $PSScriptRoot 'unity-mcp-requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'MCP runtime installation failed.' }
