@echo off
rem Starts the Moirai server on http://localhost:5000 with the given story, or w.sg.
rem Needs the ASP.NET Core 10 runtime: https://dotnet.microsoft.com/download/dotnet/10.0
cd /d "%~dp0"
if "%~1"=="" (dotnet MoiraiWebServer.dll w.sg) else (dotnet MoiraiWebServer.dll %*)
