@echo off
rem Removes Aether, its settings and its shortcuts.
echo This removes Aether and its settings from %LOCALAPPDATA%\Aether.
pause
taskkill /im Aether.exe /f >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -Command "foreach ($p in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) { Remove-Item -LiteralPath (Join-Path $p 'Aether.lnk') -ErrorAction SilentlyContinue }"
echo Aether is uninstalled.
timeout /t 2 /nobreak >nul
cd /d "%TEMP%"
rem Deleting the folder this script runs from: (goto) ends the script first, then rmdir runs
(goto) 2>nul & rmdir /s /q "%LOCALAPPDATA%\Aether"
