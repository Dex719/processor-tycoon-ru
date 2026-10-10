<#
.SYNOPSIS
  IL probe (TSK-031): dumps the IL of game methods from Assembly-CSharp.dll without running the game, and checks the code patterns
  the core's transpilers P10 and P11 rely on.

.DESCRIPTION
  Loads the assembly with Assembly.ReflectionOnlyLoadFrom (nothing is executed), preloads its references from the game folder, and for every
  method named with -Method prints the locals, the exception clauses and the IL with resolved operands, then a summary: how many times each
  local is loaded and stored, and every call target with its count.

  -CheckCredit runs the checks of the transpilers instead of (or after) the dump and exits with 1 when a pattern does not match:
    P10  BankruptcyHandler.TickBankruptcies: local 0 is float, local 2 is ICompany; local 0 is stored once, right after
         "call CentralBank::get_DebtLimit; neg"; it is loaded exactly 3 times, all after that store (inside the loop).
    P11  MoneyBalance.UpdateUI, Money.UpdateMoneyText, BankruptcyHandler.UpdateUI: exactly one call of CentralBank::get_DebtLimit each.
  These are the same rules BankruptcyThresholdTranspiler and CreditDisplayTranspiler apply in the game; the result is recorded in their
  comments. Run it again after a game update.

  The game folder is only read. Needs Windows PowerShell 5.1 (.NET Framework): ReflectionOnlyLoadFrom does not exist on .NET Core /
  PowerShell 7, so the script restarts itself in powershell.exe there.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File mod\spikes\ilprobe\dump_method_il.ps1 `
      -GameDir "C:\Games\Processor Tycoon" -Method ProcessorTycoon.Bank.BankruptcyHandler::TickBankruptcies

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File mod\spikes\ilprobe\dump_method_il.ps1 -GameDir "C:\Games\Processor Tycoon" -CheckCredit

  Exit code: 0 - dumped (and with -CheckCredit every pattern matches), 1 - a method is missing or a pattern does not match, 2 - bad arguments.
#>
[CmdletBinding()]
param(
    # Game folder (where "Processor Tycoon.exe" is); default is the PT_GAME_DIR environment variable.
    [string] $GameDir = $env:PT_GAME_DIR,
    # Assembly to read; default <GameDir>\_original_en\Assembly-CSharp.dll (the English original the mod is compiled against).
    [string] $Assembly,
    # Methods: "Namespace.Type::Method", "Type::Method" or "Type.Method" (short type name); nested types with "+".
    [string[]] $Method = @(),
    # Only the summary of each method, no instruction listing.
    [switch] $SummaryOnly,
    # Check the patterns of the transpilers P10 and P11.
    [switch] $CheckCredit
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
    $forward = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($GameDir) { $forward += @('-GameDir', $GameDir) }
    if ($Assembly) { $forward += @('-Assembly', $Assembly) }
    foreach ($name in $Method) { $forward += @('-Method', $name) }
    if ($SummaryOnly) { $forward += '-SummaryOnly' }
    if ($CheckCredit) { $forward += '-CheckCredit' }
    & $legacy @forward
    exit $LASTEXITCODE
}

# --- Arguments --------------------------------------------------------------------------------------------------
# powershell -File passes "-Method A,B" as one string: split it here.
$Method = @($Method | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Method.Count -eq 0 -and -not $CheckCredit) { Fail 'Name at least one -Method, or pass -CheckCredit.' 2 }

$searchDirs = New-Object System.Collections.Generic.List[string]
if ($GameDir) {
    $root = $GameDir.Trim('"')
    $managed = Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like '*_Data' } |
        ForEach-Object { Join-Path $_.FullName 'Managed' } |
        Where-Object { Test-Path (Join-Path $_ 'mscorlib.dll') } | Select-Object -First 1
    if (-not $managed) { Fail ("No Processor Tycoon_Data\Managed\mscorlib.dll under: " + $root) 2 }
    if (-not $Assembly) { $Assembly = Join-Path $root '_original_en\Assembly-CSharp.dll' }
    $searchDirs.Add($managed)
}
if (-not $Assembly) { Fail 'Pass -GameDir (or PT_GAME_DIR) or -Assembly.' 2 }
if (-not (Test-Path -LiteralPath $Assembly -PathType Leaf)) { Fail ("Assembly not found: " + $Assembly) 2 }
$asmPath = (Resolve-Path -LiteralPath $Assembly).Path
$searchDirs.Insert(0, (Split-Path -Parent $asmPath))

# --- Loading (reflection only) ----------------------------------------------------------------------------------
# The reflection-only context does not load dependencies by itself, and a ReflectionOnlyAssemblyResolve handler is not reliably called
# from a PowerShell script block, so the closure of references is loaded explicitly before any operand is resolved.
function Find-Assembly([string] $name) {
    foreach ($dir in $searchDirs) {
        $candidate = Join-Path $dir ($name + '.dll')
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
}

try {
    $asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($asmPath)
} catch {
    Fail ("ReflectionOnlyLoadFrom failed: " + $_.Exception.Message)
}
Write-Host ("Assembly : " + $asm.FullName)
Write-Host ("File     : " + $asmPath)

$loaded = @{}
$queue = New-Object System.Collections.Generic.Queue[System.Reflection.AssemblyName]
foreach ($ref in $asm.GetReferencedAssemblies()) { $queue.Enqueue($ref) }
while ($queue.Count -gt 0) {
    $ref = $queue.Dequeue()
    if ($loaded.ContainsKey($ref.Name)) { continue }
    $loaded[$ref.Name] = $true
    if ($ref.Name -eq 'mscorlib') { continue }   # the runtime's own
    $path = Find-Assembly $ref.Name
    if (-not $path) { continue }
    try {
        $dep = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($path)
        foreach ($next in $dep.GetReferencedAssemblies()) { $queue.Enqueue($next) }
    } catch {
        Write-Host ("WARN: cannot preload " + $path + ": " + $_.Exception.Message) -ForegroundColor Yellow
    }
}

# --- Types and methods ------------------------------------------------------------------------------------------
$allTypes = $null
try {
    $allTypes = $asm.GetTypes()
} catch [System.Reflection.ReflectionTypeLoadException] {
    $allTypes = @($_.Exception.Types | Where-Object { $_ })
}

$anyMember = [System.Reflection.BindingFlags]'Public, NonPublic, Instance, Static, DeclaredOnly'

function Find-Methods([string] $spec) {
    $typeName = $null; $methodName = $null
    $sep = $spec.IndexOf('::')
    if ($sep -ge 0) {
        $typeName = $spec.Substring(0, $sep); $methodName = $spec.Substring($sep + 2)
    } else {
        $dot = $spec.LastIndexOf('.')
        if ($dot -le 0) { return @() }
        $typeName = $spec.Substring(0, $dot); $methodName = $spec.Substring($dot + 1)
    }
    $types = @($allTypes | Where-Object { $_.FullName -eq $typeName })
    if ($types.Count -eq 0) { $types = @($allTypes | Where-Object { $_.Name -eq $typeName -or $_.FullName -like ('*.' + $typeName) -or $_.FullName -like ('*+' + $typeName) }) }
    $found = @()
    foreach ($type in $types) {
        foreach ($m in $type.GetMethods($anyMember)) { if ($m.Name -eq $methodName) { $found += $m } }
        foreach ($c in $type.GetConstructors($anyMember)) { if ($c.Name -eq $methodName) { $found += $c } }
    }
    return $found
}

# --- IL decoding ------------------------------------------------------------------------------------------------
$oneByte = @{}
$twoByte = @{}
foreach ($field in [System.Reflection.Emit.OpCodes].GetFields([System.Reflection.BindingFlags]'Public, Static')) {
    $op = [System.Reflection.Emit.OpCode] $field.GetValue($null)
    $value = ([int] $op.Value) -band 0xFFFF   # OpCode.Value is an Int16: the two-byte opcodes (0xFE..) are negative
    if ($op.Size -eq 1) { $oneByte[$value] = $op } else { $twoByte[$value -band 0xFF] = $op }
}

function Get-OperandSize([System.Reflection.Emit.OpCode] $op) {
    switch ($op.OperandType.ToString()) {
        'InlineNone' { return 0 }
        'ShortInlineBrTarget' { return 1 }
        'ShortInlineI' { return 1 }
        'ShortInlineVar' { return 1 }
        'InlineVar' { return 2 }
        'InlineI8' { return 8 }
        'InlineR' { return 8 }
        default { return 4 }
    }
}

function Describe-Member($member) {
    if ($null -eq $member) { return '?' }
    $owner = if ($member.DeclaringType) { $member.DeclaringType.FullName } else { '' }
    if ($member -is [System.Reflection.MethodBase]) {
        $params = @()
        try { foreach ($p in $member.GetParameters()) { $params += $p.ParameterType.Name } } catch { $params = @('?') }
        $ret = ''
        if ($member -is [System.Reflection.MethodInfo]) { try { $ret = $member.ReturnType.Name + ' ' } catch { $ret = '? ' } }
        return ($ret + $owner + '::' + $member.Name + '(' + ($params -join ', ') + ')')
    }
    if ($member -is [System.Reflection.FieldInfo]) {
        $ft = '?'
        try { $ft = $member.FieldType.Name } catch {}
        return ($ft + ' ' + $owner + '::' + $member.Name)
    }
    if ($member -is [System.Type]) { return $member.FullName }
    return $member.ToString()
}

# One decoded instruction: offset, opcode, the raw operand (int / long / double / branch target / local index) and its text.
function Decode-Body([System.Reflection.MethodBase] $m) {
    $body = $m.GetMethodBody()
    if ($null -eq $body) { return $null }
    $il = $body.GetILAsByteArray()
    $module = $m.Module
    $typeArgs = $null; $methodArgs = $null
    if ($m.DeclaringType -and $m.DeclaringType.IsGenericType) { $typeArgs = $m.DeclaringType.GetGenericArguments() }
    if ($m.IsGenericMethod) { $methodArgs = $m.GetGenericArguments() }

    $list = New-Object System.Collections.Generic.List[object]
    $i = 0
    while ($i -lt $il.Length) {
        $start = $i
        $code = [int] $il[$i]; $i++
        if ($code -eq 0xFE) { $op = $twoByte[[int] $il[$i]]; $i++ } else { $op = $oneByte[$code] }
        if ($null -eq $op) { throw ("unknown opcode 0x{0:X2} at IL_{1:X4}" -f $code, $start) }

        $operand = $null; $text = ''; $member = $null
        $kind = $op.OperandType.ToString()
        if ($kind -eq 'InlineSwitch') {
            $n = [BitConverter]::ToInt32($il, $i); $i += 4
            $base = $i + 4 * $n
            $targets = @()
            for ($k = 0; $k -lt $n; $k++) { $targets += ('IL_{0:X4}' -f ($base + [BitConverter]::ToInt32($il, $i + 4 * $k))) }
            $i = $base
            $text = '(' + ($targets -join ', ') + ')'
        } else {
            $size = Get-OperandSize $op
            switch ($kind) {
                'ShortInlineBrTarget' { $operand = $i + 1 + [int] [sbyte] $il[$i]; $text = 'IL_{0:X4}' -f $operand }
                'InlineBrTarget' { $operand = $i + 4 + [BitConverter]::ToInt32($il, $i); $text = 'IL_{0:X4}' -f $operand }
                'ShortInlineI' { $operand = if ($op.Name -eq 'ldc.i4.s') { [int] [sbyte] $il[$i] } else { [int] $il[$i] }; $text = [string] $operand }
                'ShortInlineVar' { $operand = [int] $il[$i]; $text = [string] $operand }
                'InlineVar' { $operand = [int] [BitConverter]::ToUInt16($il, $i); $text = [string] $operand }
                'InlineI' { $operand = [BitConverter]::ToInt32($il, $i); $text = [string] $operand }
                'InlineI8' { $operand = [BitConverter]::ToInt64($il, $i); $text = [string] $operand }
                'ShortInlineR' { $operand = [BitConverter]::ToSingle($il, $i); $text = $operand.ToString('R', [Globalization.CultureInfo]::InvariantCulture) }
                'InlineR' { $operand = [BitConverter]::ToDouble($il, $i); $text = $operand.ToString('R', [Globalization.CultureInfo]::InvariantCulture) }
                'InlineString' {
                    $token = [BitConverter]::ToInt32($il, $i)
                    try { $text = '"' + $module.ResolveString($token) + '"' } catch { $text = 'string 0x{0:X8}' -f $token }
                }
                'InlineSig' { $text = 'sig 0x{0:X8}' -f [BitConverter]::ToInt32($il, $i) }
                'InlineNone' { }
                default {
                    $token = [BitConverter]::ToInt32($il, $i)
                    try { $member = $module.ResolveMember($token, $typeArgs, $methodArgs); $text = Describe-Member $member }
                    catch { $text = 'token 0x{0:X8} (unresolved: {1})' -f $token, $_.Exception.InnerException.Message }
                }
            }
            $i += $size
        }
        $list.Add([pscustomobject]@{ Offset = $start; OpCode = $op; Name = $op.Name; Operand = $operand; Text = $text; Member = $member })
    }
    return $list
}

# The local index an instruction loads (ldloc / ldloca), or -1.
function LoadedLocal($ins) {
    switch -regex ($ins.Name) {
        '^ldloc\.([0-3])$' { return [int] $Matches[1] }
        '^ldloca?(\.s)?$' { return [int] $ins.Operand }
        default { return -1 }
    }
}

# The local index an instruction stores (stloc), or -1.
function StoredLocal($ins) {
    switch -regex ($ins.Name) {
        '^stloc\.([0-3])$' { return [int] $Matches[1] }
        '^stloc(\.s)?$' { return [int] $ins.Operand }
        default { return -1 }
    }
}

function IsCall($ins) { return $ins.Name -eq 'call' -or $ins.Name -eq 'callvirt' }

function Show-Method([System.Reflection.MethodBase] $m) {
    Write-Host ''
    Write-Host ('== ' + (Describe-Member $m)) -ForegroundColor Cyan
    $body = $m.GetMethodBody()
    if ($null -eq $body) { Write-Host '   (no body)'; return }
    Write-Host ('   IL size {0} bytes, max stack {1}, init locals {2}' -f $body.GetILAsByteArray().Length, $body.MaxStackSize, $body.InitLocals)
    foreach ($local in $body.LocalVariables) {
        $lt = '?'
        try { $lt = $local.LocalType.FullName } catch {}
        Write-Host ('   local V_{0}: {1}' -f $local.LocalIndex, $lt)
    }
    foreach ($clause in $body.ExceptionHandlingClauses) {
        Write-Host ('   {0}: try IL_{1:X4}..IL_{2:X4}, handler IL_{3:X4}..IL_{4:X4}' -f $clause.Flags, $clause.TryOffset, ($clause.TryOffset + $clause.TryLength),
            $clause.HandlerOffset, ($clause.HandlerOffset + $clause.HandlerLength))
    }

    $code = Decode-Body $m
    if (-not $SummaryOnly) {
        foreach ($ins in $code) { Write-Host ('   IL_{0:X4}: {1,-12} {2}' -f $ins.Offset, $ins.Name, $ins.Text) }
    }

    $loads = @{}; $stores = @{}; $calls = [ordered]@{}
    foreach ($ins in $code) {
        $l = LoadedLocal $ins; if ($l -ge 0) { $loads[$l] = 1 + [int] $loads[$l] }
        $s = StoredLocal $ins; if ($s -ge 0) { $stores[$s] = 1 + [int] $stores[$s] }
        if (IsCall $ins) { $calls[$ins.Text] = 1 + [int] $calls[$ins.Text] }
    }
    Write-Host '   -- summary'
    foreach ($local in $body.LocalVariables) {
        Write-Host ('   V_{0}: loaded {1}x, stored {2}x' -f $local.LocalIndex, [int] $loads[$local.LocalIndex], [int] $stores[$local.LocalIndex])
    }
    foreach ($key in $calls.Keys) { Write-Host ('   call {0}x  {1}' -f $calls[$key], $key) }
}

# --- Dump -------------------------------------------------------------------------------------------------------
$failed = $false
foreach ($spec in $Method) {
    $methods = @(Find-Methods $spec)
    if ($methods.Count -eq 0) {
        Write-Host ('FAIL: method not found: ' + $spec) -ForegroundColor Red
        $failed = $true
        continue
    }
    foreach ($m in $methods) { Show-Method $m }
}

# --- Credit patterns (P10, P11) ---------------------------------------------------------------------------------
function Get-Single([string] $spec) {
    $methods = @(Find-Methods $spec)
    if ($methods.Count -ne 1) { return $null }
    return $methods[0]
}

function IsDebtLimitGetter($ins) {
    return (IsCall $ins) -and $ins.Member -ne $null -and $ins.Member.Name -eq 'get_DebtLimit' -and $ins.Member.DeclaringType.FullName -eq 'ProcessorTycoon.Bank.CentralBank'
}

if ($CheckCredit) {
    Write-Host ''
    Write-Host '== credit patterns' -ForegroundColor Cyan

    # P10
    $tick = Get-Single 'ProcessorTycoon.Bank.BankruptcyHandler::TickBankruptcies'
    if ($null -eq $tick) {
        Write-Host 'P10 FAIL: BankruptcyHandler.TickBankruptcies not found (or overloaded)' -ForegroundColor Red; $failed = $true
    } else {
        $problems = @()
        $locals = $tick.GetMethodBody().LocalVariables
        if ($locals.Count -lt 3) { $problems += ('{0} local(s), 3 or more expected' -f $locals.Count) }
        else {
            if ($locals[0].LocalType.FullName -ne 'System.Single') { $problems += ('V_0 is ' + $locals[0].LocalType.FullName + ', System.Single expected') }
            if ($locals[2].LocalType.FullName -ne 'ProcessorTycoon.CompanySystem.ICompany') { $problems += ('V_2 is ' + $locals[2].LocalType.FullName + ', ICompany expected') }
        }
        $code = @(Decode-Body $tick)
        $storeAt = @(); $loadAt = @(); $storeOk = $false
        for ($k = 0; $k -lt $code.Count; $k++) {
            if ((StoredLocal $code[$k]) -eq 0) {
                $storeAt += $k
                if ($k -ge 2 -and $code[$k - 1].Name -eq 'neg' -and (IsDebtLimitGetter $code[$k - 2])) { $storeOk = $true }
            }
            if ((LoadedLocal $code[$k]) -eq 0) { $loadAt += $k }
        }
        if ($storeAt.Count -ne 1) { $problems += ('V_0 is stored {0}x, once expected' -f $storeAt.Count) }
        elseif (-not $storeOk) { $problems += 'V_0 is not "-CentralBank.DebtLimit" (call get_DebtLimit; neg; stloc.0)' }
        if ($loadAt.Count -ne 3) { $problems += ('V_0 is loaded {0}x, 3 expected' -f $loadAt.Count) }
        if ($storeAt.Count -eq 1 -and @($loadAt | Where-Object { $_ -lt $storeAt[0] }).Count -gt 0) { $problems += 'V_0 is loaded before it is stored' }
        foreach ($k in $loadAt) {
            if ($code[$k].Name -ne 'ldloc.0') { $problems += ('IL_{0:X4}: {1} (ldloc.0 expected; ldloca would need another rewrite)' -f $code[$k].Offset, $code[$k].Name) }
        }
        if ($problems.Count -eq 0) {
            $where = ($loadAt | ForEach-Object { 'IL_{0:X4}' -f $code[$_].Offset }) -join ', '
            Write-Host ('P10 OK  : TickBankruptcies - V_0 float = -CentralBank.DebtLimit at IL_{0:X4}; 3 loads ({1}); V_2 ICompany' -f $code[$storeAt[0]].Offset, $where) -ForegroundColor Green
        } else {
            Write-Host ('P10 FAIL: TickBankruptcies - ' + ($problems -join '; ')) -ForegroundColor Red
            $failed = $true
        }
    }

    # P11
    foreach ($spec in @('ProcessorTycoon.CompanySystem.PlayerUI.MoneyBalance::UpdateUI', 'ProcessorTycoon.CompanySystem.Money::UpdateMoneyText',
                        'ProcessorTycoon.Bank.BankruptcyHandler::UpdateUI')) {
        $m = Get-Single $spec
        if ($null -eq $m) {
            Write-Host ('P11 FAIL: ' + $spec + ' not found (or overloaded)') -ForegroundColor Red; $failed = $true; continue
        }
        $code = @(Decode-Body $m)
        $hits = @()
        for ($k = 0; $k -lt $code.Count; $k++) { if (IsDebtLimitGetter $code[$k]) { $hits += $k } }
        if ($hits.Count -eq 1) {
            Write-Host ('P11 OK  : {0} - one {1} CentralBank::get_DebtLimit at IL_{2:X4}' -f $spec, $code[$hits[0]].Name, $code[$hits[0]].Offset) -ForegroundColor Green
        } else {
            Write-Host ('P11 FAIL: {0} - {1} call(s) of CentralBank::get_DebtLimit, 1 expected' -f $spec, $hits.Count) -ForegroundColor Red
            $failed = $true
        }
    }
}

if ($failed) { exit 1 }
exit 0
