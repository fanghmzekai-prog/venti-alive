Unicode true
RequestExecutionLevel user
SetCompressor /SOLID lzma
CRCCheck force
ManifestDPIAware true
ShowInstDetails nevershow
ShowUninstDetails nevershow

!include MUI2.nsh
!include LogicLib.nsh
!include FileFunc.nsh
!include WinVer.nsh
!include x64.nsh

!ifdef FIXTURE
  !define PRODUCT_KEY "AerisAlive.SetupTest"
  !define SHORTCUT "温迪桌宠（安装测试）"
  !define STARTUP_KEY "Software\AerisAlive.SetupTest\Run"
  !ifdef FIXTURE_ROOT
    !define APPDATA_ROOT "${FIXTURE_ROOT}"
  !endif
!else
  !define PRODUCT_KEY "AerisAlive"
  !define SHORTCUT "温迪桌宠 · VENTI ALIVE"
  !define STARTUP_KEY "Software\Microsoft\Windows\CurrentVersion\Run"
!endif
!ifndef APPDATA_ROOT
  !define APPDATA_ROOT "$LOCALAPPDATA\${PRODUCT_KEY}"
!endif
!define APP_ROOT "${APPDATA_ROOT}\Installed"
!define REG_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_KEY}"
!define LINK "$SMPROGRAMS\${SHORTCUT}.lnk"
!define MARKER ".venti-setup"
!define MARKER_TEXT "VENTI ALIVE Setup 1"

Name "温迪桌宠 · VENTI ALIVE"
Caption "VENTI ALIVE ${VERSION}"
BrandingText "VENTI ALIVE"
OutFile "${OUTPUT}"
InstallDir "${APP_ROOT}"
VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=2052 "ProductName" "VENTI ALIVE 温迪桌宠"
VIAddVersionKey /LANG=2052 "FileDescription" "VENTI ALIVE 安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=2052 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=2052 "LegalCopyright" "VENTI ALIVE contributors"

!define MUI_ICON "${ARTWORK}\venti.ico"
!define MUI_UNICON "${ARTWORK}\venti.ico"
!define MUI_WELCOMEFINISHPAGE_BITMAP "${ARTWORK}\cover.bmp"
!define MUI_BGCOLOR "F2F5EF"
!define MUI_TEXTCOLOR "193930"
!define MUI_WELCOMEPAGE_TITLE "安装温迪桌宠"
!define MUI_WELCOMEPAGE_TEXT "让温迪在你的 Windows 桌面上安家。你可以和他互动、看他弹琴或自在飞行，也可以接入本机模型进行离线聊天。$\r$\n$\r$\n本次为当前用户安装，并在开始菜单添加入口。"
!define MUI_FINISHPAGE_TITLE "温迪已准备好"
!define MUI_FINISHPAGE_TEXT "以后可从开始菜单打开「温迪桌宠 · VENTI ALIVE」。"
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchVenti
!define MUI_FINISHPAGE_RUN_TEXT "现在启动温迪"
!define MUI_ABORTWARNING

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

Var SetupLock
Var VentiLock
Var Stage
Var PendingLink
Var PriorVersion
Var PriorStartup

!macro ReadInstalledStartup VALUE
  StrCpy ${VALUE} ""
  ReadRegStr $2 HKCU "${STARTUP_KEY}" "${PRODUCT_KEY}"
  StrCpy $0 '$\"$INSTDIR\versions\'
  StrLen $1 $0
  StrCpy $1 $2 $1
  ${If} $1 == $0
    StrCpy ${VALUE} $2
  ${EndIf}
!macroend

!macro Reject CODE DESCRIPTION
  IfSilent +2 0
    MessageBox MB_OK|MB_ICONEXCLAMATION "${DESCRIPTION}"
  SetErrorLevel ${CODE}
  Abort "${DESCRIPTION}"
!macroend

