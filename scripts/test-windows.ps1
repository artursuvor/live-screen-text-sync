$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    dotnet build LiveScreenTextSync.sln -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet test tests/LiveScreenTextSync.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    $assembly = Join-Path $projectRoot 'src\LiveScreenTextSync.App\bin\Release\net10.0-windows10.0.19041.0\LiveScreenTextSync.App.dll'
    $report = Join-Path $projectRoot 'artifacts\local-capture-report.json'
    dotnet $assembly --self-test-local $report
    if ($LASTEXITCODE -ne 0) { throw "Windows integration checks failed. Read $report" }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (-not $result.Success) { throw $result.Error }
    $result.Checks | Format-Table Check, Passed -AutoSize
    $smokeReport = Join-Path $projectRoot 'artifacts\ui-smoke.json'
    dotnet $assembly --smoke-ui $smokeReport
    if ($LASTEXITCODE -ne 0) { throw 'UI startup/shutdown smoke check failed.' }
    $smoke = Get-Content -LiteralPath $smokeReport -Raw | ConvertFrom-Json
    if (-not $smoke.Success -or -not $smoke.WindowClosed) { throw 'UI smoke check did not complete successfully.' }
    Write-Output 'UI startup and shutdown: passed.'
} finally {
    Pop-Location
}
