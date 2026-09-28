#!/bin/sh
# Starts the Moirai server on http://localhost:5000 with the given story, or w.sg.
# Needs the ASP.NET Core 10 runtime: https://dotnet.microsoft.com/download/dotnet/10.0
cd "$(dirname "$0")" || exit 1
[ $# -eq 0 ] && set -- w.sg
exec dotnet MoiraiWebServer.dll "$@"
