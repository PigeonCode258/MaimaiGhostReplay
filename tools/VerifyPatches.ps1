<#
=============================================================================
 VerifyPatches.ps1 -- static pre-flight check for the mod's Harmony patches

 Reads the BUILT mod assembly with Mono.Cecil, pulls every [HarmonyPatch]
 target out of its metadata, and verifies that each target type/method really
 exists in the game's Assembly-CSharp.dll. Catches typos that would otherwise
 only show up as a runtime "patch failed" log line.

 ASCII-only on purpose (see build.ps1 header for why).

 NOTE: this script memory-maps the mod DLL through Mono.Cecil. Do NOT run it in
       the same PowerShell process as a build that rewrites that DLL - csc will
       fail with CS0016 "file is in use". Run the build as a separate invocation.

 Usage:
   .\VerifyPatches.ps1
   .\VerifyPatches.ps1 -ModDll ..\build\MaimaiGhostReplay.dll
=============================================================================
#>
[CmdletBinding()]
param(
    [string]$ModDll   = (Join-Path $PSScriptRoot '..\build\MaimaiGhostReplay.dll'),
    [string]$GameRoot = (Join-Path $PSScriptRoot '..\..\SDEZ170\Package')
)

$ErrorActionPreference = 'Stop'

$GameRoot = (Resolve-Path $GameRoot).Path
$ModDll   = (Resolve-Path $ModDll).Path
$gameAsm  = Join-Path $GameRoot 'Sinmai_Data\Managed\Assembly-CSharp.dll'
$cecil    = Join-Path $GameRoot 'MelonLoader\net35\Mono.Cecil.dll'

Add-Type -Path $cecil

$mod  = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($ModDll)
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($gameAsm)

Write-Host "mod : $($mod.Name.FullName)"
Write-Host "game: $($game.Name.FullName)"
Write-Host ""

# ---- index the game assembly -----------------------------------------------
$gameTypes = @{}
foreach ($t in $game.MainModule.Types) { $gameTypes[$t.FullName] = $t }

function Find-GameType([string]$name) {
    if ($gameTypes.ContainsKey($name)) { return $gameTypes[$name] }
    # fall back to a suffix match for nested / namespaced names
    foreach ($k in $gameTypes.Keys) {
        if ($k -eq $name -or $k.EndsWith(".$name")) { return $gameTypes[$k] }
    }
    return $null
}

# ---- walk every type in the mod, incl. nested -------------------------------
$allModTypes = New-Object System.Collections.ArrayList
$queue = New-Object System.Collections.Queue
foreach ($t in $mod.MainModule.Types) { $queue.Enqueue($t) }
while ($queue.Count -gt 0) {
    $t = $queue.Dequeue()
    [void]$allModTypes.Add($t)
    if ($t.HasNestedTypes) {
        foreach ($n in $t.NestedTypes) { $queue.Enqueue($n) }
    }
}
Write-Host ("walked {0} mod types" -f $allModTypes.Count)

$ok = 0; $bad = 0; $checked = 0

