@echo off
setlocal
cd /d "%~dp0"
set "CLAUDE_CODE_GIT_BASH_PATH=C:\Program Files\Git\bin\bash.exe"
set "PATH=C:\Users\Master\.local\bin;C:\Program Files\Git\bin;C:\Program Files\Git\usr\bin;C:\Users\Master\AppData\Local\Programs\Python\Python312;%PATH%"
"C:\Users\Master\.local\bin\claude.exe" %*
