$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
dotnet build "$root\AdaptiveBarPC.sln" -c Release
