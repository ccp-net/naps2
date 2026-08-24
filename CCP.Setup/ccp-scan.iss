#ifndef AppArch
  #define AppArch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\publish\setup"
#endif
#ifndef AppVersion
  #define AppVersion "0.2.1"
#endif

#define AppName "CCP SCAN HỒ SƠ ĐẢNG VIÊN"
#define AppPublisher "Chế Công Phước"
#define AppExeName "NAPS2.exe"
#define AppId "{{9B86290F-470B-4BA0-BC95-CC50BB8E0101}"

[Setup]
AppId={#AppId}-{#AppArch}
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
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir={#OutputDir}
OutputBaseFilename=CCP_Scan_Ho_so_Dang_vien_v{#AppVersion}_Win{#AppArch}
Compression=lzma2/ultra64
LZMAUseSeparateProcess=yes
SolidCompression=yes
ArchitecturesAllowed={#AppArch}
#if AppArch == "x64"
ArchitecturesInstallIn64BitMode=x64
#endif

[Languages]
Name: "vietnamese"; MessagesFile: "..\NAPS2.Setup\config\windows\inno-lang\Vietnamese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "license.txt"; Flags: ignoreversion
Source: "..\CONTRIBUTORS"; DestDir: "{app}"; DestName: "contributors.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
