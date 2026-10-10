<#
Builds Aether, runs its self-test and packages it.

  .\build.ps1 -Version 1.0.1               build + test, the app goes in dist\Aether.exe
  .\build.ps1 -Version 1.0.1 -Publish -Notes "..."   ...and publish GitHub release v1.0.1 (needs `gh auth login`)
                                                    Notes are shown in the app as "What's new"; push main first

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
# Aether.exe is the whole app: run from anywhere, it installs itself (Core/Helpers/Installer.cs)
Copy-Item (Join-Path $out 'Aether.exe') (Join-Path $dist 'Aether.exe') -Force
# The portable zip: the same exe with portable.txt beside it keeps everything in its folder and installs nothing
$stage = Join-Path $dist 'Aether'
Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
New-Item -ItemType Directory $stage | Out-Null
Copy-Item (Join-Path $out 'Aether.exe') $stage
[IO.File]::WriteAllText((Join-Path $stage 'portable.txt'), "This file keeps Aether's settings in this folder instead of installing it.`r`nDelete it, and Aether installs itself the next time it starts.`r`n")
$zip = Join-Path $dist "Aether-$Version-portable.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "Packaged dist\Aether.exe and $(Split-Path $zip -Leaf)"

if ($Publish) {
    # The app shows these notes as "What's new", so they must say what changed
    if (-not $Notes) { throw 'Publishing needs -Notes: what players will notice in this version' }
    # Through a file: Windows PowerShell mangles native arguments that contain double quotes
    $notesFile = Join-Path $dist 'notes.md'
    [IO.File]::WriteAllText($notesFile, $Notes)
    # Aether.exe must be attached as its own asset: that's the file the in-app updater downloads
    gh release create "v$Version" (Join-Path $dist 'Aether.exe') $zip --repo $repo --title "Aether $Version" --notes-file $notesFile
    if ($LASTEXITCODE -ne 0) { throw 'Publishing the release failed' }
    Write-Host "Published https://github.com/$repo/releases/tag/v$Version"
}
