param(
    [string]$KompasRoot = 'C:\Program Files\ASCON\KOMPAS-3D v24 Home',
    [string]$ProjectRoot = $PSScriptRoot,
    [switch]$SkipBuild,
    [switch]$NoElevate
)

$ErrorActionPreference = 'Stop'

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Quote-Argument([string]$Value) {
    return '"' + ($Value -replace '"', '\"') + '"'
}

if (-not (Test-IsAdministrator)) {
    if ($NoElevate) {
        throw "Administrator rights are required to install into KOMPAS Program Files and ProgramData folders."
    }

    $argumentList = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', (Quote-Argument $PSCommandPath),
        '-KompasRoot', (Quote-Argument $KompasRoot),
        '-ProjectRoot', (Quote-Argument $ProjectRoot)
    )

    if ($SkipBuild) {
        $argumentList += '-SkipBuild'
    }
    $argumentList += '-NoElevate'

    Write-Host "Administrator rights are required. Opening elevated PowerShell via UAC..."
    $process = Start-Process -FilePath 'powershell.exe' -ArgumentList $argumentList -Verb RunAs -Wait -PassThru
    if ($null -eq $process) {
        throw "UAC elevation was cancelled. Installation was not started."
    }
    if ($process.ExitCode -ne 0) {
        throw "Elevated installer failed with exit code $($process.ExitCode). Installation was not applied."
    }
    exit 0
}

$rtwSource = Join-Path $ProjectRoot 'rtw\bin\KompasBambu.rtw'
$xmlSource = Join-Path $ProjectRoot 'rtw\KompasBambu.xml'
$bridgeProject = Join-Path $ProjectRoot 'bridge\KompasBambu.Bridge.csproj'
$bridgeSourceDir = Join-Path $ProjectRoot 'bridge\dist'
$bridgeTargetDir = Join-Path $env:LOCALAPPDATA 'KompasBambu\Bridge'
$exporterFiles = @(
    'kompas-bambu.exe',
    'kompas-bambu.dll',
    'kompas-bambu.deps.json',
    'kompas-bambu.runtimeconfig.json',
    'kompas-bambu.dll.config'
)
$targetDir = Join-Path $KompasRoot 'Libs\KompasBambu'
$kitConfigPath = 'C:\ProgramData\ASCON\KOMPAS-3D\24\Base.kit.config'
$appId = 'APP_KompasBambu'
$relativeRtwPath = 'KompasBambu\KompasBambu.rtw'
$absoluteRtwPath = Join-Path $targetDir 'KompasBambu.rtw'
$legacyConfigPaths = @(
    'C:\ProgramData\ASCON\KOMPAS-3D\24\KompasBambu.kit.config',
    'C:\ProgramData\ASCON\KOMPAS-3D\24\KompasBambuDummy.kit.config'
)

$kompasProcesses = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ProcessName -match '^KOMPAS(?:-|$)'
})
if ($kompasProcesses.Count -gt 0) {
    $processList = $kompasProcesses | ForEach-Object { "$($_.ProcessName) (PID $($_.Id))" }
    throw "KOMPAS-3D IS OPEN: $($processList -join ', '). Close KOMPAS-3D completely before installation so its RTW library and UI cache can be updated."
}

