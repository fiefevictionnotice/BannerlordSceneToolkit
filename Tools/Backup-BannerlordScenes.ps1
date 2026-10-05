# =====================================================================================
# Backup-BannerlordScenes.ps1
#
# WATCH MODE (default): stays running and takes a snapshot every few minutes for as
# long as the window is open. Close the window or press Ctrl+C to stop. No scheduled
# task, nothing left running in the background afterwards.
#   .\Backup-BannerlordScenes.ps1           -> watch, snapshot every 5 minutes
#   .\Backup-BannerlordScenes.ps1 -Once     -> single snapshot, then exit
#
# Every snapshot is a COMPLETE, browsable folder, but only files that actually changed
# consume new disk space - unchanged files are hardlinked to the previous snapshot.
#
# WHY IT IS BUILT THIS WAY (the obvious approach is wrong):
#   The usual recipe is "hardlink-clone the last snapshot, then robocopy /MIR over it."
#   On Windows that CORRUPTS HISTORY. Robocopy overwrites a destination file in place,
#   so writing a changed file also rewrites the previous snapshot's copy through the
#   shared link - silently, with no error. This script therefore compares each file
#   itself and NEVER writes over an existing link: unchanged -> new hardlink,
#   changed/new -> an independent fresh copy. Older snapshots keep their own version.
#
#   The payoff over a plain incremental: each snapshot is a full tree you can open in
#   Explorer and drag a file out of. No chain to reassemble at the moment you need it.
#
# TO RESTORE: open the snapshot folder, find the file, copy it back.
# =====================================================================================

param(
    [switch]$Once,                  # take one snapshot and exit
    [int]$IntervalMinutes = 5       # watch-mode cadence
)

# --- Settings -----------------------------------------------------------------------
$BackupRoot    = "E:\BannerlordBackups"   # E: has the most free space (~3.4 TB)
$KeepSnapshots = 150                      # browsable full-tree snapshots (recent history)
$KeepVersionsPerFile = 50                  # per-file history, long-term and independent of the above
$BL = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules"

$Sources = [ordered]@{
    # Scenes
    "Multiplayer_SceneObj"    = "$BL\Multiplayer\SceneObj"
    "Native_SceneObj"         = "$BL\Native\SceneObj"
    "Native_SceneEditData"    = "$BL\Native\SceneEditData"
    "SandBoxCore_SceneObj"    = "$BL\SandBoxCore\SceneObj"
    "SandBoxCore_SceneEditData" = "$BL\SandBoxCore\SceneEditData"
    # Whole FiefMaps module - not just SceneObj/SceneEditData, so Atmospheres,
    # FromMultiplayer, _Archive and the loose working files come along too.
    "FiefMaps"                = "$BL\FiefMaps"
    # Prefabs
    "Native_Prefabs"          = "$BL\Native\Prefabs"
    "SandBoxCore_Prefabs"     = "$BL\SandBoxCore\Prefabs"
    "SandBox_Prefabs"         = "$BL\SandBox\Prefabs"
    "Toolkit_Prefabs"         = "$BL\BannerlordSceneToolkit\Prefabs"
    "ClaudeTesting_Prefabs"   = "$BL\ClaudeTesting\Prefabs"
    # Tool data: presets, palettes, categories, culture definitions, change log
    "Toolkit_Data"            = "$env:USERPROFILE\Documents\Mount and Blade II Bannerlord\MaterialSwapTool"
    "CustomSceneCreator"      = "$env:USERPROFILE\Documents\Mount and Blade II Bannerlord\CustomSceneCreator"
}
# Missing folders are skipped silently, so extra entries here cost nothing.
# ------------------------------------------------------------------------------------

$ErrorActionPreference = "Stop"

