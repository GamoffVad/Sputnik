[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,
    [Parameter(Mandatory)]
    [string] $Compiler,
    [ValidateSet('x64', 'x86')]
    [string[]] $Architecture = @('x64', 'x86')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $root 'artifacts/win7'
$dependencies = Join-Path $work 'dependencies'
$out = Join-Path $work "v$Version"
$Compiler = (Resolve-Path -LiteralPath $Compiler).Path
New-Item -ItemType Directory -Force $dependencies, $out | Out-Null

function Assert-MicrosoftPackage([string] $Path, [string] $ExpectedHash) {
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $ExpectedHash) {
        throw "Unexpected SHA-256 for $Path"
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '^CN=Microsoft Corporation,') {
        throw "Invalid Microsoft signature for $Path"
    }
}

function Get-MicrosoftPackage([string] $Path, [string] $Url, [string] $ExpectedHash) {
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        Assert-MicrosoftPackage $Path $ExpectedHash
        return
    }
    $download = "$Path.download.exe"
    Invoke-WebRequest -Uri $Url -OutFile $download
    Assert-MicrosoftPackage $download $ExpectedHash
    Move-Item -LiteralPath $download -Destination $Path
}

$framework = Join-Path $dependencies 'NDP48-x86-x64-AllOS-ENU.exe'
Get-MicrosoftPackage $framework 'https://go.microsoft.com/fwlink/?linkid=2088631' '0A3A390C47E639D0F7FC65B21195FEE6B7F65B066F80F70C60FAB191D14B7E40'

foreach ($arch in $Architecture) {
    $payload = Join-Path $work "payload-$Version-$arch"
    $intermediate = Join-Path $work "obj-$arch/"
    dotnet publish (Join-Path $root 'Sputnik.csproj') -c Release -p:Win7=true "-p:PlatformTarget=$arch" "-p:BaseIntermediateOutputPath=$intermediate" "-p:Version=$Version" -o $payload
    if ($LASTEXITCODE) { throw "Sputnik build failed for $arch" }
    & (Join-Path $PSScriptRoot 'Prepare-Payload.ps1') -PublishDir $payload
    & (Join-Path $PSScriptRoot 'Test-Payload.ps1') -PublishDir $payload -Version $Version -Target "legacy-$arch"
    & $Compiler "/DAppVersion=$Version" "/DPublishDir=$payload" "/DInstallerOutput=$out" "/DLegacyArchitecture=$arch" "/DNetFrameworkInstaller=$framework" (Join-Path $PSScriptRoot 'Sputnik.iss')
    if ($LASTEXITCODE) { throw "Sputnik installer build failed for $arch" }
    Get-FileHash -LiteralPath (Join-Path $out "SputnikSetup-$Version-win7-$arch.exe") -Algorithm SHA256
}
