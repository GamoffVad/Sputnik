param([switch]$Legacy)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repoRoot
try {
    New-Item -ItemType Directory -Force -Path artifacts/qa | Out-Null
    Start-Transcript -Path (Join-Path $repoRoot ('artifacts/qa/run-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')) | Out-Null
    function Run-Dotnet {
        & dotnet @args
        if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $args" }
    }
    Run-Dotnet build Sputnik.csproj -c Release --nologo
    Run-Dotnet run --project tests/notification-check/Check.csproj -c Release -- artifacts/notification-check/current
    Run-Dotnet run --project tests/workspace-check/Check.csproj -c Release -- --components
    foreach ($mode in @('', '--appearance-only', '--notes-only', '--sphere-preview-only', '--glass-icons-only', '--clipping-only')) {
        Run-Dotnet run --no-build --project tests/workspace-check/Check.csproj -c Release -- $mode
    }
    Run-Dotnet run --project tests/fixed-area-check/Check.csproj -c Release
    Run-Dotnet run --project tests/neutral-pose-check/Check.csproj -c Release
    if ($Legacy) {
        foreach ($architecture in @('x64', 'x86')) {
            Run-Dotnet build Sputnik.csproj -c Release -p:Win7=true "-p:PlatformTarget=$architecture" "-p:BaseIntermediateOutputPath=artifacts/win7/obj-$architecture/" --nologo
            Run-Dotnet run --project tests/legacy-smoke/Check.csproj -c Release "-p:PlatformTarget=$architecture"
        }
    }
}
finally {
    Stop-Transcript -ErrorAction SilentlyContinue | Out-Null
    Pop-Location
}
