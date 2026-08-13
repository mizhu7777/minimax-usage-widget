' Install-Task-Admin.vbs
' 以管理员身份运行 Install-Task.ps1
' 注意：必须确保 VBS 不会被 SmartScreen 拦截，否则不会弹 UAC

Dim shell, fso, scriptDir, psScript
Set fso = CreateObject("Scripting.FileSystemObject")
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
psScript = scriptDir & "\Install-Task.ps1"

Set shell = CreateObject("Shell.Application")

' ShellExecute 第 5 个参数 = 1 表示正常窗口（不最小化）
' "runas" 触发 UAC 弹窗
shell.ShellExecute "powershell.exe", _
    "-NoProfile -ExecutionPolicy Bypass -File """ & psScript & """", _
    scriptDir, _
    "runas", _
    1

Set shell = Nothing
Set fso = Nothing