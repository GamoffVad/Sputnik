param(
    [Parameter(Mandatory)]
    [string] $PublishDir
)

$ErrorActionPreference = 'Stop'
$assets = Join-Path (Resolve-Path -LiteralPath $PublishDir).Path 'wwwroot/assets'
$catalog = Join-Path $assets 'pets.json'
$pets = @(Get-Content -Raw -LiteralPath $catalog | ConvertFrom-Json)
$included = @($pets | Where-Object id -EQ 'klyaksa') +
    @($pets | Where-Object { $_.id -notin 'hudi', 'klyaksa' })
if (!$included.Count -or $included[0].id -ne 'klyaksa') {
    throw 'The installer requires the Klyaksa pet.'
}
ConvertTo-Json -InputObject $included -Depth 12 | Set-Content -LiteralPath $catalog -Encoding utf8
$hudiImage = Join-Path $assets 'hudi.png'
if (Test-Path -LiteralPath $hudiImage -PathType Leaf) {
    Remove-Item -LiteralPath $hudiImage
}
