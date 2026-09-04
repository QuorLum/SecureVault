; Inno Setup Script for SecureVault
; Can be compiled with ISCC.exe on systems where Inno Setup is installed.

#define MyAppName "SecureVault"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "SecureVault Contributors"
#define MyAppURL "https://github.com/QuorLum/SecureVault"
#define MyAppExeName "SecureVault.exe"

[Setup]
AppId={{D14299B7-268B-4D3C-8973-2DC3194B1B89}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\publish
OutputBaseFilename=SecureVault-Setup-Inno
SetupIconFile=..\src\SecureVault.App\Assets\AppIcon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\SecureVault.App\Assets\AppIcon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\AppIcon.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\AppIcon.ico"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\.vault"; ValueType: string; ValueName: ""; ValueData: "SecureVault.VaultContainer"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\SecureVault.VaultContainer"; ValueType: string; ValueName: ""; ValueData: "SecureVault Encrypted Container"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SecureVault.VaultContainer\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\AppIcon.ico,0"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SecureVault.VaultContainer\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
