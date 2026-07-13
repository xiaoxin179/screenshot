$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot 'release'
Remove-Item $output -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish (Join-Path $PSScriptRoot 'ScreenshotTool\ScreenshotTool.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
Remove-Item (Join-Path $output '*.pdb') -Force -ErrorAction SilentlyContinue
Write-Host "Created $output\ScreenshotTool.exe"
