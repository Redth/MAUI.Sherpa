#ifndef MyAppVersion
  #error MyAppVersion must be provided
#endif
#ifndef MyAppArch
  #error MyAppArch must be provided
#endif
#ifndef MySourceDir
  #error MySourceDir must be provided
#endif
#ifndef MyOutputDir
  #error MyOutputDir must be provided
#endif

#define MyAppName "MAUI Sherpa"
#define MyAppPublisher "Redth"
#define MyAppExeName "MauiSherpa.exe"

[Setup]
AppId=codes.redth.mauisherpa
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/Redth/MAUI.Sherpa
AppSupportURL=https://github.com/Redth/MAUI.Sherpa/issues
AppUpdatesURL=https://github.com/Redth/MAUI.Sherpa/releases
DefaultDirName={localappdata}\Programs\MAUI Sherpa
DefaultGroupName=MAUI Sherpa
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#MyOutputDir}
OutputBaseFilename=MAUI-Sherpa.Setup.win-{#MyAppArch}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no
#if MyAppArch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64
#elif MyAppArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
  #error MyAppArch must be x64 or arm64
#endif

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\MAUI Sherpa"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\MAUI Sherpa"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch MAUI Sherpa"; Flags: nowait postinstall skipifsilent
