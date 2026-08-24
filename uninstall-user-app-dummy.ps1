param(
    [string]$KompasVersion = '24'
)

$roaming = [Environment]::GetFolderPath('ApplicationData')
$profileDir = Join-Path $roaming "ASCON\KOMPAS-3D\$KompasVersion"
$kitPath = Join-Path $profileDir 'kHome.kit.config'
$appPathsPath = Join-Path $profileDir 'UI_AppPaths.config'
$appId = 'DE7E8C03-25F4-497E-92EE-1C52B93A06E4'

foreach ($path in @($kitPath, $appPathsPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "KOMPAS config was not found: $path"
    }

    Copy-Item -LiteralPath $path -Destination "$path.bak-$(Get-Date -Format yyyyMMdd-HHmmss)"
}

[xml]$kitXml = Get-Content -LiteralPath $kitPath -Raw
$kitNodes = @($kitXml.Kit_Config.Groups.Group.Application | Where-Object { $_.id -eq $appId })
foreach ($node in $kitNodes) {
    [void]$node.ParentNode.RemoveChild($node)
}

$kitSettings = [System.Xml.XmlWriterSettings]::new()
$kitSettings.Encoding = [System.Text.Encoding]::Unicode
$kitSettings.Indent = $true
$kitWriter = [System.Xml.XmlWriter]::Create($kitPath, $kitSettings)
$kitXml.Save($kitWriter)
$kitWriter.Close()

[xml]$pathsXml = Get-Content -LiteralPath $appPathsPath -Raw
$pathNodes = @($pathsXml.XmlRoot.Product.App | Where-Object { $_.id -eq $appId })
foreach ($node in $pathNodes) {
    [void]$node.ParentNode.RemoveChild($node)
}

$pathsSettings = [System.Xml.XmlWriterSettings]::new()
$pathsSettings.Encoding = [System.Text.UTF8Encoding]::new($false)
$pathsSettings.Indent = $true
$pathsWriter = [System.Xml.XmlWriter]::Create($appPathsPath, $pathsSettings)
$pathsXml.Save($pathsWriter)
$pathsWriter.Close()

Write-Host "Removed Bambu Studio from user KOMPAS app config."
