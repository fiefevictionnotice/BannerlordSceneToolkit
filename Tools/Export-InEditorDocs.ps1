# =====================================================================================
# Export-InEditorDocs.ps1
#
# Writes the in-editor documentation (the Documentation buttons' User Guide and F9's
# Technical Docs) out as Markdown, so it can be read on GitHub, searched with Ctrl+F in
# one file, or handed to an AI. The source of truth stays in the code:
#   src\BannerlordSceneToolkit\MaterialSwapTool\GUI\DocumentationVM.cs
# This script only reads that file - it parses the AddTopic(...) calls and the
# `private const string XxxDoc = "..." + "...";` constants, so no build is needed.
#
#   .\Tools\Export-InEditorDocs.ps1                 # -> docs\in-editor\
#   .\Tools\Export-InEditorDocs.ps1 -OutDir <dir>
#
# Sync-GitHubExport.ps1 runs this first, so the GitHub copy can never drift from the
# code. Throws if a topic names a constant it cannot find, rather than skipping it.
# =====================================================================================

param(
    [string] $OutDir
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $repo 'docs\in-editor' }
$src = Join-Path $repo 'src\BannerlordSceneToolkit\MaterialSwapTool\GUI\DocumentationVM.cs'
$text = [IO.File]::ReadAllText($src)

# --- Constants: name -> unescaped body ------------------------------------------------
$lit = '"(?:[^"\\]|\\.)*"'
$constRx = [regex] ('private const string (\w+)\s*=\s*((?:' + $lit + '|\s|\+|//[^\n]*)+?);')
$consts = @{}
foreach ($m in $constRx.Matches($text)) {
    $parts = [regex]::Matches($m.Groups[2].Value, $lit) | ForEach-Object {
        $s = $_.Value.Substring(1, $_.Value.Length - 2)
        [regex]::Replace($s, '\\(.)', { param($e) switch ($e.Groups[1].Value) { 'n' { "`n" } 't' { "`t" } default { $e.Groups[1].Value } } })
    }
    $consts[$m.Groups[1].Value] = ($parts -join '')
}

# --- Topics, in on-screen order; the technical set starts at BuildTechnicalTopics ------
$techStart = $text.IndexOf('private void BuildTechnicalTopics')
if ($techStart -lt 0) { throw "BuildTechnicalTopics not found in $src" }
$guide = @(); $tech = @()
foreach ($m in [regex]::Matches($text, 'AddTopic\((' + $lit + ')\s*,\s*(\w+)\)\s*;')) {
    $title = $m.Groups[1].Value; $title = $title.Substring(1, $title.Length - 2) -replace '\\(.)', '$1'
    $name = $m.Groups[2].Value
    if (-not $consts.ContainsKey($name)) { throw "Topic '$title' uses $name, which is not a parsable string constant" }
    $t = [pscustomobject]@{ Title = $title; Body = $consts[$name] }
    if ($m.Index -gt $techStart) { $tech += $t } else { $guide += $t }
}

# Plain text -> Markdown: escape what Markdown would eat, keep single line breaks.
function ToMarkdown([string] $s) {
    $s = $s -replace '\\', '\\' -replace '([*<>\[\]`])', '\$1'
    $s = $s -replace '(?m)^(\s*)#', '$1\#'
    ($s -split "`n`n" | ForEach-Object { $_ -replace "`n", "  `n" }) -join "`n`n"
}

function Slug([string] $s) { (($s.ToLower() -replace '[^a-z0-9 -]', '') -replace ' ', '-') }

function WriteDoc([string] $file, [string] $heading, [string] $intro, $topics) {
    $sb = New-Object Text.StringBuilder
    [void] $sb.AppendLine("# $heading").AppendLine()
    [void] $sb.AppendLine("> Generated from ``src/BannerlordSceneToolkit/MaterialSwapTool/GUI/DocumentationVM.cs`` by ``Tools/Export-InEditorDocs.ps1``. Do not edit by hand: change the code and re-run the export.").AppendLine()
    [void] $sb.AppendLine($intro).AppendLine()
    [void] $sb.AppendLine("## Topics").AppendLine()
    foreach ($t in $topics) { [void] $sb.AppendLine("- [$($t.Title)](#$(Slug $t.Title))") }
    [void] $sb.AppendLine()
    foreach ($t in $topics) {
        [void] $sb.AppendLine("## $($t.Title)").AppendLine()
        [void] $sb.AppendLine((ToMarkdown $t.Body)).AppendLine()
    }
    New-Item -ItemType Directory -Force (Split-Path $file -Parent) | Out-Null
    [IO.File]::WriteAllText($file, $sb.ToString().TrimEnd() + "`n", (New-Object Text.UTF8Encoding $false))
    Write-Host ("  {0,-20} {1} topics -> {2}" -f (Split-Path $file -Leaf), @($topics).Count, $file)
}

WriteDoc (Join-Path $OutDir 'USER-GUIDE.md') 'Scene Toolkit User Guide (in-editor)' `
    'The text behind every **Documentation** button in the toolkit''s panels, in the same order as the in-editor list.' $guide
WriteDoc (Join-Path $OutDir 'TECHNICAL-DOCS.md') 'Scene Toolkit Technical Docs (in-editor)' `
    'The engineering notes behind the **Technical Docs** button on F9: confirmed bugs, their diagnoses, and the engine limitations that shaped the tools.' $tech
