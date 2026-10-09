# Sync-GitHubExport.ps1 - refresh the public GitHub export of BannerlordSceneToolkit.
#
# The working folder (this repo) has no git, by choice. The public repository lives in a
# SEPARATE folder (default: Documents\BannerlordSceneToolkit-github) that this script fills
# with a curated copy of the working tree. Run it, review `git status` there, commit, push.
#
#   .\Tools\Sync-GitHubExport.ps1            # sync into the default export folder
#   .\Tools\Sync-GitHubExport.ps1 -WhatIf    # list what would be copied/removed, touch nothing
#
# WHAT GOES IN
#   src\BannerlordSceneToolkit\**   minus bin\, obj\, *.backup-*, *.bak, *.bak2
#   docs\                           CHANGELOG, KNOWN-ISSUES, ROADMAP, TROUBLESHOOTING,
#                                   TUTORIAL-TRANSCRIPT, MAPPING-GUIDE (the working notes stay home)
#   Tools\*.ps1                     including this script
#   release\                        the shipped README.txt and tool_toggles.txt
#   README.md, LICENSE.md, images\  from docs\github\ (the GitHub-facing copies)
#
# WHAT NEVER GOES IN
#   _private-ideas\, Distribution\ (zips), bin\obj\, backup copies, session logs, and
#   anything under the game install (SceneObj\, SceneEditData\ - your scenes and prefabs
#   were never in the source tree to begin with).
#
# Files that exist in the export but no longer in the curated set are REMOVED from the
# export (robocopy /MIR per subtree), so a rename upstream does not leave a stale twin.
# The export's .git\ folder is never touched.
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $ExportDir = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'BannerlordSceneToolkit-github')
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

function Mirror([string] $from, [string] $to, [string[]] $xd = @(), [string[]] $xf = @()) {
    if (-not (Test-Path $from)) { Write-Warning "missing source: $from"; return }
    $args = @($from, $to, '/MIR', '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/R:1', '/W:1')
    if ($xd.Count) { $args += '/XD'; $args += $xd }
    if ($xf.Count) { $args += '/XF'; $args += $xf }
    if ($WhatIfPreference) { $args += '/L' }
    & robocopy @args | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE) for $from" }
    Write-Host ("  {0,-28} -> {1}" -f (Split-Path $from -Leaf), $to)
}

function CopyFile([string] $from, [string] $to) {
    if (-not (Test-Path $from)) { Write-Warning "missing source: $from"; return }
    if ($PSCmdlet.ShouldProcess($to, "copy $from")) {
        New-Item -ItemType Directory -Force (Split-Path $to -Parent) | Out-Null
        Copy-Item $from $to -Force
    }
    Write-Host ("  {0,-28} -> {1}" -f (Split-Path $from -Leaf), $to)
}

Write-Host "Export: $ExportDir"
New-Item -ItemType Directory -Force $ExportDir | Out-Null

# Source tree
Mirror (Join-Path $repo 'src\BannerlordSceneToolkit') (Join-Path $ExportDir 'src\BannerlordSceneToolkit') `
    -xd @('bin', 'obj') -xf @('*.backup-*', '*.bak', '*.bak2', '*.bak-*')

# Docs - curated list, not the folder
$docsOut = Join-Path $ExportDir 'docs'
New-Item -ItemType Directory -Force $docsOut | Out-Null
$keepDocs = @('CHANGELOG.md', 'KNOWN-ISSUES.md', 'MAPPING-GUIDE.md', 'ROADMAP.md', 'TROUBLESHOOTING.md', 'TUTORIAL-TRANSCRIPT.md')
foreach ($d in $keepDocs) { CopyFile (Join-Path $repo "docs\$d") (Join-Path $docsOut $d) }
Get-ChildItem $docsOut -File | Where-Object { $keepDocs -notcontains $_.Name } | ForEach-Object {
    if ($PSCmdlet.ShouldProcess($_.FullName, 'remove stale doc')) { Remove-Item $_.FullName }
}

# Tools
Mirror (Join-Path $repo 'Tools') (Join-Path $ExportDir 'Tools')

# Shipped user-facing files
CopyFile (Join-Path $repo 'Distribution\SceneToolkit\README.txt')       (Join-Path $ExportDir 'release\README.txt')
CopyFile (Join-Path $repo 'Distribution\SceneToolkit\tool_toggles.txt') (Join-Path $ExportDir 'release\tool_toggles.txt')

# GitHub-facing files
CopyFile (Join-Path $repo 'docs\github\README.md')  (Join-Path $ExportDir 'README.md')
CopyFile (Join-Path $repo 'docs\github\LICENSE.md') (Join-Path $ExportDir 'LICENSE.md')
CopyFile (Join-Path $repo 'docs\github\.gitignore') (Join-Path $ExportDir '.gitignore')
Mirror (Join-Path $repo 'docs\github\images') (Join-Path $ExportDir 'images')

# Safety net: nothing private may have slipped through
$leaks = Get-ChildItem $ExportDir -Recurse -Force -File | Where-Object {
    $_.FullName -match '\\_private-ideas\\|\\Distribution\\|\\SceneObj\\|\\SceneEditData\\|\\bin\\|\\obj\\' -or
    $_.Name -match '\.backup-|\.bak(\d|-|$)|^session_.*\.txt$'
} | Where-Object { $_.FullName -notmatch '\\\.git\\' }
if ($leaks) { $leaks | ForEach-Object { Write-Warning "should not be in the export: $($_.FullName)" }; exit 1 }

Write-Host "Done. Next: cd `"$ExportDir`"; git status; git add -A; git commit; git push"
