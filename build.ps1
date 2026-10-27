<#
.SYNOPSIS
    编译 WinHide，输出到 dist\WinHide.exe

.DESCRIPTION
    只依赖 .NET Framework 4.x 自带的 csc.exe，不需要安装 Visual Studio 或 SDK。
    图标、DPI 清单、版本信息都会编译进同一个 exe。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1
#>
[CmdletBinding()]
param(
    [string]$Output = 'dist'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'

$csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw '找不到 csc.exe，请先安装 .NET Framework 4.x'
}

$outDir = Join-Path $root $Output
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$outExe = Join-Path $outDir 'WinHide.exe'

$icon = Join-Path $src 'app.ico'
$manifest = Join-Path $src 'app.manifest'

& $csc /nologo /codepage:65001 /target:winexe /platform:anycpu `
    "/out:$outExe" `
    "/win32icon:$icon" `
    "/win32manifest:$manifest" `
    "/resource:$icon" `
    (Join-Path $src 'AssemblyInfo.cs') (Join-Path $src 'WindowHider.cs') `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll

if ($LASTEXITCODE -ne 0) {
    throw "编译失败，csc 退出码 $LASTEXITCODE"
}

$vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($outExe)
$size = [math]::Round((Get-Item $outExe).Length / 1KB, 1)
Write-Host "编译成功: $outExe"
Write-Host "  版本 $($vi.ProductVersion)，大小 $size KB"
