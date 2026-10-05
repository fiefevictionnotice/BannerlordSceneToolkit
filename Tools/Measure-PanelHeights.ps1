# =====================================================================================
# Measure-PanelHeights.ps1
#
# Reports, for every GUI panel, the height its rows actually need versus the height the
# panel declares. Read-only by default; -Fix rewrites the declared height of panels it
# can measure exactly.
#
#   .\Measure-PanelHeights.ps1              -> report
#   .\Measure-PanelHeights.ps1 -Fix         -> also correct the all-fixed panels
#   .\Measure-PanelHeights.ps1 -Cushion 20  -> leave more slack than the default 10px
#
# WHY THIS EXISTS. Panel height is hand-maintained: add a row to a fixed-height panel and
# the content runs off the bottom edge (buttons vanish), remove rows and the panel keeps
# a lump of dead space. Both have happened repeatedly - a Close button pushed off the
# bottom, and F6 left 118px too tall after its swapper section moved to F5. The arithmetic
# is trivial and worth not doing in your head.
#
# WHAT IT WILL NOT TOUCH. A panel whose content includes a StretchToParent or
# CoverChildren row is SUPPOSED to have slack: that row is a scrollable list filling
# whatever space is left, and shrinking the panel would shrink the list. Those are
# reported as SCROLLS and never rewritten, even with -Fix.
# =====================================================================================

param(
    [switch]$Fix,
    [int]$Cushion = 10
)

$ErrorActionPreference = "Stop"
$src = Join-Path (Split-Path -Parent $PSScriptRoot) "src\BannerlordSceneToolkit"

function Measure-One($path) {
    try { [xml]$doc = Get-Content $path -Raw } catch { return $null }
    $root = $doc.Prefab.Window.Widget
    if (-not $root) { return $null }
    $list = $root.Children.ChildNodes | Where-Object { $_.LocalName -eq 'ListPanel' } | Select-Object -First 1
    if (-not $list) { return $null }

    $flexible = @($list.Children.ChildNodes | Where-Object {
        $_.NodeType -eq 'Element' -and $_.HeightSizePolicy -in @('StretchToParent', 'CoverChildren')
    }).Count

    $outerTop = 0; $outerBot = 0
    if ($list.MarginTop)    { $outerTop = [int]$list.MarginTop }
    if ($list.MarginBottom) { $outerBot = [int]$list.MarginBottom }

    $sum = 0
    foreach ($c in $list.Children.ChildNodes) {
        if ($c.NodeType -ne 'Element') { continue }
        $h = 0; $mt = 0; $mb = 0
        if ($c.SuggestedHeight) { $h  = [int]$c.SuggestedHeight }
        if ($c.MarginTop)       { $mt = [int]$c.MarginTop }
        if ($c.MarginBottom)    { $mb = [int]$c.MarginBottom }
        $sum += $h + $mt + $mb
    }

    [pscustomobject]@{
        Path     = $path
        Name     = Split-Path $path -Leaf
        Needed   = $sum + $outerTop + $outerBot
        Declared = [int]$root.SuggestedHeight
        Flexible = $flexible
    }
}

$panels = Get-ChildItem $src -Recurse -Filter *.xml |
    Where-Object { $_.FullName -match '\\Prefabs\\' -and $_.FullName -notmatch '\\(bin|obj)\\' }

$fixedCount = 0
foreach ($p in $panels) {
    $m = Measure-One $p.FullName
    if (-not $m) { continue }
    $slack = $m.Declared - $m.Needed

    if ($m.Flexible -gt 0) {
        Write-Host ("SCROLLS    {0,-42} declared={1,5}  ({2} flexible row(s) - slack is by design)" -f $m.Name, $m.Declared, $m.Flexible)
        continue
    }

    if ([Math]::Abs($slack) -lt 40) {
        Write-Host ("ok         {0,-42} needed={1,5} declared={2,5} slack={3,5}" -f $m.Name, $m.Needed, $m.Declared, $slack)
        continue
    }

    $label = if ($slack -lt 0) { "TOO SMALL " } else { "OVERSIZED " }
    Write-Host ("{0} {1,-42} needed={2,5} declared={3,5} slack={4,5}" -f $label, $m.Name, $m.Needed, $m.Declared, $slack) -ForegroundColor Yellow

    if ($Fix) {
        $target = $m.Needed + $Cushion
        $text = Get-Content $m.Path -Raw
        $updated = [regex]::Replace($text, 'SuggestedHeight="' + $m.Declared + '"', 'SuggestedHeight="' + $target + '"', 1)
        if ($updated -ne $text) {
            Set-Content $m.Path $updated -Encoding UTF8
            Write-Host ("           -> set to {0}" -f $target) -ForegroundColor Green
            $fixedCount++
        }
    }
}

if ($Fix) { Write-Host "`n$fixedCount panel(s) resized. Rebuild to deploy." }
else      { Write-Host "`nReport only. Re-run with -Fix to correct the resizable ones." }
