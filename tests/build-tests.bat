@echo off
chcp 65001 >nul
rem Builds the test programs (each = all program sources + one test file) into tests\bin.
rem Usage: tests\build-tests.bat
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set T=%~dp0
set S=%~dp0..\src
if not exist "%T%bin" mkdir "%T%bin"
set REFS=/r:System.dll /r:System.Core.dll /r:System.Data.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll /r:System.Security.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Web.Extensions.dll /r:Microsoft.CSharp.dll
set RES=/resource:"%S%\Server\setup-server.ps1",setup-server.ps1 /resource:"%S%\Update\apply-update.ps1",apply-update.ps1 /resource:"%S%\app.ico",app.ico /resource:"%S%\App\lang-en.txt",lang-en.txt /resource:"%T%demo\empty-template.mdb",empty-template.mdb
set ERR=0
call :one CoreTests Stores.TestMain CoreTests.cs
call :one YearCloseTests Stores.YcTestMain YearCloseTests.cs
call :one ExcelExportCheck Stores.XlCheckMain ExcelExportCheck.cs
call :one UiClickTest Stores.UiTestMain UiClickTest.cs
call :one UiShots Stores.UiShotsMain UiShots.cs
call :one ManualShots Stores.ManualShotsMain ManualShots.cs
call :one DocumentTests Stores.DocumentTestsMain DocumentTests.cs
call :one UnitsTests Stores.UnitsTestsMain UnitsTests.cs
call :one ServerLoginTests Stores.ServerLoginTestMain ServerLoginTests.cs
call :one OfflineSetupTests Stores.OfflineSetupTestsMain OfflineSetupTests.cs
call :one StockCountTests Stores.StockCountTestsMain StockCountTests.cs
call :one ItemEntryTests Stores.ItemEntryTestsMain ItemEntryTests.cs
call :one CostCentreTests Stores.CostCentreTestsMain CostCentreTests.cs
call :one RequisitionTests Stores.RequisitionTestsMain RequisitionTests.cs
call :one RecipientTests Stores.RecipientTestsMain RecipientTests.cs
call :one PurchasingTests Stores.PurchasingTestsMain PurchasingTests.cs
call :one WriteOffTests Stores.WriteOffTestsMain WriteOffTests.cs
call :one PeriodLockTests Stores.PeriodLockTestsMain PeriodLockTests.cs
call :one LangTests Stores.LangTestsMain LangTests.cs "%T%..\tools\ExtractStrings.cs"
call :one InstallerTests Stores.InstallerTestsMain InstallerTests.cs "%T%..\installer\*.cs"
call :one ValuationTests Stores.ValuationTestsMain ValuationTests.cs
call :one DemoData Stores.DemoMain DemoData.cs
call :one MakeTemplate Stores.MakeTemplateMain MakeTemplate.cs
exit /b %ERR%
:one
"%CSC%" /nologo /target:exe /platform:x86 /codepage:65001 /out:"%T%bin\%1.exe" /main:%2 %RES% %REFS% /recurse:"%S%\*.cs" "%T%%3" %4
if errorlevel 1 set ERR=1
exit /b 0
