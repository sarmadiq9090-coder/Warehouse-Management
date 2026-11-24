@echo off
chcp 65001 >nul
rem Builds the program with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK or NuGet needed.
rem x86 because the Access (Jet) database driver exists only in 32-bit.
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set OUT=%~1
if "%OUT%"=="" set OUT=%~dp0Inventory Management.exe
"%CSC%" /nologo /target:winexe /platform:x86 /optimize+ /codepage:65001 /out:"%OUT%" ^
  /win32icon:"%~dp0src\app.ico" ^
  /resource:"%~dp0src\Server\setup-server.ps1",setup-server.ps1 ^
  /resource:"%~dp0src\Update\apply-update.ps1",apply-update.ps1 /resource:"%~dp0src\app.ico",app.ico /resource:"%~dp0src\App\lang-en.txt",lang-en.txt /resource:"%~dp0tests\demo\empty-template.mdb",empty-template.mdb ^
  /r:System.dll /r:System.Core.dll /r:System.Data.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll ^
  /r:System.Security.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Web.Extensions.dll ^
  /recurse:"%~dp0src\*.cs"
exit /b %ERRORLEVEL%