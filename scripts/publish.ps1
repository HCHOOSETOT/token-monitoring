param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$baseDirectory = Join-Path $artifacts "_TokenMonitoring-$Runtime-base"
$project = Join-Path $root "src\TokenMonitoring\TokenMonitoring.csproj"

dotnet build (Join-Path $root "TokenMonitoring.slnx") -c Release
dotnet run --project (Join-Path $root "tests\TokenMonitoring.Tests\TokenMonitoring.Tests.csproj") -c Release --no-build

if (Test-Path $baseDirectory) {
    Remove-Item -LiteralPath $baseDirectory -Recurse -Force
}

dotnet publish $project `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $baseDirectory

$packages = @(
    @{
        Suffix = "Chinese"
        Language = "zh-CN"
        Readme = "README.md"
        Security = "SECURITY.md"
    },
    @{
        Suffix = "English"
        Language = "en-US"
        Readme = "README.English.md"
        Security = "SECURITY.English.md"
    }
)

foreach ($package in $packages) {
    $name = "TokenMonitoring-$Runtime-$($package.Suffix)"
    $directory = Join-Path $artifacts $name
    $zipPath = Join-Path $artifacts "$name.zip"

    if (Test-Path $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
    if (Test-Path $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    New-Item -ItemType Directory -Path $directory | Out-Null
    Copy-Item -Path (Join-Path $baseDirectory "*") -Destination $directory -Recurse
    Get-ChildItem -LiteralPath $directory -Filter "*.pdb" -File | Remove-Item -Force
    Set-Content -LiteralPath (Join-Path $directory "language.txt") -Value $package.Language -Encoding ascii
    Copy-Item (Join-Path $root $package.Readme) (Join-Path $directory "README.md")
    Copy-Item (Join-Path $root $package.Security) (Join-Path $directory "SECURITY.md")
    Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md") $directory
    Copy-Item (Join-Path $root "LICENSE") $directory
    Compress-Archive -Path (Join-Path $directory "*") -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Published: $zipPath"
}

Remove-Item -LiteralPath $baseDirectory -Recurse -Force

$legacyDirectory = Join-Path $artifacts "TokenMonitoring-$Runtime"
$legacyZip = "$legacyDirectory.zip"
if (Test-Path $legacyDirectory) {
    Remove-Item -LiteralPath $legacyDirectory -Recurse -Force
}
if (Test-Path $legacyZip) {
    Remove-Item -LiteralPath $legacyZip -Force
}
