# MiniMax Coding Plan 桌面用量小组件

一个极简的 Windows 桌面小组件，实时显示你订阅的 MiniMax Coding Plan 剩余额度，并支持历史趋势查看。

**所有数据都留在项目目录**，不污染 C 盘，凭据用 Windows DPAPI 加密。

---

## ✨ 功能特性

- **双圆环并列显示**：5 小时与本周剩余额度一目了然
- **常驻桌面小组件**：可拖动，可置顶（`AlwaysOnTop=1` / `Dragging=1`）
- **点击查看详情**：打开独立 Windows 详情窗口，查看完整信息
- **额度趋势**：24 小时、7 天、30 天本机额度变化折线图
- **状态指示**：正常/已过期/错误（基于 `last-success-epoch.txt` 心跳，3 分钟无更新自动降级为"● 已过期"）
- **低额度自动变色**：Rainmeter 皮肤内 Calc 阈值切换（5h/weekly ≤10% 红、≤20% 橙、>20% 蓝/紫）
- **每 1 分钟自动刷新**，失败时显示上次缓存 + 错误提示（降级）
- **5小时窗口 + 周窗口** 双维度展示
- **DPAPI 加密** API Key（Windows 用户绑定，零外部依赖）
- **极简体积**：皮肤文件约 5KB，PowerShell 脚本约 15KB

---

## 📋 架构

```
MiniMax API
    │
    ▼
Update-MinimaxUsage.ps1
    ├─ DPAPI 解密 API Key
    ├─ 请求、重试、校验与标准化
    ├─ 写当前状态 cache.json（schema v2）
    ├─ 写 Rainmeter 简单文本缓存
    └─ 追加成功的额度历史 history.jsonl
              │
              ├─────────────┐
              ▼             ▼
       Rainmeter 小组件   WPF 详情 EXE
       只读简单文本       只读 cache/history
```

设计原则：

- PowerShell 是唯一的数据采集者和写入者
- Rainmeter 与 WPF 均为只读展示层
- API Key 只在 PowerShell 进程中短暂解密，不进入 UI
- 当前状态原子写入，避免读取半截 JSON
- API 失败时保留最后一次成功数据

---

## 🚀 安装步骤

### 1. 安装 Rainmeter

