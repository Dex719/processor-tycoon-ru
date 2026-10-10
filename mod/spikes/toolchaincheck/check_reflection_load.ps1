<#
.SYNOPSIS
  Spike 0003 (TSK-003): checks that a built mod DLL opens with Assembly.ReflectionOnlyLoadFrom.

.DESCRIPTION
  Loads the DLL in the reflection-only context (nothing is executed), lists its assembly references, resolves every
  reference from the game folders, enumerates all types, and fails if a required reference is missing
  (by default Assembly-CSharp: the DLL must link against the game code).
  The game folder is only read. Needs Windows PowerShell 5.1 (.NET Framework): ReflectionOnlyLoadFrom does not exist
  on .NET Core / PowerShell 7, so the script restarts itself in powershell.exe there.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File mod\spikes\toolchaincheck\check_reflection_load.ps1 `
      -Dll mod\build\spikes\ModernSyntax.dll -GameDir "C:\Games\Processor Tycoon"

  Exit code: 0 - the DLL loads and every required reference is present, 1 - the check failed, 2 - bad arguments.
#>
[CmdletBinding()]
param(
    # DLL to check (for example mod\build\spikes\ModernSyntax.dll or mod\build\PTMod.dll).
    [Parameter(Mandatory = $true)] [string] $Dll,
    # Game folder (where "Processor Tycoon.exe" is); default is the PT_GAME_DIR environment variable.
    [string] $GameDir = $env:PT_GAME_DIR,
    # Assembly names that must appear among the DLL references.
    [string[]] $Require = @('Assembly-CSharp'),
    # Extra folders searched for references (the folder of the DLL and mod\lib are always searched).
    [string[]] $ExtraDir = @()
)

$ErrorActionPreference = 'Stop'

function Fail([string] $text, [int] $code = 1) {
    Write-Host ("FAIL: " + $text) -ForegroundColor Red
    exit $code
}

# --- PowerShell 7 / .NET Core: restart in Windows PowerShell 5.1 ------------------------------------------------
if ($PSVersionTable.PSEdition -eq 'Core') {
    $legacy = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path $legacy)) { Fail 'ReflectionOnlyLoadFrom needs Windows PowerShell 5.1 (powershell.exe); not found.' 2 }
    $forward = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-Dll', $Dll)
    if ($GameDir) { $forward += @('-GameDir', $GameDir) }
    foreach ($name in $Require) { $forward += @('-Require', $name) }
    foreach ($dir in $ExtraDir) { $forward += @('-ExtraDir', $dir) }
    & $legacy @forward
    exit $LASTEXITCODE
}

# --- Arguments --------------------------------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath $Dll -PathType Leaf)) { Fail ("DLL not found: " + $Dll) 2 }
$dllPath = (Resolve-Path -LiteralPath $Dll).Path

$searchDirs = New-Object System.Collections.Generic.List[string]
if ($GameDir) {
    $root = $GameDir.Trim('"')
    $managed = Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like '*_Data' } |
        ForEach-Object { Join-Path $_.FullName 'Managed' } |
        Where-Object { Test-Path (Join-Path $_ 'mscorlib.dll') } | Select-Object -First 1
    if (-not $managed) { Fail ("No Processor Tycoon_Data\Managed\mscorlib.dll under: " + $root) 2 }
    # The English original comes first: the mod is compiled against it, the folder Managed holds the patched copy.
    $original = Join-Path $root '_original_en'
    if (Test-Path -LiteralPath $original) { $searchDirs.Add($original) }
    $searchDirs.Add($managed)
} else {
    Write-Host 'WARN: no -GameDir / PT_GAME_DIR: references to game assemblies cannot be resolved.' -ForegroundColor Yellow
}
$searchDirs.Add((Split-Path -Parent $dllPath))
$libDir = Join-Path $PSScriptRoot '..\..\lib'
if (Test-Path -LiteralPath $libDir) { $searchDirs.Add((Resolve-Path -LiteralPath $libDir).Path) }
foreach ($dir in $ExtraDir) { $searchDirs.Add($dir) }

# --- The check ------------------------------------------------------------------------------------------------
# The reflection-only context does not load dependencies by itself, and a ReflectionOnlyAssemblyResolve handler is not
# reliably called from a PowerShell script block, so the references are loaded explicitly before the types are read.
function Find-Assembly([string] $name) {
    foreach ($dir in $searchDirs) {
        $candidate = Join-Path $dir ($name + '.dll')
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
}

try {
    $asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($dllPath)
} catch {
    Fail ("ReflectionOnlyLoadFrom failed: " + $_.Exception.Message)
}
Write-Host ("Loaded      : " + $asm.FullName)
Write-Host ("Runtime     : " + $asm.ImageRuntimeVersion + " (metadata version)")

$direct = $asm.GetReferencedAssemblies()
Write-Host ("References  : " + $direct.Count)
$loaded = @{}                      # assembly name -> file it was loaded from ('' for mscorlib, null if not found)
$queue = New-Object System.Collections.Generic.Queue[System.Reflection.AssemblyName]
foreach ($ref in $direct) { $queue.Enqueue($ref) }
while ($queue.Count -gt 0) {
    $ref = $queue.Dequeue()
    if ($loaded.ContainsKey($ref.Name)) { continue }
    if ($ref.Name -eq 'mscorlib') {
        # The runtime maps mscorlib itself (the framework one); the game's copy is not loaded as a second mscorlib.
        $loaded[$ref.Name] = ''
        continue
    }
    $path = Find-Assembly $ref.Name
    if (-not $path) { $loaded[$ref.Name] = $null; continue }
    $dep = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($path)
    $loaded[$ref.Name] = $path
    foreach ($next in $dep.GetReferencedAssemblies()) { $queue.Enqueue($next) }
}

$unresolved = @()
foreach ($ref in $direct) {
    $path = $loaded[$ref.Name]
    $state = if ($ref.Name -eq 'mscorlib') { 'ok (runtime)' } elseif ($path) { 'ok' } else { 'NOT FOUND'; $unresolved += $ref.Name }
    $where = if ($path) { '  <- ' + $path } else { '' }
    Write-Host ("  {0,-34} {1,-10} {2}{3}" -f $ref.Name, $ref.Version, $state, $where)
}
$transitiveMissing = @($loaded.Keys | Where-Object { $null -eq $loaded[$_] -and ($direct.Name -notcontains $_) })
Write-Host ("Dependencies: " + $loaded.Count + " assemblies in the closure, " + $transitiveMissing.Count + " not found (transitive, informational)")

$missing = @($Require | Where-Object { $direct.Name -notcontains $_ })
if ($missing.Count -gt 0) { Fail ("Required reference(s) not found in the DLL: " + ($missing -join ', ')) }

$typeCount = 0
try {
    $typeCount = $asm.GetTypes().Count
} catch [System.Reflection.ReflectionTypeLoadException] {
    $typeCount = @($_.Exception.Types | Where-Object { $_ }).Count
    $problems = @($_.Exception.LoaderExceptions | Select-Object -ExpandProperty Message -Unique)
    # Mixing the game's System*.dll (Mono class library) with the .NET Framework mscorlib of this process can make a
    # type that derives from a System.Core / System type fail with a security-transparency complaint. That is a
    # limitation of the check, not of the DLL (Unity's Mono has no such rule), so it is reported but not fatal.
    $benign = @($problems | Where-Object { $_ -notmatch 'security (transparent|critical)' }).Count -eq 0
    if (-not $benign) { Fail ("GetTypes failed, " + $problems.Count + " loader problem(s): " + ($problems -join ' | ')) }
    Write-Host ("WARN: " + $problems.Count + " type(s) not loadable in the .NET Framework reflection context (security transparency; not a DLL defect): " + ($problems -join ' | ')) -ForegroundColor Yellow
}
Write-Host ("Types       : " + $typeCount + " loadable")

if ($unresolved.Count -gt 0) { Fail ("Unresolved reference(s): " + ($unresolved -join ', ')) }
Write-Host ("OK: " + (Split-Path -Leaf $dllPath) + " opens with ReflectionOnlyLoadFrom; required references present: " + ($Require -join ', ')) -ForegroundColor Green
exit 0
