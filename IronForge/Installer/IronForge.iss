; Inno Setup script for the IronForge member app on Windows.
;
; Packs the unpackaged, self-contained publish output (dotnet publish with
; WindowsPackageType=None) into IronForge-Setup.exe. Built by
; .github/workflows/release-apps.yml; see deploy/APP-RELEASES.md.
;
; Local build (after publishing to out\windows):
;   ISCC.exe /DAppVersion=1.2.0 /DSourceDir=<repo>\out\windows /DOutputDir=<repo>\dist IronForge\Installer\IronForge.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

; Numeric file version (Windows file properties); AppVersion may carry a
; pre-release suffix such as 1.2.0-beta.1, this one may not.
#ifndef FileVersion
  #define FileVersion "0.0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\..\out\windows"
#endif

#ifndef OutputDir
  #define OutputDir "..\..\dist"
#endif

#define AppName "IronForge"
#define AppExeName "IronForge.exe"
#define AppPublisher "IronForge"
#define AppUrl "https://mohtashim.dev"

[Setup]
; Never change AppId: it is how a new version finds and replaces the old one.
AppId={{E8D6944F-295C-4EB7-B65A-637013C2DB05}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
VersionInfoVersion={#FileVersion}
VersionInfoProductTextVersion={#AppVersion}

; Per-user install: no administrator rights or UAC prompt needed. Someone who
; wants it for all users can choose that in the first page of the wizard.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; The folder cannot be chosen: [InstallDelete] empties it on every install,
; which is only safe for a folder that holds nothing but IronForge.
DisableDirPage=yes

; The app is published for 64-bit Windows 10 1809 or later.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

; Close a running IronForge before replacing its files.
CloseApplications=yes
RestartApplications=no

UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

OutputDir={#OutputDir}
OutputBaseFilename=IronForge-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; An upgrade starts from a clean program folder, so files a newer version no
; longer ships cannot linger. The app keeps its data (sign-in, cached pass)
; under the user's profile, not here, so it survives.
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
