[CmdletBinding()]
param([ValidateSet('win-x64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $projectDir '.tools\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { throw '缺少项目内 .NET SDK' }
$output = Join-Path (Join-Path $projectDir dist) $Runtime
$csproj = Join-Path $projectDir 'src\MiniMaxUsage.App\MiniMaxUsage.App.csproj'
& $dotnet publish $csproj -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败: $LASTEXITCODE" }
$exe = Join-Path $output 'MiniMaxUsage.exe'
if (-not (Test-Path $exe)) { throw "发布完成但未找到 $exe" }
Write-Host "[OK] $exe"