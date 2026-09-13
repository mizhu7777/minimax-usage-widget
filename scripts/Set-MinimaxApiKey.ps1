# Set-MinimaxApiKey.ps1
# 一次性配置脚本：使用 DPAPI 加密 MiniMax API Key 并保存到 .config/apikey.enc
# 运行: powershell -ExecutionPolicy Bypass -File scripts\Set-MinimaxApiKey.ps1

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# === 路径解析（相对脚本位置，全部留在项目目录下） ===
$scriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Definition
$projectDir = Split-Path -Parent $scriptDir
$configDir  = Join-Path $projectDir '.config'
$cacheDir   = Join-Path $projectDir '.cache'
$apiKeyEnc  = Join-Path $configDir 'apikey.enc'
$updateScript = Join-Path $scriptDir 'Update-MinimaxUsage.ps1'

# 确保目录存在
@($configDir, $cacheDir) | ForEach-Object {
    if (-not (Test-Path $_)) { New-Item -ItemType Directory -Path $_ -Force | Out-Null }
}

Write-Host ''
Write-Host '=== MiniMax API Key 配置 ===' -ForegroundColor Cyan
Write-Host "项目目录: $projectDir"
Write-Host "加密文件: $apiKeyEnc"
Write-Host ''

# 已存在则询问是否覆盖
if (Test-Path $apiKeyEnc) {
    $overwrite = Read-Host '检测到已加密的 API Key，是否覆盖？(y/N)'
    if ($overwrite -ne 'y' -and $overwrite -ne 'Y') {
        Write-Host '已取消。' -ForegroundColor Yellow
        exit 0
    }
}

# 读取 API Key（SecureString 转明文以加密）
Write-Host '请输入 MiniMax API Key (输入隐藏):' -ForegroundColor Green
$secure = Read-Host -AsSecureString -Prompt 'API Key'
$BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    # P3-1 修复:用 PtrToStringBSTR(MS 对 BSTR 指针的规范 API),
    # 替代 PtrToStringAuto(语义上针对 ANSI/Unicode 自动选择,对 BSTR 不合适)
    $plainKey = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($BSTR)
} finally {
    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)
}

if ([string]::IsNullOrWhiteSpace($plainKey)) {
    Write-Host '错误：API Key 不能为空' -ForegroundColor Red
    exit 1
}

# DPAPI 加密（仅当前 Windows 用户能解密）
try {
    Add-Type -AssemblyName System.Security
    $bytes  = [System.Text.Encoding]::UTF8.GetBytes($plainKey)
    $encBytes = [System.Security.Cryptography.ProtectedData]::Protect(
        $bytes, $null,
        [System.Security.Cryptography.DataProtectionScope]::CurrentUser
    )
    [System.IO.File]::WriteAllBytes($apiKeyEnc, $encBytes)
    Write-Host "[OK] API Key 已加密保存到 $apiKeyEnc" -ForegroundColor Green
} catch {
    Write-Host "[ERR] 加密失败: $_" -ForegroundColor Red
    exit 1
} finally {
    # 清除内存中的明文
    $plainKey = $null
    $bytes = $null
    [System.GC]::Collect()
}

# 立即运行一次 Update 脚本验证
Write-Host ''
Write-Host '--- 立即运行一次刷新验证 ---' -ForegroundColor Cyan
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $updateScript
$exitCode = $LASTEXITCODE

# P0-3 修复:退出码 2 = 已有刷新实例在运行(计划任务抢占),验证本身没有失败
if ($exitCode -eq 0 -or $exitCode -eq 2) {
    Write-Host ''
    Write-Host '=== 配置完成 ===' -ForegroundColor Green
    Write-Host '下一步: 运行 scripts\Install-Task.ps1 注册定时任务' -ForegroundColor Yellow
} else {
    Write-Host ''
    Write-Host '=== 配置完成（但验证失败）===' -ForegroundColor Yellow
    Write-Host '请检查网络/Key 是否正确，日志见: .cache\widget.log' -ForegroundColor Yellow
}

exit $exitCode