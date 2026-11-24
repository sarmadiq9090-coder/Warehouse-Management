@echo off
rem Builds dist\Inventory Management Setup.exe (run build.bat first). Options are passed to build-setup.ps1 (-Release, -Tag vX.Y.Z, -Out file).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-setup.ps1" %*
exit /b %ERRORLEVEL%
