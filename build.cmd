@echo off
setlocal
cd /d "%~dp0"
if not exist dist mkdir dist
set "LINKSTACK_ICON="
if exist dist\LinkStack.ico set "LINKSTACK_ICON=/win32icon:dist\LinkStack.ico"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /utf8output %LINKSTACK_ICON% /out:dist\LinkStack.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll /reference:System.Xml.Linq.dll src\*.cs
exit /b %errorlevel%
