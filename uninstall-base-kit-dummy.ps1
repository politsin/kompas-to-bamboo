param(
    [string]$KompasVersion = '24',
    [string]$KompasInstallPath = 'C:\Program Files\ASCON\KOMPAS-3D v24 Home'
)

$programData = [Environment]::GetFolderPath('CommonApplicationData')
$kitPath = Join-Path $programData "ASCON\KOMPAS-3D\$KompasVersion\Base.kit.config"
$targetKle = Join-Path $KompasInstallPath 'Libs\KompasBambuDummy.kle'
$libraryId = 'B7FDEF9F-AA0B-4BA2-A915-6126A7235AB3'

if (-not (Test-Path -LiteralPath $kitPath)) {
    throw "Base kit config was not found: $kitPath"
}

$backup = "$kitPath.bak-$(Get-Date -Format yyyyMMdd-HHmmss)"
Copy-Item -LiteralPath $kitPath -Destination $backup

[xml]$xml = Get-Content -LiteralPath $kitPath -Raw
$nodes = @($xml.Kit_Config.Groups.Group.Library | Where-Object { $_.id -eq $libraryId })

foreach ($node in $nodes) {
    [void]$node.ParentNode.RemoveChild($node)
}

$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Encoding = [System.Text.Encoding]::Unicode
$settings.Indent = $true
$writer = [System.Xml.XmlWriter]::Create($kitPath, $settings)
$xml.Save($writer)
$writer.Close()

Remove-Item -LiteralPath $targetKle -Force -ErrorAction SilentlyContinue

Write-Host "Removed Bambu Dummy Library from $kitPath"
Write-Host "Backup: $backup"
