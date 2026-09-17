Option Explicit
Dim shell, fso, folder, exe, shortcut
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
folder = fso.GetParentFolderName(WScript.ScriptFullName)
exe = fso.BuildPath(folder, "CodexQuota.exe")
If Not fso.FileExists(exe) Then
  MsgBox "CodexQuota.exe is missing. Keep all files in the same folder.", 16, "Codex Quota"
  WScript.Quit 1
End If
Set shortcut = shell.CreateShortcut(fso.BuildPath(shell.SpecialFolders("Desktop"), "Codex Quota.lnk"))
shortcut.TargetPath = exe
shortcut.WorkingDirectory = folder
shortcut.Description = "Codex quota widget - refresh every 60 seconds"
shortcut.IconLocation = fso.BuildPath(folder, "CodexQuota.ico") & ",0"
shortcut.Save
shell.Run Chr(34) & exe & Chr(34), 1, False
