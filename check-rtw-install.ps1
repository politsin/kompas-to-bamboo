param(
    [string]$KompasRoot = 'C:\Program Files\ASCON\KOMPAS-3D v24 Home',
    [string]$ProjectRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

$targetDir = Join-Path $KompasRoot 'Libs\KompasBambu'
$installedXmlPath = Join-Path $targetDir 'KompasBambu.xml'
$installedRtwPath = Join-Path $targetDir 'KompasBambu.rtw'
$installedExePath = Join-Path $targetDir 'kompas-bambu.exe'
$sourceXmlPath = Join-Path $ProjectRoot 'rtw\KompasBambu.xml'
$sourceRtwPath = Join-Path $ProjectRoot 'rtw\bin\KompasBambu.rtw'
$kitConfigPath = 'C:\ProgramData\ASCON\KOMPAS-3D\24\Base.kit.config'
$appId = 'APP_KompasBambu'

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

foreach ($path in @($installedXmlPath, $installedRtwPath, $installedExePath, $sourceXmlPath, $sourceRtwPath, $kitConfigPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        Fail "Missing required file: $path"
    }
}

[xml]$sourceXml = Get-Content -LiteralPath $sourceXmlPath -Raw -Encoding UTF8
[xml]$installedXml = Get-Content -LiteralPath $installedXmlPath -Raw -Encoding Unicode
[xml]$kitXml = Get-Content -LiteralPath $kitConfigPath -Raw

$expectedCommands = @($sourceXml.application.appCommand | ForEach-Object { "$($_.id):$($_.title)" })
$installedCommands = @($installedXml.application.appCommand | ForEach-Object { "$($_.id):$($_.title)" })
$commandDiff = Compare-Object $expectedCommands $installedCommands
if ($commandDiff) {
    Write-Host "Expected commands:"
    $expectedCommands | ForEach-Object { Write-Host "  $_" }
    Write-Host "Installed commands:"
    $installedCommands | ForEach-Object { Write-Host "  $_" }
    Fail "Installed KompasBambu.xml does not match rtw\KompasBambu.xml."
}

$expectedMenuItems = @($sourceXml.application.menu.appItem | ForEach-Object { "$($_.id)" })
$installedMenuItems = @($installedXml.application.menu.appItem | ForEach-Object { "$($_.id)" })
$menuDiff = Compare-Object $expectedMenuItems $installedMenuItems
if ($menuDiff) {
    Write-Host "Expected menu items: $($expectedMenuItems -join ', ')"
    Write-Host "Installed menu items: $($installedMenuItems -join ', ')"
    Fail "Installed menu items do not match rtw\KompasBambu.xml."
}

$registered = $kitXml.SelectSingleNode("//Application[@id='$appId']")
if ($null -eq $registered) {
    Fail "APP_KompasBambu is not registered in $kitConfigPath."
}
if ($registered.path -ne 'KompasBambu\KompasBambu.rtw') {
    Fail "APP_KompasBambu points to unexpected RTW path: $($registered.path)"
}

$sourceHash = (Get-FileHash -LiteralPath $sourceRtwPath -Algorithm SHA256).Hash
$installedHash = (Get-FileHash -LiteralPath $installedRtwPath -Algorithm SHA256).Hash
if ($sourceHash -ne $installedHash) {
    Fail "Installed RTW does not match built RTW. Re-run install-rtw-library.ps1."
}

Write-Host "KOMPAS RTW install is current."
Write-Host "Installed directory: $targetDir"
Write-Host "Registered path: $($registered.path)"
Write-Host "Commands:"
$installedXml.application.appCommand | ForEach-Object {
    Write-Host ("  {0}: {1}" -f $_.id, $_.title)
}