foreach ($t in $allModTypes) {
    foreach ($ca in $t.CustomAttributes) {
        if ($ca.AttributeType.Name -notmatch 'HarmonyPatch') { continue }
        if ($ca.ConstructorArguments.Count -lt 2) { continue }

        $targetRef = $ca.ConstructorArguments[0].Value
        $methodName = $ca.ConstructorArguments[1].Value
        if ($targetRef -eq $null -or -not $methodName) { continue }

        $checked++
        $targetName = $targetRef.FullName
        $gt = Find-GameType $targetName

        if ($gt -eq $null) {
            Write-Host ("[MISS TYPE ] {0}  ->  target type '{1}' not found" -f $t.Name, $targetName) -ForegroundColor Red
            $bad++
            continue
        }

        $hits = @($gt.Methods | Where-Object { $_.Name -eq $methodName })
        if ($hits.Count -eq 0) {
            Write-Host ("[MISS METH ] {0}  ->  {1}.{2}() not found" -f $t.Name, $gt.FullName, $methodName) -ForegroundColor Red
            $bad++
            continue
        }

        $sig = ($hits | ForEach-Object {
            $p = ($_.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
            "$($_.ReturnType.Name) $methodName($p)"
        }) -join '  |  '

        $overloadWarn = ''
        if ($hits.Count -gt 1) { $overloadWarn = '   <-- WARNING: overloaded, Harmony needs explicit arg types' }

        Write-Host ("[OK        ] {0,-38} -> {1}.{2}{3}" -f $t.Name, $gt.FullName, $sig, $overloadWarn)
        $ok++
    }
}

Write-Host ""
Write-Host ("checked={0}  ok={1}  bad={2}" -f $checked, $ok, $bad)

# ---- runtime reflection lookups ---------------------------------------------
# ONLY the members the mod still reaches by name at runtime.
# Everything else goes through the game's own interfaces/types and is therefore
# already checked by the C# compiler.
$memberChecks = @(
    @{ T = 'Process.SubSequence.SequenceBase';  M = 'f:ProcessProcessing';      W = 'gesture: entry point' },
    @{ T = 'Process.SubSequence.SequenceBase';  M = 'f:PlayerIndex';            W = 'gesture: player' },
    @{ T = 'Process.MusicSelectProcess';        M = 'f:container';              W = 'notice: ProcessManager' },
    @{ T = 'Process.MusicSelectProcess';        M = 'p:MonitorArray';           W = 'bug2: refresh cursor' }
    @{ T = 'Process.MusicSelectProcess';        M = 'm:SetSortIndexToMaiList(Int32,Int32,Boolean,Boolean)'; W = 'bug2: locate song' }
    @{ T = 'Process.MusicSelectProcess';        M = 'm:SetSortIndexToExtraGenre(Int32,String)'; W = 'utage: locate song' }
    @{ T = 'Monitor.MusicSelectMonitor';        M = 'm:SetDeployList(Boolean,Boolean)'; W = 'bug2: push cursor to UI' }
    @{ T = 'ProcessDataContainer';              M = 'a:processManager';         W = 'notice: ProcessManager' },
    # field handles used by Recorder / ReplayInjector
    @{ T = 'Monitor.NoteBase';                  M = 'f:NoteIndex';              W = 'inject/record' },
    @{ T = 'Monitor.NoteBase';                  M = 'f:JudgeResult';            W = 'inject' },
    @{ T = 'Monitor.NoteBase';                  M = 'f:JudgeTimingDiffMsec';    W = 'inject' },
    @{ T = 'Monitor.SlideRoot';                 M = 'f:NoteIndex';              W = 'inject/record' },
    @{ T = 'Monitor.SlideRoot';                 M = 'f:JudgeResult';            W = 'inject' },
    @{ T = 'Monitor.SlideRoot';                 M = 'f:JudgeTimingDiffMsec';    W = 'inject' },
    @{ T = 'Monitor.HoldNote';                  M = 'f:HeadJudged';             W = 'inject' },
    @{ T = 'Monitor.HoldNote';                  M = 'f:JudgeHeadResult';        W = 'record' },
    @{ T = 'Monitor.BreakHoldNote';             M = 'f:HeadJudged';             W = 'inject' },
    @{ T = 'Monitor.BreakHoldNote';             M = 'f:JudgeHeadResult';        W = 'record' },
    @{ T = 'Monitor.TouchHoldC';                M = 'f:HeadJudged';             W = 'inject' },
    @{ T = 'Monitor.TouchHoldC';                M = 'f:JudgeHeadResult';        W = 'record' }
    # v0.3.0 judgement sound: call the note's own PlayJudgeSe/PlayJudgeHeadSe after injecting
    @{ T = 'Monitor.NoteBase';                  M = 'm:PlayJudgeSe()';          W = 'sound: tap/star/break/touch' }
    @{ T = 'Monitor.HoldNote';                  M = 'm:PlayJudgeSe()';          W = 'sound: hold tail' }
    @{ T = 'Monitor.HoldNote';                  M = 'm:PlayJudgeHeadSe()';      W = 'sound: hold head' }
    @{ T = 'Monitor.BreakHoldNote';             M = 'm:PlayJudgeSe()';          W = 'sound: breakhold tail' }
    @{ T = 'Monitor.BreakHoldNote';             M = 'm:PlayJudgeHeadSe()';      W = 'sound: breakhold head' }
    @{ T = 'Monitor.TouchHoldC';                M = 'm:PlayJudgeHeadSe()';      W = 'sound: touchhold head' }
    @{ T = 'Monitor.SlideRoot';                 M = 'm:PlayJudgeSe()';          W = 'sound: slide (already fires)' }
    # v0.6.0 hold body visual
    @{ T = 'Monitor.NoteBase';                  M = 'f:JudgeEffectObject';      W = 'holdbody: TouchEffect' }
    @{ T = 'Monitor.HoldNote';                  M = 'm:HoldOn(Boolean)';        W = 'holdbody: sprite swap' }
    @{ T = 'Monitor.BreakHoldNote';             M = 'm:HoldOn(Boolean)';        W = 'holdbody: sprite swap' }
    @{ T = 'Monitor.TouchHoldC';                M = 'm:HoldOn(Boolean)';        W = 'holdbody: sprite swap' }
    @{ T = 'Monitor.TouchEffect';               M = 'm:InitializeHold(ETiming)';W = 'holdbody: start effect' }
    @{ T = 'Monitor.TouchEffect';               M = 'm:StopHoldPlay()';         W = 'holdbody: stop effect' })

Write-Host "`n--- runtime reflection lookups ---"
$mOk = 0; $mBad = 0
foreach ($c in $memberChecks) {
    $gt = Find-GameType $c.T
    if ($gt -eq $null) {
        Write-Host ("[MISS TYPE ] {0}  ({1})" -f $c.T, $c.W) -ForegroundColor Red
        $mBad++; continue
    }
    $spec = $c.M
    $kind = $spec.Substring(0, 1)
    $name = $spec.Substring(2)
    $found = $false

    if ($kind -eq 'f' -or $kind -eq 'a') {
        $t = $gt
        while ($t -ne $null -and -not $found) {
            if (@($t.Fields | Where-Object { $_.Name -eq $name }).Count -gt 0) { $found = $true }
            $t = $t.BaseType
        }
    }
    if (-not $found -and ($kind -eq 'p' -or $kind -eq 'a')) {
        $t = $gt
        while ($t -ne $null -and -not $found) {
            if (@($t.Properties | Where-Object { $_.Name -eq $name }).Count -gt 0) { $found = $true }
            $t = $t.BaseType
        }
    }
    if ($kind -eq 'm') {
        $openParen = $name.IndexOf('(')
        $mName = $name.Substring(0, $openParen)
        $argList = $name.Substring($openParen + 1).TrimEnd(')')
        $want = @()
        if ($argList.Length -gt 0) { $want = $argList.Split(',') }
        # walk base types: the runtime lookup (Reflect.FindMethod) does the same,
        # so e.g. BreakNote has no PlayJudgeSe of its own but inherits NoteBase's.
        $bt = $gt
        while ($bt -ne $null -and -not $found) {
            foreach ($mm in $bt.Methods) {
                if ($mm.Name -ne $mName) { continue }
                if ($mm.Parameters.Count -ne $want.Count) { continue }
                $match = $true
                for ($i = 0; $i -lt $want.Count; $i++) {
                    if ($mm.Parameters[$i].ParameterType.Name -ne $want[$i]) { $match = $false; break }
                }
                if ($match) { $found = $true; break }
            }
            $bt = $bt.BaseType
        }
    }

    if ($found) {
        Write-Host ("[OK        ] {0}.{1}   ({2})" -f $gt.FullName, $spec, $c.W)
        $mOk++
    } else {
        Write-Host ("[MISSING   ] {0}.{1}   ({2})" -f $gt.FullName, $spec, $c.W) -ForegroundColor Red
        $mBad++
    }
}
Write-Host ("`nreflection lookups: ok={0}  bad={1}" -f $mOk, $mBad)

# ---- assembly references ----------------------------------------------------
Write-Host "`n--- mod references ---"
foreach ($r in $mod.MainModule.AssemblyReferences) { Write-Host "  $($r.FullName)" }

if ($bad -gt 0 -or $mBad -gt 0) { exit 1 }
exit 0
