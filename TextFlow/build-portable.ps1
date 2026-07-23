$ErrorActionPreference = 'Stop'

# Produces a self-contained, single-file EXE. No SDK/runtime is needed on the target PC.
$project = Join-Path $PSScriptRoot 'TextFlow.csproj'
$publish = Join-Path $PSScriptRoot 'publish\win-x64'

dotnet publish $project `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o $publish

Write-Host "Portable build: $publish\TextFlow.exe"
