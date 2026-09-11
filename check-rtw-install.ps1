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
$userKitConfigPath = Join-Path $env:APPDATA 'ASCON\KOMPAS-3D\24\kHome.kit.config'
$userAppPathsConfigPath = Join-Path $env:APPDATA 'ASCON\KOMPAS-3D\24\UI_AppPaths.config'
$userResourcesCachePath = Join-Path $env:APPDATA 'ASCON\KOMPAS-3D\24\resources.bin'
$expectedAbsoluteRtwPath = Join-Path $targetDir 'KompasBambu.rtw'
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

function Get-CommandSpecs([xml]$Xml) {
    @(
        $Xml.SelectNodes('//appCommand[@title]') |
            ForEach-Object { "$($_.id):$($_.title)" } |
            Sort-Object -Unique
    )
}

$expectedCommands = Get-CommandSpecs $sourceXml
$installedCommands = Get-CommandSpecs $installedXml
$commandDiff = Compare-Object $expectedCommands $installedCommands
if ($commandDiff) {
    Write-Host "Expected commands:"
    $expectedCommands | ForEach-Object { Write-Host "  $_" }
    Write-Host "Installed commands:"
    $installedCommands | ForEach-Object { Write-Host "  $_" }
    Fail "Installed KompasBambu.xml does not match rtw\KompasBambu.xml."
}

$expectedMenuItems = @($sourceXml.application.menu.appItem | ForEach-Object { "$($_.id)" })
foreach ($menuId in @($appId, "$appId|Kompas.m3d", "$appId|Kompas.a3d")) {
    $sourceMenu = $sourceXml.SelectSingleNode("//menu[@id='$menuId']")
    $installedMenu = $installedXml.SelectSingleNode("//menu[@id='$menuId']")
    if ($null -eq $sourceMenu -or $null -eq $installedMenu) {
        Fail "Missing required menu '$menuId' in source or installed KompasBambu.xml."
    }

    $expectedMenuItems = @($sourceMenu.appItem | ForEach-Object { "$($_.id)" })
    $installedMenuItems = @($installedMenu.appItem | ForEach-Object { "$($_.id)" })
    $menuDiff = Compare-Object $expectedMenuItems $installedMenuItems
    if ($menuDiff) {
        Write-Host "Expected menu items for ${menuId}: $($expectedMenuItems -join ', ')"
        Write-Host "Installed menu items for ${menuId}: $($installedMenuItems -join ', ')"
        Fail "Installed menu items do not match rtw\KompasBambu.xml."
    }
}

$registered = $kitXml.SelectSingleNode("//Application[@id='$appId']")
if ($null -eq $registered) {
    Fail "APP_KompasBambu is not registered in $kitConfigPath."
}
if ($registered.path -ne 'KompasBambu\KompasBambu.rtw') {
    Fail "APP_KompasBambu points to unexpected RTW path: $($registered.path)"
}

if (Test-Path -LiteralPath $userKitConfigPath) {
    [xml]$userKitXml = Get-Content -LiteralPath $userKitConfigPath -Raw
    $legacyUserOverrides = @($userKitXml.SelectNodes("//Application[@id='KompasBambu.rtw']"))
    if ($legacyUserOverrides.Count -gt 0) {
        Fail "User KOMPAS profile still has legacy KompasBambu.rtw application overrides. Re-run install-rtw-library.ps1 while KOMPAS is closed."
    }

    $userApp = $userKitXml.SelectSingleNode("//Application[@id='$appId']")
    if ($null -eq $userApp) {
        Fail "User KOMPAS profile does not register APP_KompasBambu. Re-run install-rtw-library.ps1 while KOMPAS is closed."
    }
    if ($userApp.path -ne $expectedAbsoluteRtwPath) {
        Fail "User KOMPAS profile points APP_KompasBambu to unexpected path: $($userApp.path)"
    }
}

if (Test-Path -LiteralPath $userAppPathsConfigPath) {
    [xml]$userAppPathsXml = Get-Content -LiteralPath $userAppPathsConfigPath -Raw
    $staleAppPaths = @($userAppPathsXml.SelectNodes("//App[contains(@path, 'KompasBambuPlugin.dll')]"))
    if ($staleAppPaths.Count -gt 0) {
        Fail "User KOMPAS profile still has stale KompasBambu app paths. Re-run install-rtw-library.ps1 while KOMPAS is closed."
    }

    $userAppPath = $userAppPathsXml.SelectSingleNode("//App[@id='$appId']")
    if ($null -eq $userAppPath) {
        Fail "User KOMPAS app paths do not register APP_KompasBambu. Re-run install-rtw-library.ps1 while KOMPAS is closed."
    }
    if ($userAppPath.path -ne $expectedAbsoluteRtwPath) {
        Fail "User KOMPAS app paths point APP_KompasBambu to unexpected path: $($userAppPath.path)"
    }
}

$sourceHash = (Get-FileHash -LiteralPath $sourceRtwPath -Algorithm SHA256).Hash
$installedHash = (Get-FileHash -LiteralPath $installedRtwPath -Algorithm SHA256).Hash
if ($sourceHash -ne $installedHash) {
    Fail "Installed RTW does not match built RTW. Re-run install-rtw-library.ps1."
}

$requiredExports = @(
    'LIBRARYENTRY',
    'LIBRARYID',
    'LIBRARYNAME',
    'LIBRARYNAMEW',
    'DisplayLibraryNameW',
    'LIBRARYPROTECTNUMBER',
    'LibToolBarId',
    'LibraryBmpBeginID'
)
$objdump = Get-Command objdump.exe -ErrorAction SilentlyContinue
if ($null -eq $objdump) {
    $scoopObjdump = Join-Path $env:USERPROFILE 'scoop\apps\mingw\current\bin\objdump.exe'
    if (Test-Path -LiteralPath $scoopObjdump) {
        $objdump = [pscustomobject]@{ Source = $scoopObjdump }
    }
}
if ($null -ne $objdump) {
    $exports = & $objdump.Source -p $installedRtwPath
    foreach ($requiredExport in $requiredExports) {
        if (-not ($exports | Select-String -SimpleMatch $requiredExport -Quiet)) {
            Fail "Installed RTW does not export $requiredExport. Rebuild and reinstall RTW."
        }
    }
}

if (Test-Path -LiteralPath $userResourcesCachePath) {
    Fail "KOMPAS UI resource cache still exists: $userResourcesCachePath. Close KOMPAS and rerun install-rtw-library.ps1 so menu changes are rebuilt."
}

Write-Host "KOMPAS RTW install is current."
Write-Host "Installed directory: $targetDir"
Write-Host "Registered path: $($registered.path)"
Write-Host "Commands:"
$installedXml.SelectNodes('//appCommand[@title]') | ForEach-Object {
    "$($_.id):$($_.title)"
} | Sort-Object -Unique | ForEach-Object {
    $id, $title = $_ -split ':', 2
    Write-Host ("  {0}: {1}" -f $id, $title)
}
