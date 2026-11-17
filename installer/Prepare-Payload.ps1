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
# The repository's additional pet must also be available after a clean install.
# Rewrite only the distribution copy; retain the standalone .pet for import.
$package = Join-Path (Split-Path -Parent $PSScriptRoot) 'pets/knight-watchman.pet'
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
    $reader = [IO.StreamReader]::new($archive.GetEntry('manifest.json').Open())
    try { $knight = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if ($knight.id -ne 'knight-watchman' -or $knight.schemaVersion -ne 1) { throw 'Unexpected bundled Knight manifest' }
    $knightDir = Join-Path $assets 'knight-watchman'
    New-Item -ItemType Directory -Force -Path $knightDir | Out-Null
    foreach ($clip in $knight.animations) {
        if ($clip.file -notmatch '^[A-Za-z0-9_-]+\.png$') { throw 'Unexpected Knight asset path' }
        $entry = $archive.GetEntry($clip.file)
        if (!$entry -or $entry.Length -gt 32MB) { throw 'Missing or oversized Knight animation' }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $knightDir $clip.file), $true)
        $clip.file = 'assets/knight-watchman/' + $clip.file
    }
    $knight.PSObject.Properties.Remove('schemaVersion')
    $included = @($included | Where-Object id -NE 'knight-watchman') + @($knight)
} finally { $archive.Dispose() }
ConvertTo-Json -InputObject $included -Depth 12 | Set-Content -LiteralPath $catalog -Encoding utf8
$hudiImage = Join-Path $assets 'hudi.png'
if (Test-Path -LiteralPath $hudiImage -PathType Leaf) {
    Remove-Item -LiteralPath $hudiImage
}
