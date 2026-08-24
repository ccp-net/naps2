#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif
#ifndef PrereqDir
  #define PrereqDir ".\cache"
#endif
#ifndef OutputDir
  #define OutputDir "..\publish\setup"
#endif
#ifndef AppVersion
  #define AppVersion "0.2.7"
#endif

#define AppName "CCP SCAN HỒ SƠ ĐẢNG VIÊN"
#define AppPublisher "Chế Công Phước"
#define AppExeName "NAPS2.exe"
#define AppId "{{9B86290F-470B-4BA0-BC95-CC50BB8E0101}"
#define AppUserModelId "CCP.Scan.HoSoDangVien"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoDescription={#AppName} Setup
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
DefaultDirName={autopf}\CCP Scan Hồ sơ Đảng viên
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
WizardStyle=modern
MinVersion=10.0.10240
SetupIconFile=..\NAPS2.Lib\Icons\favicon.ico
UninstallDisplayIcon={app}\favicon.ico
OutputDir={#OutputDir}
OutputBaseFilename=CCP_Scan_Ho_so_Dang_vien_v{#AppVersion}_Win64
Compression=lzma2/ultra64
LZMAUseSeparateProcess=yes
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
; CCP Scan is deployed with Vietnamese as the installer language by default.
Name: "vietnamese"; MessagesFile: "compiler:Default.isl,..\NAPS2.Setup\config\windows\inno-lang\Vietnamese.isl"

[Tasks]
; No "unchecked" flag: the Desktop shortcut is selected by default during installation.
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PrereqDir}\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "license.txt"; Flags: ignoreversion
Source: "..\CONTRIBUTORS"; DestDir: "{app}"; DestName: "contributors.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\favicon.ico"; AppUserModelID: "{#AppUserModelId}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\favicon.ico"; AppUserModelID: "{#AppUserModelId}"; Tasks: desktopicon

[Run]
; Run the official redistributable directly so Inno Setup waits for and observes the real installer process. The old
; cmd.exe wrapper masked failures by always returning exit code 0, which could leave Pdfium without its VC++ runtime.
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Đang cài Microsoft Visual C++ Runtime..."; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
