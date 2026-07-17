#!/bin/sh
set -eu

# Heroku assigns $PORT at runtime; ASP.NET must listen on it.
export ASPNETCORE_URLS="http://0.0.0.0:${PORT:-8080}"
export ASPNETCORE_FORWARDEDHEADERS_ENABLED="${ASPNETCORE_FORWARDEDHEADERS_ENABLED:-true}"

exec dotnet AVI-Meals.Server.dll "$@"
