<#
Builds Aether, runs its self-test and packages it.

  .\build.ps1 -Version 1.0.1               build + dist\Aether-1.0.1.zip
  .\build.ps1 -Version 1.0.1 -Publish      ...and publish GitHub release v1.0.1 (needs `gh auth login`)

Every installed copy checks the latest release on startup and updates itself when its version is lower,
so publishing is all it takes to ship an update. Bump the version every time.
#>
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [switch]$Publish,
    [string]$Notes = ""
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'SmartHunter\SmartHunter.csproj'
$out = Join-Path $root 'SmartHunter\bin\Release'
$dist = Join-Path $root 'dist'
$repo = 'noahwaseaten/Aether'

Write-Host "Building Aether $Version"
dotnet build $project -c Release -p:Version=$Version --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

# Self-test in a scratch folder so it doesn't write config files into the build output
$test = Join-Path ([IO.Path]::GetTempPath()) "aether-selftest-$([guid]::NewGuid())"
New-Item -ItemType Directory $test | Out-Null
Copy-Item (Join-Path $out 'Aether.exe') $test
$p = Start-Process (Join-Path $test 'Aether.exe') -ArgumentList '--selftest' -Wait -PassThru
$report = Get-Content (Join-Path $test 'SelfTest.txt') -Raw -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $test
if ($p.ExitCode -ne 0) { throw "Self-test failed:`n$report" }
Write-Host 'Self-test passed'

$exeVersion = (Get-Item (Join-Path $out 'Aether.exe')).VersionInfo.ProductVersion
if (-not $exeVersion.StartsWith($Version)) { throw "Aether.exe reports version $exeVersion, expected $Version" }

New-Item -ItemType Directory -Force $dist | Out-Null
$stage = Join-Path $dist 'Aether'
Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
New-Item -ItemType Directory $stage | Out-Null
Copy-Item (Join-Path $out 'Aether.exe') $stage
Copy-Item (Join-Path $root 'installer\*.cmd') $stage
Copy-Item (Join-Path $root 'installer\Read me.txt') $stage
$zip = Join-Path $dist "Aether-$Version.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Copy-Item (Join-Path $out 'Aether.exe') (Join-Path $dist 'Aether.exe') -Force
Write-Host "Packaged $zip"

if ($Publish) {
    if (-not $Notes) { $Notes = "Aether $Version. Download Aether-$Version.zip, extract it and run ""Install Aether.cmd"". Installed copies update themselves." }
    # Aether.exe must be attached as its own asset: that's the file the in-app updater downloads
    gh release create "v$Version" (Join-Path $dist 'Aether.exe') $zip --repo $repo --title "Aether $Version" --notes $Notes
    if ($LASTEXITCODE -ne 0) { throw 'Publishing the release failed' }
    Write-Host "Published https://github.com/$repo/releases/tag/v$Version"
}
