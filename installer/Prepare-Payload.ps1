param([Parameter(Mandatory)][string] $PublishDir)
$ErrorActionPreference = 'Stop'
$payload = (Resolve-Path -LiteralPath $PublishDir).Path
$assets = Join-Path $payload 'wwwroot/assets'
$catalog = Join-Path $assets 'pets.json'
$pets = @(Get-Content -Raw -LiteralPath $catalog | ConvertFrom-Json)
$included = @($pets | Where-Object id -EQ 'klyaksa')
if ($included.Count -ne 1) { throw 'The installer requires exactly one Klyaksa pet.' }
$allowed = @('pets.json', 'icon-catalog.json', 'LUCIDE-LICENSE.txt')
foreach ($clip in $included[0].animations) {
    if ($clip.file -notmatch '^assets/klyaksa/[a-z-]+\.png$') { throw 'Unexpected bundled asset path' }
    $allowed += $clip.file.Substring(7)
}
# Clean stale distribution output only.
foreach ($file in Get-ChildItem -LiteralPath $assets -File -Recurse) {
    $relative = $file.FullName.Substring($assets.Length + 1).Replace('\','/')
    if ($relative -notin $allowed) { Remove-Item -LiteralPath $file.FullName }
}
ConvertTo-Json -InputObject $included -Depth 12 | Set-Content -LiteralPath $catalog -Encoding utf8
