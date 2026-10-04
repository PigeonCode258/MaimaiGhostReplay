<#
=============================================================================
 FindMethod.ps1 —— 用 Mono.Cecil 直接读取 Assembly-CSharp.dll 的元数据
 (不加载游戏、不启动游戏也能查类 / 查方法签名 —— 写 Harmony 补丁时的主力工具)

 用法:
   .\FindMethod.ps1 -Type NoteBase
   .\FindMethod.ps1 -Type Manager.GameManager -Member IsAutoPlay
   .\FindMethod.ps1 -Type NoteBase -Methods
   .\FindMethod.ps1 -Search "Judge"
=============================================================================
#>
[CmdletBinding(DefaultParameterSetName='Type')]
param(
    [Parameter(ParameterSetName='Type', Position=0)]
    [string]$Type,

    [Parameter(ParameterSetName='Type')]
    [string]$Member,

    [Parameter(ParameterSetName='Type')]
    [switch]$Methods,

    [Parameter(ParameterSetName='Search')]
    [string]$Search,

    [string]$GameRoot = (Join-Path $PSScriptRoot '..\..\SDEZ170\Package')
)

$ErrorActionPreference = 'Stop'

$GameRoot = (Resolve-Path $GameRoot).Path
$asmPath  = Join-Path $GameRoot 'Sinmai_Data\Managed\Assembly-CSharp.dll'
$cecil    = Join-Path $GameRoot 'MelonLoader\net35\Mono.Cecil.dll'

if (-not (Test-Path $cecil)) { throw "找不到 Mono.Cecil.dll: $cecil" }
Add-Type -Path $cecil

$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($asmPath)
Write-Host "程序集: $($asm.Name.FullName)" -ForegroundColor Cyan

# ------------------------------------------------------------------ 搜索模式
if ($PSCmdlet.ParameterSetName -eq 'Search') {
    $asm.MainModule.Types |
        Where-Object { $_.FullName -like "*$Search*" } |
        ForEach-Object { Write-Host "  $($_.FullName)" }
    $asm.MainModule.Types | ForEach-Object {
        $t = $_
        $t.Methods | Where-Object { $_.Name -like "*$Search*" } | ForEach-Object {
            Write-Host ("  {0}::{1}" -f $t.FullName, $_.Name) -ForegroundColor Yellow
        }
    }
    return
}

if (-not $Type) { throw "请用 -Type 指定类型名, 或 -Search 模糊搜索" }

# ------------------------------------------------------------------ 找类型
$targets = $asm.MainModule.Types | Where-Object {
    $_.FullName -eq $Type -or $_.Name -eq $Type -or $_.FullName -like "*.$Type"
}

if (-not $targets) { throw "找不到类型: $Type" }

foreach ($t in $targets) {
    Write-Host "`n=== $($t.FullName)  [$(if($t.IsAbstract){'abstract '})$($t.BaseType)] ===" -ForegroundColor Green
    Write-Host "  基类: $($t.BaseType)"

    foreach ($m in $t.Methods) {
        if (-not $Methods -and $Member -and $m.Name -ne $Member) { continue }
        if (-not $Methods -and -not $Member -and ($m.IsGetter -or $m.IsSetter)) { continue }

        # 只显示 public / protected, 或用户点名要看的
        $vis = if ($m.IsPublic) { 'public' }
               elseif ($m.IsFamily) { 'protected' }
               elseif ($m.IsAssembly) { 'internal' }
               else { 'private' }

        if (-not $Methods -and -not $Member -and $vis -eq 'private') { continue }

        $parms = ($m.Parameters | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ', '
        $attr  = @()
        if ($m.IsStatic)  { $attr += 'static' }
        if ($m.IsVirtual) { $attr += 'virtual' }
        if ($m.IsAbstract){ $attr += 'abstract' }

        Write-Host ("  {0,-9} {1,-7} {2} {3}({4})" -f $vis, ($attr -join ' '), $m.ReturnType.Name, $m.Name, $parms)
    }
}