!macro AcquireMutex HANDLE NAME CODE DESCRIPTION
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Local\${NAME}") p.s'
  Pop ${HANDLE}
  ${If} ${HANDLE} == 0
    !insertmacro Reject ${CODE} "${DESCRIPTION}"
  ${EndIf}
  System::Call 'kernel32::WaitForSingleObject(p ${HANDLE}, i 0) i.r0'
  ${If} $0 != 0
  ${AndIf} $0 != 128
    !insertmacro Reject ${CODE} "${DESCRIPTION}"
  ${EndIf}
!macroend

!macro AcquireSetupLock
  !insertmacro AcquireMutex $SetupLock "${PRODUCT_KEY}.Setup" 2 "另一个温迪安装或卸载程序正在运行。"
!macroend

!macro AcquireVentiLock
  !ifdef FIXTURE
    !define /redef RUNNING_MUTEX "AerisAlive.SetupTest.Desktop"
  !else
    !define /redef RUNNING_MUTEX "AerisAlive.Desktop"
  !endif
  !insertmacro AcquireMutex $VentiLock "${RUNNING_MUTEX}" 3 "请先从主控窗口退出温迪，再运行此程序。"
!macroend

!macro RequirePlainDirectory PATH
  System::Call 'kernel32::GetFileAttributesW(w "${PATH}") i.r0'
  ${If} $0 != -1
    IntOp $0 $0 & 0x410
    ${If} $0 <> 0x10
      !insertmacro Reject 4 "安装路径中有链接或同名文件。请检查目录后再试。"
    ${EndIf}
  ${EndIf}
!macroend

!macro RequireOwnedRoot
  !insertmacro RequirePlainDirectory "$LOCALAPPDATA"
  !insertmacro RequirePlainDirectory "${APPDATA_ROOT}"
  !insertmacro RequirePlainDirectory "$INSTDIR"
  !insertmacro RequirePlainDirectory "$INSTDIR\versions"
  System::Call 'kernel32::GetFileAttributesW(w "$INSTDIR\uninstall.exe") i.r0'
  ${If} $0 != -1
    IntOp $0 $0 & 0x410
    ${If} $0 != 0
      !insertmacro Reject 4 "卸载程序的路径已被占用。请检查后再试。"
    ${EndIf}
  ${EndIf}
  System::Call 'kernel32::GetFileAttributesW(w "$INSTDIR") i.r0'
  ${If} $0 != -1
    System::Call 'kernel32::GetFileAttributesW(w "$INSTDIR\${MARKER}") i.r0'
    IntOp $0 $0 & 0x410
    ${If} $0 != 0
      !insertmacro Reject 4 "这个安装目录已被占用。请检查后再试。"
    ${EndIf}
    StrCpy $2 ""
    ClearErrors
    FileOpen $1 "$INSTDIR\${MARKER}" r
    ${IfNot} ${Errors}
      FileRead $1 $2
      FileClose $1
    ${EndIf}
    ${If} $2 != "${MARKER_TEXT}"
      !insertmacro Reject 4 "这个安装目录已被占用。请检查后再试。"
    ${EndIf}
  ${EndIf}
!macroend

Function .onInit
  SetShellVarContext current
  SetRegView 64
  StrCpy $INSTDIR "${APP_ROOT}"
  ${IfNot} ${AtLeastWin10}
  ${OrIfNot} ${RunningX64}
    !insertmacro Reject 5 "需要 64 位 Windows 10 或 Windows 11。"
  ${EndIf}
  !insertmacro AcquireSetupLock
FunctionEnd

