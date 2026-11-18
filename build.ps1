param(
    [ValidateSet('Lite', 'Full')]
    [string]$Edition = 'Full'
)
$ErrorActionPreference = 'Stop'
$selfContained = ($Edition -eq 'Full').ToString().ToLowerInvariant()
$destination = Join-Path $PSScriptRoot "artifacts/$Edition"
$publishArguments = @(
    'publish', (Join-Path $PSScriptRoot 'OrbitLaunchpad.csproj'),
    '-c', 'Release', '-r', 'win-x64',
    '--self-contained', $selfContained,
    '-p:DebugType=None', '-o', $destination
)

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
