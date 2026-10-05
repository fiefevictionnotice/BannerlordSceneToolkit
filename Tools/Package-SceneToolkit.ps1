# =====================================================================================
# Package-SceneToolkit.ps1
#
# Assembles a shareable zip of the Bannerlord Scene Toolkit. RUN BY HAND, ON REQUEST
# ONLY - nothing in the build invokes this, deliberately: packaging is a decision, not
# a side effect.
#
#   .\Package-SceneToolkit.ps1            -> stage + zip into Distribution\
#   .\Package-SceneToolkit.ps1 -NoZip     -> stage only (inspect before zipping)
#
# What it does, mirroring the manual steps that used to live only in the README:
#   1. Copies the DEPLOYED module (Modules\BannerlordSceneToolkit in the game install)
#      into Distribution\SceneToolkit\BannerlordSceneToolkit, excluding SceneObj\,
#      SceneEditData\ and *.bak - working data, not part of the mod.
#   2. Warns if the deployed DLL looks older than the newest source file, because a
#      stale deploy is exactly how a package silently ships last week's build. It
#      warns rather than builds: building deploys into the game folder, and that must
#      never happen while the editor might be open.
#   3. Zips the staging folder together with README.txt, LICENSE.txt and
#      tool_toggles.txt as SceneToolkit-<version>-<date>.zip, version read from the
#      module's own SubModule.xml.
# =====================================================================================

param(
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$DistDir    = Join-Path $RepoRoot "Distribution\SceneToolkit"
$StageDir   = Join-Path $DistDir  "BannerlordSceneToolkit"
$SrcDir     = Join-Path $RepoRoot "src\BannerlordSceneToolkit"
$ModuleDir  = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\BannerlordSceneToolkit"

if (-not (Test-Path $ModuleDir)) { throw "Deployed module not found at $ModuleDir - build (with the editor CLOSED) first." }
if (-not (Test-Path $DistDir))   { throw "Distribution folder not found at $DistDir." }

# --- Staleness check: deployed DLL vs newest source file --------------------------------
$dll = Get-ChildItem "$ModuleDir\bin\Win64_Shipping_wEditor\BannerlordSceneToolkit.dll" -ErrorAction SilentlyContinue
$newestSrc = Get-ChildItem $SrcDir -Recurse -File -Include *.cs,*.xml |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($null -eq $dll) {
    Write-Warning "No deployed DLL found - this package would ship data files with no assembly."
} elseif ($newestSrc.LastWriteTime -gt $dll.LastWriteTime) {
    Write-Warning ("Deployed DLL ({0:g}) is OLDER than the newest source file {1} ({2:g})." -f `
        $dll.LastWriteTime, $newestSrc.Name, $newestSrc.LastWriteTime)
    Write-Warning "The package will contain the OLD build. Close the editor, rebuild, then rerun this."
    $answer = Read-Host "Package the old build anyway? (y/N)"
    if ($answer -ne "y") { Write-Host "Stopped - nothing was packaged."; exit 1 }
}

# --- Stage ------------------------------------------------------------------------------
if (Test-Path $StageDir) { Remove-Item $StageDir -Recurse -Force }
Write-Host "Staging $ModuleDir -> $StageDir (excluding SceneObj, SceneEditData, *.bak)..."
robocopy $ModuleDir $StageDir /E /XD SceneObj SceneEditData /XF *.bak /NFL /NDL /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }

# --- Version + zip ----------------------------------------------------------------------
$version = "unknown"
$manifest = Join-Path $StageDir "SubModule.xml"
if (Test-Path $manifest) {
    $m = Select-String -Path $manifest -Pattern 'Version value="([^"]+)"'
    if ($m) { $version = $m.Matches[0].Groups[1].Value }
}

if ($NoZip) {
    Write-Host "Staged (version $version). No zip made (-NoZip). Zip contents when ready:"
    Write-Host "  $StageDir + README.txt, LICENSE.txt, tool_toggles.txt"
    exit 0
}

$zipName = "SceneToolkit-$version-$(Get-Date -Format yyyyMMdd-HHmm).zip"
$zipPath = Join-Path (Join-Path $RepoRoot "Distribution") $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath }

Write-Host "Zipping -> $zipPath"
Compress-Archive -Path $StageDir, (Join-Path $DistDir "README.txt"), (Join-Path $DistDir "LICENSE.txt"), (Join-Path $DistDir "tool_toggles.txt") -DestinationPath $zipPath

Write-Host "Done: $zipPath (version $version)"