Section "安装" INSTALL_VENTI
  !insertmacro AcquireVentiLock
  !insertmacro RequireOwnedRoot

  ${GetRoot} "$INSTDIR" $0
  ${DriveSpace} "$0" "/D=F /S=M" $1
  SectionGetSize ${INSTALL_VENTI} $2
  IntOp $2 $2 / 1024
  IntOp $2 $2 + 8
  ${If} $1 != ""
  ${AndIf} $1 < $2
    !insertmacro Reject 7 "安装空间不足。请在安装盘至少腾出 $2 MB 后重试。"
  ${EndIf}

  ReadRegStr $0 HKCU "${REG_KEY}" "InstallLocation"
  ${If} $0 != ""
  ${AndIf} $0 != "$INSTDIR"
    !insertmacro Reject 4 "Windows 已记录另一处温迪安装。请先检查已有安装。"
  ${EndIf}
  ${If} ${FileExists} "${LINK}"
  ${AndIf} $0 == ""
    !insertmacro Reject 4 "开始菜单已有同名入口。请先检查该快捷方式。"
  ${EndIf}
  ReadRegStr $PriorVersion HKCU "${REG_KEY}" "DisplayVersion"
  !insertmacro ReadInstalledStartup $PriorStartup

  ClearErrors
  CreateDirectory "$INSTDIR"
  IfErrors failed
  ${IfNot} ${FileExists} "$INSTDIR\${MARKER}"
    FileOpen $1 "$INSTDIR\${MARKER}" w
    IfErrors failed
    FileWrite $1 "${MARKER_TEXT}"
    FileClose $1
    IfErrors failed
  ${EndIf}

  CreateDirectory "$INSTDIR\versions"
  IfErrors failed
  GetTempFileName $0 "$INSTDIR\versions"
  IfErrors failed
  ${GetFileName} "$0" $1
  Delete "$0"
  StrCpy $Stage "$INSTDIR\versions\${VERSION}-$1"
  ${If} ${FileExists} "$Stage"
    StrCpy $Stage ""
    Goto failed
  ${EndIf}
  SetOutPath "$Stage"
  File /r "${PAYLOAD}\runtime"
  File /r "${PAYLOAD}\assets"
  IfErrors failed

  GetTempFileName $0 "$SMPROGRAMS"
  IfErrors failed
  Delete "$0"
  StrCpy $PendingLink "$0.lnk"
  SetOutPath "$Stage\runtime"
  CreateShortCut "$PendingLink" "$Stage\runtime\Aeris.WindowProbe.exe" \
    '--preview --sprite "$Stage\assets\front.png" --motion "$Stage\assets" --play' \
    "$Stage\runtime\Aeris.WindowProbe.exe" 0 SW_SHOWNORMAL "" "VENTI ALIVE 温迪桌宠"
  IfErrors failed

  WriteUninstaller "$INSTDIR\uninstall.exe"
  IfErrors failed

  WriteRegStr HKCU "${REG_KEY}" "DisplayName" "温迪桌宠 · VENTI ALIVE"
  WriteRegStr HKCU "${REG_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${REG_KEY}" "UninstallString" '$\"$INSTDIR\uninstall.exe$\"'
  WriteRegStr HKCU "${REG_KEY}" "DisplayIcon" "$Stage\runtime\Aeris.WindowProbe.exe,0"
  WriteRegStr HKCU "${REG_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegDWORD HKCU "${REG_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${REG_KEY}" "NoRepair" 1
  IfErrors failed_registry

  ${If} $PriorStartup != ""
    WriteRegStr HKCU "${STARTUP_KEY}" "${PRODUCT_KEY}" '$\"$Stage\runtime\Aeris.WindowProbe.exe$\" --startup'
    IfErrors failed_registry
  ${EndIf}

  ; The single shortcut switch happens only after extraction succeeds.
  System::Call 'kernel32::MoveFileExW(w "$PendingLink", w "${LINK}", i 9) i.r0'
  ${If} $0 == 0
    Goto failed_registry
  ${EndIf}
  Goto finished

failed_registry:
  ${If} $PriorStartup != ""
    WriteRegStr HKCU "${STARTUP_KEY}" "${PRODUCT_KEY}" "$PriorStartup"
  ${EndIf}
  ${If} $PriorVersion == ""
    DeleteRegKey HKCU "${REG_KEY}"
  ${Else}
    WriteRegStr HKCU "${REG_KEY}" "DisplayVersion" "$PriorVersion"
    WriteRegStr HKCU "${REG_KEY}" "DisplayIcon" "$INSTDIR\uninstall.exe,0"
  ${EndIf}
failed:
  SetOutPath "$LOCALAPPDATA"
  ${If} $PendingLink != ""
    Delete "$PendingLink"
  ${EndIf}
  ${If} $Stage != ""
    ; Stage was allocated by this run under the verified private versions root.
    RMDir /r "$Stage"
  ${EndIf}
  !insertmacro Reject 6 "安装未能完成。请检查磁盘空间或文件权限，然后重试。"
finished:
  System::Call 'kernel32::ReleaseMutex(p $VentiLock)'
  System::Call 'kernel32::CloseHandle(p $VentiLock)'
SectionEnd

Function LaunchVenti
  ExecShell "open" "${LINK}"
FunctionEnd

Function un.onInit
  SetShellVarContext current
  SetRegView 64
  ${If} "$INSTDIR" != "${APP_ROOT}"
    !insertmacro Reject 4 "请从 Windows 的已安装应用中卸载温迪桌宠。"
  ${EndIf}
  !insertmacro AcquireSetupLock
FunctionEnd

; Refuse recursive removal if any installed directory or file is a link.
Function un.ValidateTree
  Exch $R0
  Push $R1
  Push $R2
  Push $R3
  ClearErrors
  FindFirst $R1 $R2 "$R0\*"
  ${If} ${Errors}
    !insertmacro Reject 4 "无法读取安装目录，已停止卸载。"
  ${EndIf}
  ${DoWhile} $R2 != ""
    ${If} $R2 != "."
    ${AndIf} $R2 != ".."
      System::Call 'kernel32::GetFileAttributesW(w "$R0\$R2") i.R3'
      IntOp $3 $R3 & 0x400
      ${If} $3 != 0
        FindClose $R1
        !insertmacro Reject 4 "安装目录存在链接或无法读取的文件，已停止卸载。"
      ${EndIf}
      IntOp $3 $R3 & 0x10
      ${If} $3 != 0
        Push "$R0\$R2"
        Call un.ValidateTree
      ${EndIf}
    ${EndIf}
    StrCpy $R2 ""
    FindNext $R1 $R2
  ${Loop}
  FindClose $R1
  Pop $R3
  Pop $R2
  Pop $R1
  Pop $R0
FunctionEnd

Section "Uninstall"
  !insertmacro AcquireVentiLock
  !insertmacro RequireOwnedRoot
  SetOutPath "$LOCALAPPDATA"
  ${If} ${FileExists} "$INSTDIR\versions\."
    Push "$INSTDIR\versions"
    Call un.ValidateTree
    ClearErrors
    RMDir /r "$INSTDIR\versions"
    ${If} ${Errors}
      !insertmacro Reject 6 "部分文件仍在使用。请关闭温迪后重试。"
    ${EndIf}
  ${EndIf}
  ${If} ${FileExists} "${LINK}"
    ClearErrors
    Delete "${LINK}"
    IfErrors incomplete
  ${EndIf}
  !insertmacro ReadInstalledStartup $PriorStartup
  ${If} $PriorStartup != ""
    ClearErrors
    DeleteRegValue HKCU "${STARTUP_KEY}" "${PRODUCT_KEY}"
    IfErrors incomplete
  ${EndIf}
  Delete "$INSTDIR\${MARKER}"
  Delete "$INSTDIR\uninstall.exe"
  DeleteRegKey HKCU "${REG_KEY}"
  RMDir "$INSTDIR"
  Goto removed
incomplete:
  !insertmacro Reject 6 "无法移除开始菜单入口或自启动设置。请稍后重试卸载。"
removed:
SectionEnd
