param(
    [string]$KompasVersion = '24'
)

$roaming = [Environment]::GetFolderPath('ApplicationData')
$kitPath = Join-Path $roaming "ASCON\KOMPAS-3D\$KompasVersion\kHome.kit.config"
$libraryPath = 'C:\Program Files\ASCON\KOMPAS-3D v24 Home\Libs\KompasBambuDummy.kle'
$libraryId = 'B7FDEF9F-AA0B-4BA2-A915-6126A7235AB3'

if (-not (Test-Path -LiteralPath $kitPath)) {
    throw "KOMPAS user kit config was not found: $kitPath"
}

if (-not (Test-Path -LiteralPath $libraryPath)) {
    throw "Dummy KLE library was not found: $libraryPath"
}

$backup = "$kitPath.bak-$(Get-Date -Format yyyyMMdd-HHmmss)"
Copy-Item -LiteralPath $kitPath -Destination $backup

[xml]$xml = Get-Content -LiteralPath $kitPath -Raw
$existing = $xml.Kit_Config.Groups.Group.Library | Where-Object { $_.id -eq $libraryId }

if (-not $existing) {
    $group = $xml.Kit_Config.Groups.Group | Where-Object { -not $_.groupName } | Select-Object -First 1
    if (-not $group) {
        throw "Default group was not found in $kitPath"
    }

    $node = $xml.CreateElement('Library')
    $attributes = [ordered]@{
        libType = 'lbt_kle'
        path = $libraryPath
        id = $libraryId
        libName = 'Bambu Dummy Library'
        displayName = 'Bambu Dummy Library'
    }

    foreach ($entry in $attributes.GetEnumerator()) {
        $attribute = $xml.CreateAttribute($entry.Key)
        $attribute.Value = $entry.Value
        [void]$node.Attributes.Append($attribute)
    }

    [void]$group.AppendChild($node)
}

$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Encoding = [System.Text.Encoding]::Unicode
$settings.Indent = $true
$writer = [System.Xml.XmlWriter]::Create($kitPath, $settings)
$xml.Save($writer)
$writer.Close()

Write-Host "Updated $kitPath"
Write-Host "Backup: $backup"
