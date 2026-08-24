param(
    [string]$KompasRoot = 'C:\Program Files\ASCON\KOMPAS-3D v24 Home',
    [string]$ProjectRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

$rtwSource = Join-Path $ProjectRoot 'rtw\bin\KompasBambu.rtw'
$xmlSource = Join-Path $ProjectRoot 'rtw\KompasBambu.xml'
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
$legacyConfigPaths = @(
    'C:\ProgramData\ASCON\KOMPAS-3D\24\KompasBambu.kit.config',
    'C:\ProgramData\ASCON\KOMPAS-3D\24\KompasBambuDummy.kit.config'
)

if (-not (Test-Path $rtwSource)) {
    throw "RTW file is missing: $rtwSource. Run build-rtw.bat first."
}
if (-not (Test-Path $xmlSource)) {
    throw "RTW XML file is missing: $xmlSource."
}

New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

$rtwTarget = Join-Path $targetDir 'KompasBambu.rtw'
if (Test-Path $rtwTarget) {
    $sourceHash = (Get-FileHash -LiteralPath $rtwSource -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash -LiteralPath $rtwTarget -Algorithm SHA256).Hash
    if ($sourceHash -ne $targetHash) {
        Copy-Item -LiteralPath $rtwSource -Destination $rtwTarget -Force
    }
} else {
    Copy-Item -LiteralPath $rtwSource -Destination $rtwTarget -Force
}

$xmlTarget = Join-Path $targetDir 'KompasBambu.xml'
$xmlText = Get-Content -LiteralPath $xmlSource -Raw
[System.IO.File]::WriteAllText($xmlTarget, $xmlText, [System.Text.Encoding]::Unicode)

foreach ($file in $exporterFiles) {
    $source = Join-Path $ProjectRoot "dist\$file"
    if (-not (Test-Path $source)) {
        throw "Exporter file is missing: $source. Run publish first."
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $targetDir $file) -Force
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
if (Test-Path $userKitConfigPath) {
    $userXml = New-Object System.Xml.XmlDocument
    $userXml.PreserveWhitespace = $true
    $userXml.Load($userKitConfigPath)

    $duplicateNodes = @($userXml.SelectNodes("//Application[@id='KompasBambu.rtw']"))
    foreach ($node in $duplicateNodes) {
        [void]$node.ParentNode.RemoveChild($node)
    }

    if ($duplicateNodes.Count -gt 0) {
        $userXml.Save($userKitConfigPath)
    }
}

Write-Host "Installed RTW library to $targetDir"
Write-Host "Registered APP_KompasBambu in $kitConfigPath"
Write-Host "Restart KOMPAS to refresh the Applications menu."
