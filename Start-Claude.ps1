# Start Claude from the workspace root so local skills and hooks load.
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:CLAUDE_CODE_GIT_BASH_PATH = 'C:\Program Files\Git\bin\bash.exe'
$env:PATH = 'C:\Users\Master\.local\bin;C:\Program Files\Git\bin;C:\Program Files\Git\usr\bin;C:\Users\Master\AppData\Local\Programs\Python\Python312;' + $env:PATH
& 'C:\Users\Master\.local\bin\claude.exe' @args
