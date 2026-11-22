[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $OutputDir,
    [hashtable] $LocalPackages = @{},
    [string] $NinjaPackage,
    [string] $ClothNinjaPackage,
    [string] $SandWatchmanPackage
)
$ErrorActionPreference = 'Stop'
$catalog = @(Get-Content -LiteralPath "$PSScriptRoot/pets/catalog.json" -Raw | ConvertFrom-Json)
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDir).Path
if ($NinjaPackage) { $LocalPackages['quiet-ninja']=$NinjaPackage }
if ($ClothNinjaPackage) { $LocalPackages['cloth-ninja']=$ClothNinjaPackage }
if ($SandWatchmanPackage) { $LocalPackages['sand-watchman-clean']=$SandWatchmanPackage }
foreach ($pet in $catalog) {
    if ($pet.id -notmatch '^[a-z0-9][a-z0-9-]{0,48}$' -or $pet.file -ne ($pet.id + '.pet')) { throw 'Invalid catalogue identity' }
    $destination = Join-Path $output $pet.file
    if ($LocalPackages.ContainsKey($pet.id)) { Copy-Item -LiteralPath $LocalPackages[$pet.id] -Destination $destination }
    elseif (Test-Path -LiteralPath $destination) {
        if ((Get-Item -LiteralPath $destination).Length -ne $pet.bytes -or (Get-FileHash -LiteralPath $destination).Hash -ne $pet.sha256) { throw "Existing output differs: $($pet.id)" }
    }
    else { Invoke-WebRequest -Uri $pet.url -OutFile $destination }
    if ((Get-Item -LiteralPath $destination).Length -ne $pet.bytes -or (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $pet.sha256) { throw "Package checksum mismatch: $($pet.id)" }
}
ConvertTo-Json -InputObject $catalog -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'pets.json') -Encoding utf8
$catalog | Select-Object id,name,animations,bytes,sha256