从 [rainmeter.net](https://www.rainmeter.net/) 下载安装（约 3MB，一次性）。

### 2. 复制皮肤到 Rainmeter 皮肤目录

将 `Skins\MiniMaxUsage\` 整个文件夹复制到：

```
%USERPROFILE%\Documents\Rainmeter\Skins\
```

最终位置：`C:\Users\<你>\Documents\Rainmeter\Skins\MiniMaxUsage`

然后：

1. 右键系统托盘的 Rainmeter 图标 → **刷新全部**
2. 在 Configs 下找到 **MiniMaxUsage** → 点击加载

### 3. 配置 API Key（一次性）

打开 PowerShell（普通用户即可），运行：

```powershell
cd D:\claude_work\2026\07\05\minimax
powershell -ExecutionPolicy Bypass -File scripts\Set-MinimaxApiKey.ps1
```

按提示输入你的 MiniMax API Key（以 `sk-cp-` 开头）。

API Key 用 Windows DPAPI 加密后保存到 `.config\apikey.enc`，**只有同一 Windows 用户在同一台机器能解密**。

### 4. 注册定时任务（开机自启）

**以管理员身份**打开 PowerShell，运行：

```powershell
cd D:\claude_work\2026\07\05\minimax
powershell -ExecutionPolicy Bypass -File scripts\Install-Task.ps1
```

这会创建任务计划 `MiniMaxUsageRefresh`：
- 登录时自动启动（`LogonTrigger`，延迟 15 秒）
- 之后每 1 分钟调用一次 API（无限重复，无 1 天寿命）
- 最小权限运行（`LeastPrivilege`，不需要管理员）
- 自动生成 `Variables.inc` 给 Rainmeter 皮肤用
- 与 WPF 详情窗口的「↻ 刷新数据」按钮互斥（命名互斥体）

### 5. 发布详情 EXE（可选，首次需要）

运行以下命令发布自包含详情窗口应用：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Publish-App.ps1
```

发布后 `dist\win-x64\MiniMaxUsage.exe` 约 160MB，用户电脑无需安装 .NET 即可运行。

### 6. 完成

皮肤应立即显示真实数据。如未显示，检查 `.cache\widget.log`。

---

## 📂 项目目录结构

```
D:\claude_work\2026\07\05\minimax\
├─ Skins\MiniMaxUsage\               # Rainmeter 皮肤
│  ├─ MiniMaxUsage.ini               # 双圆环小组件
│  └─ @Resources\
│     ├─ Variables.inc               # 路径与颜色变量（由安装脚本生成）
│     └─ Freshness.lua               # 数据过期检测
├─ src\MiniMaxUsage.App\             # WPF 详情应用源码
│  ├─ Models\                        # JSON 文档与领域模型
│  ├─ Services\                      # 缓存/历史/刷新/定位
│  ├─ ViewModels\                    # MainViewModel
│  ├─ Controls\                      # CircularGauge / TrendChart
│  └─ MainWindow.xaml                # 详情窗口 UI
├─ scripts\
│  ├─ Set-MinimaxApiKey.ps1          # 加密配置 API Key
│  ├─ Update-MinimaxUsage.ps1        # 后台查询脚本（含历史快照）
│  ├─ Install-Task.ps1               # 任务计划管理
│  ├─ Install-Task-Admin.vbs         # 管理员提权入口
│  ├─ Update-MinimaxUsage.vbs        # 无窗口 VBS 包装
│  └─ Publish-App.ps1                # 自包含 EXE 发布
├─ tests\
│  ├─ powershell\                    # PowerShell 测试
│  └─ MiniMaxUsage.App.Tests\        # .NET xUnit 测试
├─ .config\apikey.enc                # DPAPI 加密的 API Key (gitignore)
├─ .cache\
│  ├─ cache.json                     # 最新用量（schema v2）
│  ├─ history.jsonl                  # 额度快照历史（每行一条）
│  ├─ last-success-epoch.txt         # 最后成功时间戳
│  ├─ updated.txt                    # 可读更新时间
│  ├─ pct1.txt / pct2.txt            # 百分比（Rainmeter）
│  ├─ bar1.txt / bar2.txt            # 小数进度（Rainmeter）
│  ├─ reset1.txt / reset2.txt        # 倒计时文本
│  └─ widget.log                     # 运行日志
├─ .gitignore
└─ README.md
```

---

## 🔧 故障排查

### 小组件显示 "● ERR"

API 调用失败。查看 `.cache\widget.log` 了解原因。

### 小组件显示 "● 数据已过期"

超过 3 分钟未成功更新。检查网络或 API Key 是否有效。

### 详情窗口显示 "缓存不可用"

`cache.json` 不存在或格式损坏。运行 `Update-MinimaxUsage.ps1` 手动刷新。

### 详情窗口无趋势图

需要至少一次成功采集后才有历史数据。无数据时窗口显示"开始记录后将显示趋势"。

### 点击"查看详情"无反应

确认已执行 `Publish-App.ps1` 发布 EXE。确认 `dist\win-x64\MiniMaxUsage.exe` 存在。

### 进度条一直是 0

确认 `cache.json` 含有 `remaining_pct` 字段（schema v2）。

### 任务计划不执行

没以管理员运行。重新以管理员运行 `Install-Task.ps1`。

---

## 🔐 安全说明

- **API Key** 通过 Windows DPAPI (`ProtectedData.Protect`) 加密，绑定当前用户 SID
- **不可跨用户/跨机器**：复制 `apikey.enc` 到其他电脑无法解密
- **不写 C 盘**：所有缓存、日志、凭据都在项目目录下
- **不联网**：仅与 `https://www.minimaxi.com` 一个域名通信
- **详情 EXE 不持有 API Key**：所有联网请求统一经过 PowerShell，EXE 只读本地缓存

---

## 📊 数据说明

- 历史数据是本机定时记录的"剩余额度快照"，不是服务商提供的精确 Token 消耗明细
- 额度重置时曲线会明显回升
- 首版只显示 MiniMax Coding Plan 一个服务
- 保留最近 90 天历史

---

## 📜 许可证

MIT

---

## 🙏 参考

- [UsageBoard/minimax-usage-plugin.py](https://github.com/marsmay/UsageBoard) - MiniMax API 端点参考
- [Eyozy/minimax-usage](https://github.com/Eyozy/minimax-usage) - 类似用量的 Web 工具
- [ccusage](https://github.com/ryoppippi/ccusage) - Claude Code 用量统计 CLI（代码分层参考）