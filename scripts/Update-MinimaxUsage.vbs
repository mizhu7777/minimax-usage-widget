' Update-MinimaxUsage.vbs
' VBS wrapper: 完全无窗口运行 PowerShell 后台脚本
' 由 Windows 任务计划程序调用，替代直接调用 powershell.exe
' VBScript 的 Run 方法第2个参数 0 = 隐藏窗口

Dim shell, psScript, scriptDir, psCmd
Set shell = CreateObject("WScript.Shell")

' 获取当前脚本所在目录 (@ScriptFullPath 的所在目录)
scriptDir = CreateObject("Scripting.FileSystemObject").GetParentFolderName(WScript.ScriptFullName)
psScript = scriptDir & "\Update-MinimaxUsage.ps1"
psCmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File """ & psScript & """"

' 0 = 隐藏窗口, True = 等待完成
shell.Run psCmd, 0, True
Set shell = Nothing