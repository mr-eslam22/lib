; Inno Setup Script for Windows Print Automation System
#define MyAppName "Windows Print Automation System"
#define MyAppArabicName "نظام إدارة المطبعة والطباعة"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Print Automation Team"
#define MyAppExeName "WindowsPrintAutomation.UI.exe"

[Setup]
AppId={{8B84F01A-399B-4D88-97F7-2D917CFD4F88}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\WindowsPrintAutomation
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\artifacts
OutputBaseFilename=WindowsPrintAutomation-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
