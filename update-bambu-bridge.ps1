$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$bridgeProject = Join-Path $projectRoot 'bridge\KompasBambu.Bridge.csproj'
$buildOutput = Join-Path $projectRoot 'bridge\dist'
$targetDir = Join-Path $env:LOCALAPPDATA 'KompasBambu'

& dotnet publish $bridgeProject -c Release -r win-x64 --self-contained false -o $buildOutput
if ($LASTEXITCODE -ne 0) { throw "Bridge build failed with exit code $LASTEXITCODE." }

New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
Copy-Item -Path (Join-Path $buildOutput '*') -Destination $targetDir -Force
Write-Host "Updated Bambu bridge: $targetDir\kompas-bambu-bridge.exe"
