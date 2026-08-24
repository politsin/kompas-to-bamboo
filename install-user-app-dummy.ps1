param(
    [string]$KompasVersion = '24'
)

$roaming = [Environment]::GetFolderPath('ApplicationData')
$profileDir = Join-Path $roaming "ASCON\KOMPAS-3D\$KompasVersion"
$kitPath = Join-Path $profileDir 'kHome.kit.config'
$appPathsPath = Join-Path $profileDir 'UI_AppPaths.config'
$appId = 'DE7E8C03-25F4-497E-92EE-1C52B93A06E4'
$pluginPath = 'W:\proj\win64\kompas-bambu\dist\plugin\KompasBambuPlugin.dll'

if (-not (Test-Path -LiteralPath $kitPath)) {
    throw "KOMPAS user kit config was not found: $kitPath"
}

if (-not (Test-Path -LiteralPath $appPathsPath)) {
    throw "KOMPAS UI app paths config was not found: $appPathsPath"
}

if (-not (Test-Path -LiteralPath $pluginPath)) {
    throw "Plugin DLL was not found: $pluginPath"
}

foreach ($path in @($kitPath, $appPathsPath)) {
    Copy-Item -LiteralPath $path -Destination "$path.bak-$(Get-Date -Format yyyyMMdd-HHmmss)"
}

[xml]$kitXml = Get-Content -LiteralPath $kitPath -Raw
$existingApp = $kitXml.Kit_Config.Groups.Group.Application | Where-Object { $_.id -eq $appId }

if (-not $existingApp) {
    $group = $kitXml.Kit_Config.Groups.Group | Where-Object { -not $_.groupName } | Select-Object -First 1
    if (-not $group) {
        throw "Default group was not found in $kitPath"
    }

    $node = $kitXml.CreateElement('Application')
    $attributes = [ordered]@{
        libType = 'lbt_ocx'
        ocxClassId = 'KompasBambu.Plugin'
        id = $appId
        libName = 'Bambu Studio'
        displayName = 'Bambu Studio'
        autostart = 'true'
    }

    foreach ($entry in $attributes.GetEnumerator()) {
        $attribute = $kitXml.CreateAttribute($entry.Key)
        $attribute.Value = $entry.Value
        [void]$node.Attributes.Append($attribute)
    }

    [void]$group.AppendChild($node)
}

$kitSettings = [System.Xml.XmlWriterSettings]::new()
$kitSettings.Encoding = [System.Text.Encoding]::Unicode
$kitSettings.Indent = $true
$kitWriter = [System.Xml.XmlWriter]::Create($kitPath, $kitSettings)
$kitXml.Save($kitWriter)
$kitWriter.Close()

[xml]$pathsXml = Get-Content -LiteralPath $appPathsPath -Raw
$product = $pathsXml.XmlRoot.Product | Where-Object { $_.id -eq 'kHome' } | Select-Object -First 1
if (-not $product) {
    throw "kHome product node was not found in $appPathsPath"
}

$existingPath = $product.App | Where-Object { $_.id -eq $appId }
if (-not $existingPath) {
    $node = $pathsXml.CreateElement('App')
    $idAttr = $pathsXml.CreateAttribute('id')
    $idAttr.Value = $appId
    [void]$node.Attributes.Append($idAttr)

    $pathAttr = $pathsXml.CreateAttribute('path')
    $pathAttr.Value = $pluginPath
    [void]$node.Attributes.Append($pathAttr)

    [void]$product.AppendChild($node)
} else {
    $existingPath.path = $pluginPath
}

$pathsSettings = [System.Xml.XmlWriterSettings]::new()
$pathsSettings.Encoding = [System.Text.UTF8Encoding]::new($false)
$pathsSettings.Indent = $true
$pathsWriter = [System.Xml.XmlWriter]::Create($appPathsPath, $pathsSettings)
$pathsXml.Save($pathsWriter)
$pathsWriter.Close()

Write-Host "Registered Bambu Studio in user KOMPAS app config."
