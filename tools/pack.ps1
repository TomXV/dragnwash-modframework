# Builds the core, the libraries and the preloader patcher, and packs them into
# release/DragNWash.ModFramework-<version>.zip with the layout of a game folder:
#
#   BepInEx/plugins/DragNWash.ModFramework/DragNWash.ModFramework.dll (+ LICENSE.txt, icon.png, ModsButton0.png, ModsButton1.png)
#   BepInEx/plugins/DragNWash.ModFramework.<Library>/DragNWash.ModFramework.<Library>.dll
#   BepInEx/patchers/DragNWash.ModFramework.Preloader.dll
#   installer/Install.exe, installer/install-steamdeck.sh, installer/mod-install.example.json
#   README.md, README.ja.md, CHANGELOG.md
#
# Players normally get the framework with a mod that needs it (Drag'n Wash
# Localization ships it); this zip is for mod authors and for installing by hand.
# The game's reference assemblies must be in src/DragNWash.ModFramework/libs
# (tools/copy-libs.ps1), so this runs on a machine with the game installed.
#
#   pwsh tools/pack.ps1
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Plugins = @(
    'DragNWash.ModFramework',
    'DragNWash.ModFramework.Text',
    'DragNWash.ModFramework.Dialogue',
    'DragNWash.ModFramework.ToolWindow',
    'DragNWash.ModFramework.Assets',
    'DragNWash.ModFramework.Saves',
    'DragNWash.ModFramework.Inspector',
    'DragNWash.ModFramework.Overrides'
)
$Patcher = 'DragNWash.ModFramework.Preloader'

if (-not $Version) {
    $csproj = Get-Content -LiteralPath (Join-Path $Root "src/DragNWash.ModFramework/DragNWash.ModFramework.csproj") -Raw
    if ($csproj -notmatch '<Version>([^<]+)</Version>') { throw 'Could not read the core version. Pass -Version.' }
    $Version = $Matches[1]
}

& python (Join-Path $Root 'tools/check-repo.py')
if ($LASTEXITCODE -ne 0) { throw 'tools/check-repo.py failed.' }

foreach ($name in $Plugins + $Patcher) {
    $project = Join-Path $Root "src/$name/$name.csproj"
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue (Join-Path $Root "src/$name/bin"), (Join-Path $Root "src/$name/obj")
    Write-Host "Building $name ..."
    dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) { throw "Build of $name failed." }
}

$OutDir = Join-Path $Root 'release'
$Stage = Join-Path $OutDir "DragNWash.ModFramework-$Version"
if (Test-Path -LiteralPath $Stage) { Remove-Item -LiteralPath $Stage -Recurse -Force }
foreach ($name in $Plugins) {
    $dir = Join-Path $Stage "BepInEx/plugins/$name"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Copy-Item -LiteralPath (Join-Path $Root "src/$name/bin/Release/$name.dll") -Destination $dir
}
Copy-Item -LiteralPath (Join-Path $Root 'LICENSE') -Destination (Join-Path $Stage 'BepInEx/plugins/DragNWash.ModFramework/LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $Root 'src/DragNWash.ModFramework/icon.png') -Destination (Join-Path $Stage 'BepInEx/plugins/DragNWash.ModFramework')
# The Options screen's Mods button, drawn for the framework by Mister ERIO.
foreach ($art in 'ModsButton0.png', 'ModsButton1.png') {
    Copy-Item -LiteralPath (Join-Path $Root "src/DragNWash.ModFramework/$art") -Destination (Join-Path $Stage 'BepInEx/plugins/DragNWash.ModFramework')
}
New-Item -ItemType Directory -Force -Path (Join-Path $Stage 'BepInEx/patchers') | Out-Null
Copy-Item -LiteralPath (Join-Path $Root "src/$Patcher/bin/Release/$Patcher.dll") -Destination (Join-Path $Stage 'BepInEx/patchers')
# The shared installer, for mods to ship next to their files (docs/INSTALLER.md).
# Built deterministically: the same sources give a byte-identical Install.exe.
$InstallerProject = Join-Path $Root 'installer/DragNWash.Installer.csproj'
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue (Join-Path $Root 'installer/bin'), (Join-Path $Root 'installer/obj')
dotnet build $InstallerProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build of the installer failed.' }
$InstallerStage = Join-Path $Stage 'installer'
New-Item -ItemType Directory -Force -Path $InstallerStage | Out-Null
Copy-Item -LiteralPath (Join-Path $Root 'installer/bin/Release/Install.exe') -Destination $InstallerStage
Copy-Item -LiteralPath (Join-Path $Root 'installer/install-steamdeck.sh') -Destination $InstallerStage
Copy-Item -LiteralPath (Join-Path $Root 'installer/mod-install.example.json') -Destination $InstallerStage
$InstallerHash = (Get-FileHash -LiteralPath (Join-Path $InstallerStage 'Install.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Install.exe sha256 $InstallerHash (unchanged unless installer/ or the .NET SDK changed)"

# The crash reporter the core starts on Windows (docs/CRASH_REPORTS.md), next to
# the core DLL. Built deterministically, like Install.exe.
$ReporterProject = Join-Path $Root 'crashreporter/DragNWash.CrashReporter.csproj'
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue (Join-Path $Root 'crashreporter/bin'), (Join-Path $Root 'crashreporter/obj')
dotnet build $ReporterProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build of the crash reporter failed.' }
Copy-Item -LiteralPath (Join-Path $Root 'crashreporter/bin/Release/CrashReporter.exe') -Destination (Join-Path $Stage 'BepInEx/plugins/DragNWash.ModFramework')
$ReporterHash = (Get-FileHash -LiteralPath (Join-Path $Stage 'BepInEx/plugins/DragNWash.ModFramework/CrashReporter.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "CrashReporter.exe sha256 $ReporterHash"

foreach ($doc in 'README.md', 'README.ja.md', 'CHANGELOG.md') {
    Copy-Item -LiteralPath (Join-Path $Root $doc) -Destination $Stage
}

$Zip = Join-Path $OutDir "DragNWash.ModFramework-$Version.zip"
if (Test-Path -LiteralPath $Zip) { Remove-Item -LiteralPath $Zip -Force }
Compress-Archive -Path (Join-Path $Stage '*') -DestinationPath $Zip
Write-Host "Created $Zip"
