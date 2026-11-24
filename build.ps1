# 一键构建 TouchpadGuard.exe（使用 Windows 自带的 .NET Framework 编译器，无需安装 SDK）
param(
    [string]$OutDir = (Join-Path $PSScriptRoot 'bin')
)

$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw '找不到 .NET Framework 的 csc.exe，请确认系统已启用 .NET Framework 4.x。'
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$target = Join-Path $OutDir 'TouchpadGuard.exe'

& $csc /nologo /target:winexe /optimize+ "/out:$target" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    (Join-Path $PSScriptRoot 'TouchpadGuard.cs')

if ($LASTEXITCODE -ne 0) { throw '编译失败。' }

Write-Host "已生成: $target"
Write-Host '双击运行即可，程序会常驻托盘。'
