# Copy the reference assemblies the framework compiles against from your own game
# install into src/DragNWash.ModFramework/libs. They are game and BepInEx files, so
# they are never committed.
#
#   pwsh tools/copy-libs.ps1
#   pwsh tools/copy-libs.ps1 -GamePath "D:\SteamLibrary\steamapps\common\Drag'n Wash"
param(
    [string]$GamePath = "C:\Program Files (x86)\Steam\steamapps\common\Drag'n Wash"
)

$ErrorActionPreference = 'Stop'

$libs = Join-Path $PSScriptRoot '..\src\DragNWash.ModFramework\libs'
$managed = Join-Path $GamePath 'DragNWash_Data\Managed'
$core = Join-Path $GamePath 'BepInEx\core'

if (-not (Test-Path -LiteralPath $managed)) {
    throw "Game not found at '$GamePath'. Pass -GamePath."
}
if (-not (Test-Path -LiteralPath $core)) {
    throw "BepInEx is not installed in '$GamePath' (no BepInEx\core)."
}

$fromCore = @('BepInEx.dll', '0Harmony.dll', 'Mono.Cecil.dll')
$fromManaged = @(
    'Assembly-CSharp.dll',
    'UnityEngine.dll',
    'UnityEngine.CoreModule.dll',
    'UnityEngine.UI.dll',
    'UnityEngine.UIModule.dll',
    'UnityEngine.TextRenderingModule.dll',
    'UnityEngine.IMGUIModule.dll',
    'UnityEngine.AssetBundleModule.dll',
    'UnityEngine.TextCoreFontEngineModule.dll',
    'UnityEngine.UnityWebRequestModule.dll',
    'UnityEngine.JSONSerializeModule.dll',
    'Unity.TextMeshPro.dll',
    'Unity.InputSystem.dll',
    'Naelstrof.UnityScriptableSettings.dll',
    'Unity.Localization.dll',
    'YarnSpinner.dll',
    'YarnSpinner.Unity.dll',
    'Yarn.Google.Protobuf.dll'
)

New-Item -ItemType Directory -Force -Path $libs | Out-Null
foreach ($name in $fromCore) {
    Copy-Item -LiteralPath (Join-Path $core $name) -Destination $libs -Force
    Write-Host "copied $name"
}
foreach ($name in $fromManaged) {
    Copy-Item -LiteralPath (Join-Path $managed $name) -Destination $libs -Force
    Write-Host "copied $name"
}
