[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDir,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidateSet('modern-x64', 'legacy-x64', 'legacy-x86')][string]$Target
)
$ErrorActionPreference = 'Stop'
$payload = (Resolve-Path -LiteralPath $PublishDir).Path
function Require-File([string]$Name) {
    $path = Join-Path $payload $Name
    if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw "Missing or empty payload file: $Name" }
    return $path
}
$exe = Require-File 'Sputnik.exe'
$binary = [IO.File]::ReadAllBytes($exe)
$pe = [BitConverter]::ToInt32($binary, 0x3c)
if ([BitConverter]::ToUInt32($binary, $pe) -ne 0x4550) { throw 'Invalid PE executable' }
$machine = [BitConverter]::ToUInt16($binary, $pe + 4)
$expected = if ($Target -eq 'legacy-x86') { 0x14c } else { 0x8664 }
if ($machine -ne $expected) { throw "Wrong executable architecture: $machine, expected $expected" }
$assembly = if ($Target -eq 'modern-x64') { Require-File 'Sputnik.dll' } else { $exe }
$info = [Diagnostics.FileVersionInfo]::GetVersionInfo($assembly)
if ($info.FileVersion -ne "$Version.0" -or $info.ProductVersion -notmatch ('^' + [regex]::Escape($Version) + '(\+|$)')) {
    throw "Wrong application version: $($info.FileVersion) / $($info.ProductVersion)"
}
if ($Target -eq 'modern-x64') {
    foreach ($name in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'PresentationFramework.dll', 'Sputnik.deps.json')) { $null = Require-File $name }
    $config = Get-Content -Raw -LiteralPath (Require-File 'Sputnik.runtimeconfig.json') | ConvertFrom-Json
    if (@($config.runtimeOptions.includedFrameworks).Count -ne 2 -or $config.runtimeOptions.framework -or $config.runtimeOptions.frameworks) {
        throw 'Modern payload is not self-contained'
    }
} else {
    $config = [xml](Get-Content -Raw -LiteralPath (Require-File 'Sputnik.exe.config'))
    if ($config.configuration.startup.supportedRuntime.sku -ne '.NETFramework,Version=v4.8') { throw 'Wrong legacy framework target' }
    foreach ($name in @('System.Text.Json.dll', 'System.Net.Http.Json.dll', 'System.Memory.dll', 'Microsoft.Bcl.AsyncInterfaces.dll')) { $null = Require-File $name }
    if (Test-Path -LiteralPath (Join-Path $payload 'coreclr.dll')) { throw 'Legacy payload contains modern runtime files' }
}
if (Get-ChildItem -LiteralPath $payload -File -Filter '*WebView2*') { throw 'Obsolete WebView2 dependency in payload' }
$pets = @(Get-Content -Raw -LiteralPath (Require-File 'wwwroot/assets/pets.json') | ConvertFrom-Json)
if ($pets[0].id -ne 'klyaksa' -or 'knight-watchman' -notin $pets.id) { throw 'Required bundled pets are missing' }
$assetCount = 0
foreach ($pet in $pets) {
    foreach ($clip in $pet.animations) {
        $files = if ($clip.sheets) { @($clip.sheets.file) } else { @($clip.file) }
        foreach ($file in $files) {
            $asset = [IO.Path]::GetFullPath((Join-Path $payload "wwwroot/$file"))
            if (!$asset.StartsWith($payload + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Asset escapes payload: $file" }
            $null = Require-File "wwwroot/$file"
            $assetCount++
        }
    }
}
[pscustomobject]@{ Target=$Target; Version=$info.FileVersion; Pets=$pets.Count; AnimationAssets=$assetCount; ApplicationSHA256=(Get-FileHash -LiteralPath $assembly).Hash }
