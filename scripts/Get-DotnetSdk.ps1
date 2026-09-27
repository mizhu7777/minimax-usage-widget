# Get-DotnetSdk.ps1
# G15 修复:项目自带 pinned .NET SDK 存放在 .tools/dotnet(gitignore 不入库)。
# 新克隆环境上 AGENTS.md 列出的 build/test/publish 全部依赖它 —— 本脚本负责引导安装。
# 用法: powershell -ExecutionPolicy Bypass -File scripts\Get-DotnetSdk.ps1

[CmdletBinding()]
param(
    # 与 .tools/dotnet 内当前 pin 的版本保持一致(dotnet --version)
    [string]$Version = '8.0.423'
)

$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $PSScriptRoot
$dotnetDir = Join-Path $projectDir '.tools\dotnet'
$dotnetExe = Join-Path $dotnetDir 'dotnet.exe'

if (Test-Path $dotnetExe) {
    Write-Host "[OK] .NET SDK 已存在: $dotnetExe" -ForegroundColor Green
    & $dotnetExe --version
    exit 0
}

Write-Host "正在下载并安装 .NET SDK $Version 到 $dotnetDir ..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $dotnetDir -Force | Out-Null

$installScript = Join-Path ([IO.Path]::GetTempPath()) 'dotnet-install.ps1'
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installScript -UseBasicParsing
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installScript -Version $Version -InstallDir $dotnetDir
if ($LASTEXITCODE -ne 0) {
    throw ".NET SDK 安装失败: $LASTEXITCODE"
}

if (-not (Test-Path $dotnetExe)) {
    throw "安装流程结束但未找到 $dotnetExe"
}
Write-Host "[OK] 已安装 .NET SDK $Version → $dotnetDir" -ForegroundColor Green
& $dotnetExe --version
