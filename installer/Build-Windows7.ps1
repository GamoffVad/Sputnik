[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,
    [Parameter(Mandatory)]
    [string] $Compiler,
    [Parameter(Mandatory)]
    [string] $SevenZip,
    [ValidateSet('x64', 'x86')]
    [string[]] $Architecture = @('x64', 'x86')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $root 'artifacts/win7'
$dependencies = Join-Path $work 'dependencies'
$out = Join-Path $work "v$Version"
$Compiler = (Resolve-Path -LiteralPath $Compiler).Path
$SevenZip = (Resolve-Path -LiteralPath $SevenZip).Path
New-Item -ItemType Directory -Force $dependencies, $out | Out-Null

# These are the two Windows 7 WebView2 109.0.1518.140 entries in Microsoft Update Catalog.
$webview = @{
    x64 = @{
        Url = 'https://catalog.s.download.windowsupdate.com/c/msdownload/update/software/updt/2023/09/microsoftedgestandaloneinstallerx64_1c890b4b8dd6b7c93da98ebdc08ecdc5e30e50cb.exe'
        UpdateHash = 'EAC95C8095EC5F9971EADE9827D8FB67FD251F5C16E702B5312D31067E39119B'
        OfflineHash = '70D496873A0A1CA14AE0A038D25856B2121B1B4B7BAD9801CE639B144BAC41F8'
    }
    x86 = @{
        Url = 'https://catalog.s.download.windowsupdate.com/c/msdownload/update/software/updt/2023/09/microsoftedgestandaloneinstallerx86_179f59bc54d73843d9288a9fd5609de0e507b911.exe'
        UpdateHash = '60B22C71128F10ABC5E92DD8FED90FF6CA55B8382BA01E48D87FAF1664746A31'
        OfflineHash = '2B48CE3B304B7B9A8F9377A42D8661ECDC53C5F2C6EE708DA9D62127C7802C18'
    }
}

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
    $package = $webview[$arch]
    $offline = Join-Path $dependencies "WebView2Runtime109Setup-$arch.exe"
    if (!(Test-Path -LiteralPath $offline -PathType Leaf)) {
        $update = Join-Path $dependencies "MicrosoftEdgeWebView2Runtime109-$arch.exe"
        Get-MicrosoftPackage $update $package.Url $package.UpdateHash
        # Extract the signed offline application installer from the Windows Update wrapper.
        $entry = "MicrosoftEdge_$($arch.ToUpperInvariant())_109.0.1518.140.exe.{0D50BFEC-CD6A-4F9A-964C-C7416E3ACB10}"
        & $SevenZip e -y "-o$dependencies" $update $entry
        if ($LASTEXITCODE) { throw "7-Zip failed to extract WebView2 for $arch" }
        $extracted = Join-Path $dependencies $entry
        $extractedExe = "$offline.extracted.exe"
        Move-Item -LiteralPath $extracted -Destination $extractedExe -Force
        Assert-MicrosoftPackage $extractedExe $package.OfflineHash
        Move-Item -LiteralPath $extractedExe -Destination $offline
    }
    Assert-MicrosoftPackage $offline $package.OfflineHash

    $payload = Join-Path $work "payload-$Version-$arch"
    $intermediate = Join-Path $work "obj-$arch/"
    dotnet publish (Join-Path $root 'Sputnik.csproj') -c Release -p:Win7=true "-p:PlatformTarget=$arch" "-p:BaseIntermediateOutputPath=$intermediate" "-p:Version=$Version" -o $payload
    if ($LASTEXITCODE) { throw "Sputnik build failed for $arch" }
    & (Join-Path $PSScriptRoot 'Prepare-Payload.ps1') -PublishDir $payload
    & $Compiler "/DAppVersion=$Version" "/DPublishDir=$payload" "/DInstallerOutput=$out" "/DLegacyArchitecture=$arch" "/DNetFrameworkInstaller=$framework" "/DWebViewInstaller=$offline" (Join-Path $PSScriptRoot 'Sputnik.iss')
    if ($LASTEXITCODE) { throw "Sputnik installer build failed for $arch" }
    Get-FileHash -LiteralPath (Join-Path $out "SputnikSetup-$Version-win7-$arch.exe") -Algorithm SHA256
}
