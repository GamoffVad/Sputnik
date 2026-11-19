[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $OutputDir,
    [string] $NinjaPackage,
    [string] $ClothNinjaPackage,
    [string] $SandWatchmanPackage
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$out = (Resolve-Path -LiteralPath $OutputDir).Path
Add-Type -AssemblyName System.IO.Compression.FileSystem
$catalog = @(Get-Content -Raw -LiteralPath "$PSScriptRoot/pets/bundled-source.json" | ConvertFrom-Json)
$exported = @()
foreach ($pet in $catalog | Where-Object { $_.id -notin @('klyaksa', 'sand-watchman-clean') }) {
    $pet | Add-Member -NotePropertyName schemaVersion -NotePropertyValue 1
    $package = Join-Path $out ($pet.id + '.pet')
    if (Test-Path -LiteralPath $package) { throw "Output already exists: $package" }
    $zip = [IO.Compression.ZipFile]::Open($package, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $added = @{}
        foreach ($clip in $pet.animations) {
            $parts = if ($clip.sheets) { @($clip.sheets) } else { @($clip) }
            foreach ($part in $parts) {
                $file = [string]$part.file
                if ($file -notmatch '^assets/(?:[A-Za-z0-9_-]+/)*[A-Za-z0-9_-]+\.png$') { throw "Unexpected source path: $file" }
                $source = Join-Path $root "wwwroot/$file"
                if (!$clip.frameWidth) {
                    # PNG IHDR dimensions are big endian. No image decoding or resampling.
                    $bytes = [IO.File]::ReadAllBytes($source)
                    $width = $bytes[19] + 256 * ($bytes[18] + 256 * ($bytes[17] + 256 * $bytes[16]))
                    $height = $bytes[23] + 256 * ($bytes[22] + 256 * ($bytes[21] + 256 * $bytes[20]))
                    $clip | Add-Member -NotePropertyName frameWidth -NotePropertyValue $width
                    $clip | Add-Member -NotePropertyName frameHeight -NotePropertyValue $height
                    $clip | Add-Member -NotePropertyName columns -NotePropertyValue 1
                }
                if (!$added.ContainsKey($file)) {
                    $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $source, $file, [IO.Compression.CompressionLevel]::NoCompression)
                    $added[$file] = $true
                }
            }
        }
        $writer = [IO.StreamWriter]::new($zip.CreateEntry('manifest.json').Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write(($pet | ConvertTo-Json -Depth 16)) } finally { $writer.Dispose() }
    } finally { $zip.Dispose() }
    $exported += [pscustomobject]@{ id=$pet.id; name=$pet.name; file=($pet.id + '.pet'); theme=$pet.theme }
}
$sandPin = Get-Content -Raw -LiteralPath "$PSScriptRoot/pets/sand-watchman-clean-source.json" | ConvertFrom-Json
$sandDownloaded = !$SandWatchmanPackage
if (!$SandWatchmanPackage) {
    $SandWatchmanPackage = Join-Path $out 'sand-watchman-clean.download'
    Invoke-WebRequest -Uri $sandPin.url -OutFile $SandWatchmanPackage
}
try {
    if ((Get-FileHash -LiteralPath $SandWatchmanPackage -Algorithm SHA256).Hash -ne $sandPin.sha256) { throw 'Sand Watchman checksum mismatch.' }
    Copy-Item -LiteralPath $SandWatchmanPackage -Destination (Join-Path $out 'sand-watchman-clean.pet')
} finally {
    if ($sandDownloaded -and (Test-Path -LiteralPath $SandWatchmanPackage)) { Remove-Item -LiteralPath $SandWatchmanPackage }
}
$exported += [pscustomobject]@{ id='sand-watchman-clean'; name='Песчаный Дозорный'; file='sand-watchman-clean.pet'; theme=$sandPin.theme }
Copy-Item -LiteralPath "$root/pets/knight-watchman.pet" -Destination (Join-Path $out 'knight-watchman.pet')
$exported += [pscustomobject]@{ id='knight-watchman'; name='Рыцарь-дозорный'; file='knight-watchman.pet' }
$pin = Get-Content -Raw -LiteralPath "$PSScriptRoot/pets/quiet-ninja-source.json" | ConvertFrom-Json
$downloaded = !$NinjaPackage
if (!$NinjaPackage) {
    $NinjaPackage = Join-Path $out 'quiet-ninja.download'
    Invoke-WebRequest -Uri $pin.url -OutFile $NinjaPackage
}
try {
    if ((Get-FileHash -LiteralPath $NinjaPackage -Algorithm SHA256).Hash -ne $pin.sha256) { throw 'Quiet Ninja checksum mismatch.' }
    Copy-Item -LiteralPath $NinjaPackage -Destination (Join-Path $out 'quiet-ninja.pet')
} finally {
    if ($downloaded -and (Test-Path -LiteralPath $NinjaPackage)) { Remove-Item -LiteralPath $NinjaPackage }
}
$exported += [pscustomobject]@{ id='quiet-ninja'; name='Тихий ниндзя'; file='quiet-ninja.pet' }
$clothPin = Get-Content -Raw -LiteralPath "$PSScriptRoot/pets/cloth-ninja-source.json" | ConvertFrom-Json
$clothDownloaded = !$ClothNinjaPackage
if (!$ClothNinjaPackage) {
    $ClothNinjaPackage = Join-Path $out 'cloth-ninja.download'
    Invoke-WebRequest -Uri $clothPin.url -OutFile $ClothNinjaPackage
}
try {
    if ((Get-FileHash -LiteralPath $ClothNinjaPackage -Algorithm SHA256).Hash -ne $clothPin.sha256) { throw 'Cloth Ninja checksum mismatch.' }
    Copy-Item -LiteralPath $ClothNinjaPackage -Destination (Join-Path $out 'cloth-ninja.pet')
} finally {
    if ($clothDownloaded -and (Test-Path -LiteralPath $ClothNinjaPackage)) { Remove-Item -LiteralPath $ClothNinjaPackage }
}
$exported += [pscustomobject]@{ id='cloth-ninja'; name='Тканевый ниндзя'; file='cloth-ninja.pet' }
foreach ($item in $exported) {
    $file = Join-Path $out $item.file
    $item | Add-Member -NotePropertyName sha256 -NotePropertyValue ((Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant())
    $item | Add-Member -NotePropertyName bytes -NotePropertyValue ((Get-Item -LiteralPath $file).Length)
}
ConvertTo-Json -InputObject $exported -Depth 8 | Set-Content -LiteralPath (Join-Path $out 'pets.json') -Encoding utf8
$exported | Select-Object id,file,bytes,sha256
