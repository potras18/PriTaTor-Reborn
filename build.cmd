@echo off
rem Builds release\PriTaTorReborn.exe. Needs the .NET 8 SDK; the exe itself runs on .NET Framework 4.8.
setlocal
set ROOT=%~dp0
dotnet build "%ROOT%src\PriTaTor\PriTaTor.csproj" -c Release -nologo -v q || goto :fail
if not exist "%ROOT%release" mkdir "%ROOT%release"
copy /y "%ROOT%src\PriTaTor\bin\Release\PriTaTorReborn.exe" "%ROOT%release\" >nul || goto :fail
echo Built %ROOT%release\PriTaTorReborn.exe
exit /b 0
:fail
echo Build failed.
exit /b 1
