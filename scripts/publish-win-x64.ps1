$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
dotnet publish "$root\src\AdaptiveBarPC.App\AdaptiveBarPC.App.csproj" `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
Write-Host "Published under src\AdaptiveBarPC.App\bin\Release\net8.0-windows\win-x64\publish"