function Invoke-Snapshot {
    $started  = Get-Date
    $snapRoot = Join-Path $BackupRoot "snapshots"
    if (-not (Test-Path $snapRoot)) { New-Item -ItemType Directory -Path $snapRoot -Force | Out-Null }

    # Previous snapshot = newest existing. Hardlinks require one volume, guaranteed
    # because every snapshot lives under $BackupRoot.
    $prev = Get-ChildItem $snapRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notlike "_partial_*" } |
            Sort-Object Name -Descending | Select-Object -First 1
    # Build under a "_partial_" name and rename only once the whole snapshot is written.
    # Found the hard way: killing the script mid-run left a folder that LOOKED like a
    # complete snapshot but held roughly half the files - and nothing about it said so.
    # Restoring from that would silently hand you an incomplete scene set. A partial now
    # keeps its _partial_ prefix, so it is obvious in Explorer and is ignored when
    # picking the previous snapshot to link against.
    $snapName    = $started.ToString("yyyy-MM-dd_HHmmss")
    $snapPath    = Join-Path $snapRoot ("_partial_" + $snapName)
    $finalPath   = Join-Path $snapRoot $snapName
    New-Item -ItemType Directory -Path $snapPath -Force | Out-Null

    $totCopied = 0; $totLinked = 0; $totBytes = 0
    $failures = New-Object System.Collections.Generic.List[string]
    $changedFiles = New-Object System.Collections.Generic.List[object]

    foreach ($label in $Sources.Keys) {
        $src = $Sources[$label]
        if (-not (Test-Path $src)) { continue }
        $srcFull = (Resolve-Path $src).Path
        $files = Get-ChildItem -LiteralPath $srcFull -Recurse -File -ErrorAction SilentlyContinue

        foreach ($f in $files) {
            $rel     = $f.FullName.Substring($srcFull.Length).TrimStart('\')
            $dest    = Join-Path (Join-Path $snapPath $label) $rel
            $destDir = Split-Path $dest -Parent
            if (-not (Test-Path -LiteralPath $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }

            $prevFile = if ($prev) { Join-Path (Join-Path (Join-Path $snapRoot $prev.Name) $label) $rel } else { $null }

            # Unchanged == same size AND same last-write time. Hashing 14 GB every
            # 5 minutes would cost minutes per run for no practical gain here.
            $unchanged = $false
            if ($prevFile -and (Test-Path -LiteralPath $prevFile)) {
                $p = Get-Item -LiteralPath $prevFile
                if ($p.Length -eq $f.Length -and $p.LastWriteTimeUtc -eq $f.LastWriteTimeUtc) { $unchanged = $true }
            }

            try {
                if ($unchanged) {
                    try {
                        New-Item -ItemType HardLink -Path $dest -Target $prevFile -ErrorAction Stop | Out-Null
                        $totLinked++
                    } catch {
                        # NTFS caps a file at 1024 hardlinks. A file that never changes
                        # across that many snapshots hits the ceiling - fall back to a
                        # real copy rather than failing the file outright.
                        Copy-Item -LiteralPath $f.FullName -Destination $dest -Force -ErrorAction Stop
                        $totCopied++; $totBytes += $f.Length
                        $changedFiles.Add([pscustomobject]@{ Label = $label; Rel = $rel })
                    }
                } else {
                    Copy-Item -LiteralPath $f.FullName -Destination $dest -Force -ErrorAction Stop
                    $totCopied++; $totBytes += $f.Length
                    $changedFiles.Add([pscustomobject]@{ Label = $label; Rel = $rel })
                }
            } catch {
                $failures.Add("$label\$rel : $($_.Exception.Message)")
            }
        }
    }

    $elapsed = ((Get-Date) - $started).TotalSeconds
    $stamp   = $started.ToString("HH:mm:ss")

    # A snapshot where nothing changed is noise: it buries the snapshots that DO
    # represent work, and burns one of every unchanged file's 1024 hardlinks. Discard
    # it. The first snapshot is always kept - there is no baseline without it.
    if ($prev -and $totCopied -eq 0 -and $failures.Count -eq 0) {
        Remove-Item -LiteralPath $snapPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host ("[{0}] no changes - nothing to snapshot ({1:N0}s)" -f $stamp, $elapsed) -ForegroundColor DarkGray
        return
    }

    # Complete - promote out of _partial_ so it counts as a real snapshot.
    Rename-Item -LiteralPath $snapPath -NewName $snapName -Force

    # PER-FILE VERSION STORE.
    #
    # Snapshot retention is global: prune 150 snapshots and a file you edited twice last
    # week loses those versions once 150 newer change-events push them out. This keeps a
    # per-file history that is independent of that - every CHANGED file also gets a
    # hardlink into versions\<label>\<relative path>\<timestamp><ext>, and each of those
    # folders is pruned to its own newest $KeepVersionsPerFile.
    #
    # It is a hardlink, so a version costs a directory entry and no data. It also keeps
    # the data alive after the snapshot that introduced it is pruned - deleting a
    # snapshot only removes ITS name for those bytes.
    $verRoot = Join-Path $BackupRoot "versions"
    $versioned = 0
    foreach ($c in $changedFiles) {
        try {
            $vDir = Join-Path (Join-Path $verRoot $c.Label) $c.Rel
            if (-not (Test-Path -LiteralPath $vDir)) { New-Item -ItemType Directory -Path $vDir -Force | Out-Null }
            $ext  = [System.IO.Path]::GetExtension($c.Rel)
            $vPath = Join-Path $vDir ($snapName + $ext)
            if (-not (Test-Path -LiteralPath $vPath)) {
                $liveInSnapshot = Join-Path (Join-Path $finalPath $c.Label) $c.Rel
                New-Item -ItemType HardLink -Path $vPath -Target $liveInSnapshot -ErrorAction Stop | Out-Null
                $versioned++
            }
            # Prune this ONE file's history, newest kept.
            $vAll = Get-ChildItem -LiteralPath $vDir -File | Sort-Object Name -Descending
            if ($vAll.Count -gt $KeepVersionsPerFile) {
                foreach ($old in ($vAll | Select-Object -Skip $KeepVersionsPerFile)) {
                    Remove-Item -LiteralPath $old.FullName -Force -ErrorAction SilentlyContinue
                }
            }
        } catch {
            $failures.Add("version store: $($c.Label)\$($c.Rel) : $($_.Exception.Message)")
        }
    }

    Write-Host ("[{0}] snapshot {1}  ->  {2} changed file(s), {3:N1} MB  ({4} linked, {5:N0}s)" -f `
        $stamp, $snapName, $totCopied, ($totBytes/1MB), $totLinked, $elapsed) -ForegroundColor Green

    if ($failures.Count -gt 0) {
        Write-Host ("          {0} file(s) locked/unreadable - likely open in the editor:" -f $failures.Count) -ForegroundColor Yellow
        $failures | Select-Object -First 5 | ForEach-Object { Write-Host "            $_" -ForegroundColor Yellow }
        if ($failures.Count -gt 5) { Write-Host "            ...and $($failures.Count - 5) more" -ForegroundColor Yellow }
    }

    # Pruning only removes directory entries. Data survives while any newer snapshot
    # still links it, so keeping many snapshots stays cheap.
    $all = Get-ChildItem $snapRoot -Directory | Sort-Object Name -Descending
    if ($all.Count -gt $KeepSnapshots) {
        foreach ($o in ($all | Select-Object -Skip $KeepSnapshots)) {
            try { Remove-Item -LiteralPath $o.FullName -Recurse -Force; Write-Host "          pruned $($o.Name)" -ForegroundColor DarkGray }
            catch { }
        }
    }
}

# --- Run ----------------------------------------------------------------------------
if (-not (Test-Path $BackupRoot)) { New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null }

Write-Host "Bannerlord scene backup" -ForegroundColor Cyan
Write-Host ("=" * 66)
Write-Host "Target : $BackupRoot"

if ($Once) {
    Invoke-Snapshot
    Write-Host "Single run complete." -ForegroundColor Cyan
    return
}

Write-Host ("Mode   : WATCH - snapshot every {0} minute(s) while this window is open" -f $IntervalMinutes)
Write-Host "Stop   : close this window, or press Ctrl+C"
Write-Host ("=" * 66)
Write-Host ""

# Ctrl+C becomes a READABLE KEY rather than a kill signal. Without this, Ctrl+C
# terminates PowerShell wherever it happens to be - including halfway through writing a
# snapshot, which is exactly how the partial snapshot got created during testing. Now
# nothing can interrupt a snapshot in progress: a stop request is noticed only between
# runs, and has to be confirmed by typing.
[Console]::TreatControlCAsInput = $true

$runs = 0
$stopRequested = $false

while (-not $stopRequested) {
    try { Invoke-Snapshot; $runs++ }
    catch { Write-Host ("[{0}] snapshot FAILED: {1}" -f (Get-Date).ToString("HH:mm:ss"), $_.Exception.Message) -ForegroundColor Red }

    $next = (Get-Date).AddMinutes($IntervalMinutes)
    Write-Host ("          next at {0}  (runs this session: {1})   press any key to stop" -f $next.ToString("HH:mm:ss"), $runs) -ForegroundColor DarkGray

    # Interruptible wait: poll for a keypress instead of one long Start-Sleep, so a stop
    # request is picked up promptly WITHOUT ever landing mid-snapshot.
    $deadline = (Get-Date).AddMinutes($IntervalMinutes)
    while ((Get-Date) -lt $deadline) {
        if ([Console]::KeyAvailable) {
            [void][Console]::ReadKey($true)   # swallow the key that woke us
            Write-Host ""
            Write-Host "Stop requested. The current snapshot is already finished - nothing is mid-write." -ForegroundColor Yellow
            $answer = Read-Host "Type CLOSE to stop backing up, or press Enter to keep going"
            if ($answer.Trim().ToUpper() -eq "CLOSE") { $stopRequested = $true; break }
            Write-Host "Continuing." -ForegroundColor Green
            Write-Host ("          next at {0}" -f $deadline.ToString("HH:mm:ss")) -ForegroundColor DarkGray
        }
        Start-Sleep -Milliseconds 250
    }
}

Write-Host ""
Write-Host ("Backup watcher stopped cleanly after {0} snapshot run(s). Nothing is running in the background." -f $runs) -ForegroundColor Cyan
