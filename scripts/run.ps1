$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
dotnet run --project "$root\src\AdaptiveBarPC.App\AdaptiveBarPC.App.csproj"
