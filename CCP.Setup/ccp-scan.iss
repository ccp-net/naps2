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
  #define AppVersion "0.2.5"
#endif

#define AppName "CCP SCAN HỒ SƠ ĐẢNG VIÊN"
#define AppPublisher "Chế Công Phước"
#define AppExeName "NAPS2.exe"
#define AppId "{{9B86290F-470B-4BA0-BC95-CC50BB8E0101}"

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
; Windows 10 / Windows 11 x64 only
MinVersion=10.0.10240
SetupIconFile=..\NAPS2.Lib\Icons\favicon.ico
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir={#OutputDir}
OutputBaseFilename=CCP_Scan_Ho_so_Dang_vien_v{#AppVersion}_Win64
Compression=lzma2/ultra64
LZMAUseSeparateProcess=yes
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
; Load current Default.isl first so new Inno Setup messages automatically fall back to English when the older
; Vietnamese translation has not yet translated them.
Name: "vietnamese"; MessagesFile: "compiler:Default.isl,..\NAPS2.Setup\config\windows\inno-lang\Vietnamese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PrereqDir}\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "license.txt"; Flags: ignoreversion
Source: "..\CONTRIBUTORS"; DestDir: "{app}"; DestName: "contributors.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Pdfium on Windows depends on the Microsoft Visual C++ 2015-2022 runtime. Bundle the official x64 redistributable so
; PDF Import works on clean Windows installations as well as machines where the runtime was never installed.
Filename: "{cmd}"; Parameters: "/C """"{tmp}\vc_redist.x64.exe"" /install /quiet /norestart >nul 2>&1 & exit /b 0"""; StatusMsg: "Đang cài Microsoft Visual C++ Runtime..."; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
