# Converts the GEDCOM and builds the site into public\.
#   .\build.ps1 path\to\tree.ged          build once
#   .\build.ps1 path\to\tree.ged -Serve   build, then preview at http://127.0.0.1:1313
param(
    [Parameter(Mandatory = $true)][string]$Gedcom,
    [switch]$Serve
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$tool = if ($env:GEDCOM_HUGO_PROJECT) { $env:GEDCOM_HUGO_PROJECT } else { '..\src\GedcomHugo' }

dotnet run --project $tool -c Release -- --gedcom $Gedcom --out _generated --overrides overrides.txt
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Hugo never deletes pages it didn't render this time, so a person withheld since
# the last build would otherwise linger in public\. Always start from empty.
if (Test-Path public) { Remove-Item public -Recurse -Force }

if ($Serve) { hugo server --bind 127.0.0.1 } else { hugo --minify --gc }
exit $LASTEXITCODE
