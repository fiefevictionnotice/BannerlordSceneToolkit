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
#   docs\scene-toolkit\             CHANGELOG, KNOWN-ISSUES, ROADMAP, TROUBLESHOOTING,
#                                   TUTORIAL-TRANSCRIPT, in-editor\ (generated from the code by
#                                   Export-InEditorDocs.ps1), not-yet-in-editor\, ALL-IN-ONE
#   docs\misc\                      MAPPING-GUIDE (the working notes stay home)
#   Tools\*.ps1                     including this script
#   release\                        the shipped README.txt and tool_toggles.txt
#   README.md, LICENSE.md, images\  from docs\github\ (the GitHub-facing copies)
#   guides\                         from docs\github\guides\ (community 3D/scene guides + sources)
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

# Docs - curated list, not the folder. Two groups on GitHub (2026-10-26):
#   docs\scene-toolkit\  the toolkit's own docs: in-editor topics (regenerated from the code
#                        right here), drafts not yet in the editor, troubleshooting, known
#                        issues, roadmap, changelog, the tutorial transcript, and one
#                        ALL-IN-ONE file of all of it for Ctrl+F or an AI
#   docs\misc\           everything that isn't about the toolkit itself (mapping guide)
# Index pages for both come from docs\github\docs\.
& (Join-Path $PSScriptRoot 'Export-InEditorDocs.ps1') | Out-Null
$docsOut = Join-Path $ExportDir 'docs'
$stDocs = Join-Path $docsOut 'scene-toolkit'
$miscDocs = Join-Path $docsOut 'misc'
$toolkitDocs = @('TROUBLESHOOTING.md', 'KNOWN-ISSUES.md', 'ROADMAP.md', 'TUTORIAL-TRANSCRIPT.md', 'CHANGELOG.md')
$miscList = @('MAPPING-GUIDE.md')
if ($PSCmdlet.ShouldProcess($docsOut, 'rebuild docs')) {
    if (Test-Path $docsOut) { Remove-Item $docsOut -Recurse -Force }
}
foreach ($d in $toolkitDocs) { CopyFile (Join-Path $repo "docs\$d") (Join-Path $stDocs $d) }
foreach ($d in $miscList)    { CopyFile (Join-Path $repo "docs\$d") (Join-Path $miscDocs $d) }
Mirror (Join-Path $repo 'docs\in-editor')         (Join-Path $stDocs 'in-editor')
Mirror (Join-Path $repo 'docs\not-yet-in-editor') (Join-Path $stDocs 'not-yet-in-editor')
CopyFile (Join-Path $repo 'docs\github\docs\scene-toolkit\README.md') (Join-Path $stDocs 'README.md')
CopyFile (Join-Path $repo 'docs\github\docs\misc\README.md')          (Join-Path $miscDocs 'README.md')

# ALL-IN-ONE: every toolkit doc in one file, most-used first, the long history last.
# Each part: title, source in this repo, wrap as plain text?, path in the GitHub repo.
$allParts = @(
    , @('Installing (release README.txt)', 'Distribution\SceneToolkit\README.txt', $true, 'release/README.txt')
    , @('In-editor User Guide',            'docs\in-editor\USER-GUIDE.md', $false, 'docs/scene-toolkit/in-editor/USER-GUIDE.md')
    , @('In-editor Technical Docs',        'docs\in-editor\TECHNICAL-DOCS.md', $false, 'docs/scene-toolkit/in-editor/TECHNICAL-DOCS.md')
)
Get-ChildItem (Join-Path $repo 'docs\not-yet-in-editor') -Filter *.md | Sort-Object Name | ForEach-Object {
    $allParts += , @("Not yet in the editor: $($_.BaseName)", "docs\not-yet-in-editor\$($_.Name)", $false, "docs/scene-toolkit/not-yet-in-editor/$($_.Name)")
}
foreach ($d in $toolkitDocs) { $allParts += , @(($d -replace '\.md$', ''), "docs\$d", $false, "docs/scene-toolkit/$d") }
$all = New-Object Text.StringBuilder
[void] $all.AppendLine('# Bannerlord Scene Toolkit - all documentation in one file').AppendLine()
[void] $all.AppendLine('> Generated by `Tools/Sync-GitHubExport.ps1` from the files in this folder, for searching everything at once or uploading to an AI. Each part starts with a `# PART:` heading.').AppendLine()
foreach ($p in $allParts) {
    $body = [IO.File]::ReadAllText((Join-Path $repo $p[1])).Trim()
    if ($p[2]) { $body = "``````text`n$body`n``````" }
    [void] $all.AppendLine('---').AppendLine().AppendLine("# PART: $($p[0])").AppendLine().AppendLine("Source: ``$($p[3])``").AppendLine()
    [void] $all.AppendLine($body).AppendLine()
}
$allOut = Join-Path $stDocs 'SCENE-TOOLKIT-DOCS-ALL-IN-ONE.md'
if ($PSCmdlet.ShouldProcess($allOut, 'write all-in-one')) {
    [IO.File]::WriteAllText($allOut, $all.ToString().TrimEnd() + "`n", (New-Object Text.UTF8Encoding $false))
}
Write-Host ("  {0,-28} -> {1}" -f 'ALL-IN-ONE', $allOut)

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
Mirror (Join-Path $repo 'docs\github\guides') (Join-Path $ExportDir 'guides') -xf @('*.zip')

# Safety net: nothing private may have slipped through
$leaks = Get-ChildItem $ExportDir -Recurse -Force -File | Where-Object {
    $_.FullName -match '\\_private-ideas\\|\\Distribution\\|\\SceneObj\\|\\SceneEditData\\|\\bin\\|\\obj\\' -or
    $_.Name -match '\.backup-|\.bak(\d|-|$)|^session_.*\.txt$'
} | Where-Object { $_.FullName -notmatch '\\\.git\\' }
if ($leaks) { $leaks | ForEach-Object { Write-Warning "should not be in the export: $($_.FullName)" }; exit 1 }

Write-Host "Done. Next: cd `"$ExportDir`"; git status; git add -A; git commit; git push"
