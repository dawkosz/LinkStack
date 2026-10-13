@echo off
setlocal
cd /d "%~dp0"
if not exist dist mkdir dist
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 /optimize+ /utf8output /main:LinkStack.Tests /out:dist\LinkStack.Tests.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll /reference:System.Xml.Linq.dll src\*.cs tests\Tests.cs
if errorlevel 1 exit /b %errorlevel%
if "%~1"=="--build-only" exit /b 0
dist\LinkStack.Tests.exe
exit /b %errorlevel%
