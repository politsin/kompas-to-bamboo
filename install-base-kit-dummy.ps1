param(
    [string]$KompasVersion = '24',
    [string]$KompasInstallPath = 'C:\Program Files\ASCON\KOMPAS-3D v24 Home'
)

$programData = [Environment]::GetFolderPath('CommonApplicationData')
$kitPath = Join-Path $programData "ASCON\KOMPAS-3D\$KompasVersion\Base.kit.config"
$sourceKle = Join-Path $KompasInstallPath 'Libs\GRAPHIC.KLE'
$targetKle = Join-Path $KompasInstallPath 'Libs\KompasBambuDummy.kle'
$libraryId = 'B7FDEF9F-AA0B-4BA2-A915-6126A7235AB3'

if (-not (Test-Path -LiteralPath $kitPath)) {
    throw "Base kit config was not found: $kitPath"
}

if (-not (Test-Path -LiteralPath $sourceKle)) {
    throw "Source KLE was not found: $sourceKle"
}

if (-not (Test-Path -LiteralPath $targetKle)) {
    Copy-Item -LiteralPath $sourceKle -Destination $targetKle
}

$backup = "$kitPath.bak-$(Get-Date -Format yyyyMMdd-HHmmss)"
Copy-Item -LiteralPath $kitPath -Destination $backup

[xml]$xml = Get-Content -LiteralPath $kitPath -Raw
$existing = $xml.Kit_Config.Groups.Group.Library | Where-Object { $_.id -eq $libraryId }

if (-not $existing) {
    $group = $xml.Kit_Config.Groups.Group | Where-Object {
        $_.Library | Where-Object { $_.libType -eq 'lbt_kle' }
    } | Select-Object -First 1

    if (-not $group) {
        throw "KLE library group was not found in $kitPath"
    }

    $node = $xml.CreateElement('Library')
    $attributes = [ordered]@{
        libType = 'lbt_kle'
        path = 'KompasBambuDummy.kle'
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

Write-Host "Installed Bambu Dummy Library into $kitPath"
Write-Host "Backup: $backup"
