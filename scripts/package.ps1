param(
    [string]$Runtime = 'win-x64',
    [ValidatePattern('^[A-Za-z0-9._-]+$')][string]$OutputName = 'LiveScreenTextSync-win-x64'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $projectRoot "artifacts\$OutputName"
$archive = Join-Path $projectRoot "artifacts\$OutputName.zip"

Push-Location $projectRoot
try {
    dotnet publish src/LiveScreenTextSync.App -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publishing failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $publishDirectory -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\validation.md') -Destination $publishDirectory -Force
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archive -Force
    Get-Item -LiteralPath $archive | Select-Object FullName, Length
    Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Format-List Path, Hash
} finally {
    Pop-Location
}
