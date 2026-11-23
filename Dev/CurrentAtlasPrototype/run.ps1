param(
    [ValidateSet('CoreCLR','Framework')][string]$Runtime = 'Framework',
    [Parameter(Mandatory=$true)][string]$OutputDir,
    [switch]$ChecksOnly
)
$ErrorActionPreference = 'Stop'
$results = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir)
if (Test-Path -LiteralPath $results) { throw "Output directory already exists: $results" }
$runArgs = @($results)
if ($ChecksOnly) { $runArgs += '--checks-only' }
if ($Runtime -eq 'CoreCLR') {
    & dotnet run --project (Join-Path $PSScriptRoot 'CurrentAtlas.Prototype.csproj') -c Release -- @runArgs
    exit $LASTEXITCODE
}
$sdk = (& dotnet --version).Trim()
$csc = Join-Path $env:ProgramFiles "dotnet\sdk\$sdk\Roslyn\bincore\csc.dll"
$fx = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$exeDir = Join-Path $PSScriptRoot 'bin/Framework'
New-Item -ItemType Directory -Force -Path $exeDir | Out-Null
$exe = Join-Path $exeDir 'CurrentAtlas.Prototype.exe'
& dotnet $csc /noconfig /nologo /langversion:7 /optimize+ /target:exe /platform:x64 "/out:$exe" `
    "/reference:$fx\mscorlib.dll" "/reference:$fx\System.dll" "/reference:$fx\System.Core.dll" `
    "/reference:$fx\System.IO.Compression.dll" (Join-Path $PSScriptRoot 'Program.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $exe @runArgs
exit $LASTEXITCODE
