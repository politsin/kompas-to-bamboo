param(
    [string]$KompasRoot = 'C:\Program Files\ASCON\KOMPAS-3D v24 Home'
)

$ErrorActionPreference = 'Stop'

$targetDir = Join-Path $KompasRoot 'Libs\KompasBambu'
$kitConfigPath = 'C:\ProgramData\ASCON\KOMPAS-3D\24\Base.kit.config'
$appId = 'APP_KompasBambu'

if (Test-Path $kitConfigPath) {
    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $true
    $xml.Load($kitConfigPath)
    $node = $xml.SelectSingleNode("//Application[@id='$appId']")
    if ($null -ne $node -and $null -ne $node.ParentNode) {
        [void]$node.ParentNode.RemoveChild($node)
        $xml.Save($kitConfigPath)
    }
}

if (Test-Path $targetDir) {
    Remove-Item -LiteralPath $targetDir -Recurse -Force
}

Write-Host "Removed RTW library from $targetDir"
