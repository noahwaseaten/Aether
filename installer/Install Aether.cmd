@echo off
rem Installs Aether for the current user: %LOCALAPPDATA%\Aether, Start Menu and Desktop shortcuts. No admin needed.
rem Run it again to update or repair. Settings in the install folder are kept.
setlocal
set "SRC=%~dp0Aether.exe"
set "DEST=%LOCALAPPDATA%\Aether"

if not exist "%SRC%" (
    echo Aether.exe isn't next to this installer. Extract the whole zip first, then run this again.
    pause
    exit /b 1
)

tasklist /fi "imagename eq Aether.exe" | find /i "Aether.exe" >nul && (
    echo Closing the running Aether...
    taskkill /im Aether.exe /f >nul
    timeout /t 2 /nobreak >nul
)

if not exist "%DEST%" mkdir "%DEST%"
copy /y "%SRC%" "%DEST%\Aether.exe" >nul || (
    echo Couldn't copy Aether.exe to %DEST%.
    pause
    exit /b 1
)
copy /y "%~dp0Uninstall Aether.cmd" "%DEST%\Uninstall Aether.cmd" >nul

rem Windows marks downloaded files; clearing it stops the SmartScreen prompt on every start
powershell -NoProfile -ExecutionPolicy Bypass -Command "Unblock-File -LiteralPath (Join-Path $env:DEST 'Aether.exe')" >nul 2>&1

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$s = New-Object -ComObject WScript.Shell;" ^
  "foreach ($p in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) {" ^
  "  $l = $s.CreateShortcut((Join-Path $p 'Aether.lnk'));" ^
  "  $l.TargetPath = (Join-Path $env:DEST 'Aether.exe'); $l.WorkingDirectory = $env:DEST; $l.Description = 'Monster Hunter: World overlay'; $l.Save() }"

echo.
echo Aether is installed in %DEST%
echo Start it from the Start Menu or the Desktop shortcut. It updates itself from now on.
echo To remove it later, run "Uninstall Aether.cmd" in that folder.
echo.
start "" "%DEST%\Aether.exe"
pause
