$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

try {
    $project = Join-Path $projectRoot 'src\LiveScreenTextSync.App\LiveScreenTextSync.App.csproj'
    [xml]$projectXml = Get-Content -LiteralPath $project -Raw
    $version = [string]$projectXml.Project.PropertyGroup.Version
    $executableName = 'LiveScreenTextSync.App.exe'
    $candidates = @(
        (Join-Path $projectRoot "artifacts\LiveScreenTextSync-v$version-win-x64\$executableName"),
        (Join-Path $projectRoot "artifacts\LiveScreenTextSync-win-x64\$executableName")
    )
    $executable = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1

    if (-not $executable) {
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
            throw 'No portable build found. Install the .NET 10 SDK or extract a portable build into artifacts\LiveScreenTextSync-win-x64.'
        }
        $publishDirectory = Join-Path $projectRoot 'artifacts\LiveScreenTextSync-win-x64'
        Push-Location $projectRoot
        try {
            dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $publishDirectory
            if ($LASTEXITCODE -ne 0) { throw 'Building the application failed.' }
        } finally {
            Pop-Location
        }
        $executable = Join-Path $publishDirectory $executableName
    }

    Start-Process -FilePath $executable -WorkingDirectory (Split-Path -Parent $executable) | Out-Null
} catch {
    Write-Host "Unable to start Live Screen Text Sync: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
