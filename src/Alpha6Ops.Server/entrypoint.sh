#!/bin/sh
# Apply pending account migrations, then serve. PORT is set by the host (Render); default for local docker runs.
set -e
: "${PORT:=8080}"
export ASPNETCORE_URLS="http://0.0.0.0:${PORT}"
dotnet Alpha6Ops.Server.dll --migrate-accounts
exec dotnet Alpha6Ops.Server.dll