if (-not $SkipBuild) {
    Push-Location $ProjectRoot
    try {
        & dotnet publish -c Release -r win-x64 --self-contained false -o dist
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE"
        }

        & (Join-Path $ProjectRoot 'build-rtw.bat')
        if ($LASTEXITCODE -ne 0) {
            throw "build-rtw.bat failed with exit code $LASTEXITCODE"
        }

        & dotnet publish $bridgeProject -c Release -r win-x64 --self-contained false -o $bridgeSourceDir
        if ($LASTEXITCODE -ne 0) {
            throw "Bambu Bridge build failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
}

if (-not (Test-Path $rtwSource)) {
    throw "RTW file is missing: $rtwSource. Run build-rtw.bat first."
}
if (-not (Test-Path $xmlSource)) {
    throw "RTW XML file is missing: $xmlSource."
}
if (-not (Test-Path $bridgeSourceDir)) {
    throw "Bambu Bridge build output is missing: $bridgeSourceDir."
}

# The bridge is an independent Windows application in the user profile. The
# RTW/exporter only talks to it through a local named-pipe command API.
New-Item -ItemType Directory -Path $bridgeTargetDir -Force | Out-Null
Copy-Item -Path (Join-Path $bridgeSourceDir '*') -Destination $bridgeTargetDir -Force

New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

$rtwTarget = Join-Path $targetDir 'KompasBambu.rtw'
if (Test-Path $rtwTarget) {
    $sourceHash = (Get-FileHash -LiteralPath $rtwSource -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash -LiteralPath $rtwTarget -Algorithm SHA256).Hash
    if ($sourceHash -ne $targetHash) {
        try {
            Copy-Item -LiteralPath $rtwSource -Destination $rtwTarget -Force
        }
        catch {
            throw "Cannot overwrite $rtwTarget. Close KOMPAS-3D and run this installer as Administrator. Original error: $($_.Exception.Message)"
        }
    }
} else {
    try {
        Copy-Item -LiteralPath $rtwSource -Destination $rtwTarget -Force
    }
    catch {
        throw "Cannot copy $rtwTarget. Run this installer as Administrator. Original error: $($_.Exception.Message)"
    }
}

$xmlTarget = Join-Path $targetDir 'KompasBambu.xml'
$xmlText = Get-Content -LiteralPath $xmlSource -Raw -Encoding UTF8
$xmlText = [regex]::Replace($xmlText, '^<\?xml[^?]*\?>', '<?xml version="1.0" encoding="utf-16"?>')
try {
    [System.IO.File]::WriteAllText($xmlTarget, $xmlText, [System.Text.Encoding]::Unicode)
}
catch {
    throw "Cannot write $xmlTarget. Close KOMPAS-3D and run this installer as Administrator. Original error: $($_.Exception.Message)"
}

foreach ($file in $exporterFiles) {
    $source = Join-Path $ProjectRoot "dist\$file"
    if (-not (Test-Path $source)) {
        throw "Exporter file is missing: $source. Run publish first."
    }
    $destination = Join-Path $targetDir $file
    try {
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }
    catch {
        throw "Cannot copy $destination. Close KOMPAS-3D and run this installer as Administrator. Original error: $($_.Exception.Message)"
    }
}

$xml = New-Object System.Xml.XmlDocument
$xml.PreserveWhitespace = $true
$xml.Load($kitConfigPath)

$existing = $xml.SelectSingleNode("//Application[@id='$appId']")
if ($null -eq $existing) {
    $groupsNode = $xml.SelectSingleNode('/Kit_Config/Groups')
    if ($null -eq $groupsNode) {
        throw "Cannot find /Kit_Config/Groups in $kitConfigPath"
    }

    $groupNode = $xml.SelectSingleNode('/Kit_Config/Groups/Group[Application]')
    if ($null -eq $groupNode) {
        $groupNode = $xml.CreateElement('Group')
        [void]$groupsNode.AppendChild($groupNode)
    }

    $appNode = $xml.CreateElement('Application')
    $attributes = [ordered]@{
        libType = 'lbt_rtw_ocx'
        path = $relativeRtwPath
        id = $appId
        libName = 'Bambu Studio'
        displayName = 'Bambu Studio'
        autostart = 'false'
    }

    foreach ($entry in $attributes.GetEnumerator()) {
        $attribute = $xml.CreateAttribute($entry.Key)
        $attribute.Value = $entry.Value
        [void]$appNode.Attributes.Append($attribute)
    }

    [void]$groupNode.AppendChild($appNode)
} else {
    $existing.SetAttribute('libType', 'lbt_rtw_ocx')
    $existing.SetAttribute('path', $relativeRtwPath)
    $existing.SetAttribute('libName', 'Bambu Studio')
    $existing.SetAttribute('displayName', 'Bambu Studio')
    $existing.SetAttribute('autostart', 'false')
}

$xml.Save($kitConfigPath)

foreach ($legacyConfigPath in $legacyConfigPaths) {
    Remove-Item -LiteralPath $legacyConfigPath -Force -ErrorAction SilentlyContinue
}

$userKitConfigPath = Join-Path $env:APPDATA 'ASCON\KOMPAS-3D\24\kHome.kit.config'
$userResourcesCachePath = Join-Path $env:APPDATA 'ASCON\KOMPAS-3D\24\resources.bin'
if (Test-Path $userKitConfigPath) {
    $userXml = New-Object System.Xml.XmlDocument
    $userXml.PreserveWhitespace = $true
    $userXml.Load($userKitConfigPath)

    $duplicateNodes = @($userXml.SelectNodes("//Application[@id='KompasBambu.rtw']"))
    foreach ($node in $duplicateNodes) {
        [void]$node.ParentNode.RemoveChild($node)
    }

    $userAppNode = $userXml.SelectSingleNode("//Application[@id='$appId']")
    if ($null -eq $userAppNode) {
        $userGroupsNode = $userXml.SelectSingleNode('/Kit_Config/Groups')
        if ($null -eq $userGroupsNode) {
            throw "Cannot find /Kit_Config/Groups in $userKitConfigPath"
        }

        $userGroupNode = $userXml.SelectSingleNode('/Kit_Config/Groups/Group[not(@groupName)]')
        if ($null -eq $userGroupNode) {
            $userGroupNode = $userXml.CreateElement('Group')
            [void]$userGroupsNode.PrependChild($userGroupNode)
        }

        $userAppNode = $userXml.CreateElement('Application')
        [void]$userGroupNode.AppendChild($userAppNode)
    }

    $userAttributes = [ordered]@{
        libType = 'lbt_rtw_ocx'
        path = $absoluteRtwPath
        id = $appId
        libName = 'Bambu Studio'
        displayName = 'Bambu Studio'
        autostart = 'true'
    }

    foreach ($entry in $userAttributes.GetEnumerator()) {
        $userAppNode.SetAttribute($entry.Key, $entry.Value)
    }

    $userXml.Save($userKitConfigPath)
}

$userAppPathsConfigPath = Join-Path $env:APPDATA 'ASCON\KOMPAS-3D\24\UI_AppPaths.config'
if (Test-Path $userAppPathsConfigPath) {
    $appPathsXml = New-Object System.Xml.XmlDocument
    $appPathsXml.PreserveWhitespace = $true
    $appPathsXml.Load($userAppPathsConfigPath)

    $staleAppNodes = @($appPathsXml.SelectNodes("//App[contains(@path, 'KompasBambuPlugin.dll')]"))
    foreach ($node in $staleAppNodes) {
        [void]$node.ParentNode.RemoveChild($node)
    }

    $productNode = $appPathsXml.SelectSingleNode('/XmlRoot/Product[@id="kHome"]')
    if ($null -ne $productNode) {
        $appPathNode = $appPathsXml.SelectSingleNode("//App[@id='$appId']")
        if ($null -eq $appPathNode) {
            $appPathNode = $appPathsXml.CreateElement('App')
            [void]$productNode.AppendChild($appPathNode)
        }

        $appPathNode.SetAttribute('id', $appId)
        $appPathNode.SetAttribute('path', $absoluteRtwPath)
    }

    $appPathsXml.Save($userAppPathsConfigPath)
}

if (Test-Path $userResourcesCachePath) {
    try {
        Remove-Item -LiteralPath $userResourcesCachePath -Force
        Write-Host "Removed KOMPAS UI resource cache: $userResourcesCachePath"
    }
    catch {
        throw "Cannot remove KOMPAS UI resource cache $userResourcesCachePath. Close KOMPAS-3D and retry. Original error: $($_.Exception.Message)"
    }
}

Write-Host "Installed RTW library to $targetDir"
Write-Host "Registered APP_KompasBambu in $kitConfigPath"
if ((Test-Path $userKitConfigPath) -or (Test-Path $userAppPathsConfigPath)) {
    Write-Host "Updated user-level KompasBambu registration in KOMPAS profile."
}
Write-Host "Installed commands:"
[xml]$installedAppXml = Get-Content -LiteralPath $xmlTarget -Raw -Encoding Unicode
$installedAppXml.SelectNodes('//appCommand') | ForEach-Object {
    "$($_.id):$($_.title)"
} | Sort-Object -Unique | ForEach-Object {
    $id, $title = $_ -split ':', 2
    Write-Host ("  {0}: {1}" -f $id, $title)
}
Write-Host "Restart KOMPAS to refresh the Applications menu."

Write-Host "Verifying installed KOMPAS integration..."
& (Join-Path $ProjectRoot 'check-rtw-install.ps1') -KompasRoot $KompasRoot -ProjectRoot $ProjectRoot
if ($LASTEXITCODE -ne 0) {
    throw "Installation verification failed. Do not open KOMPAS until check-rtw-install.ps1 succeeds."
}
Write-Host "INSTALLATION VERIFIED. You can start KOMPAS now."
