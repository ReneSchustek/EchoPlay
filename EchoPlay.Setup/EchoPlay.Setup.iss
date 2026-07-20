; Inno-Setup-Skript für EchoPlay — erzeugt eine Setup.exe aus dem self-contained
; Publish (dist\setup-publish). Die App bringt die .NET- und Windows-App-SDK-Laufzeit
; selbst mit (WindowsAppSDKSelfContained), daher braucht der Zielrechner keine
; vorinstallierte Runtime und das Paket muss nicht signiert werden.
;
; Build (vom Repo-Root):
;   tools\publish-setup.ps1            -> publiziert + kompiliert die Setup.exe
; oder direkt (Publish muss vorher unter dist\setup-publish liegen):
;   ISCC.exe /DMyAppVersion=1.0.0 EchoPlay.Setup\EchoPlay.Setup.iss

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppName "EchoPlay"
#define MyAppPublisher "Ruhrcoder (René Schustek)"
#define MyAppURL "https://ruhrcoder.de"
#define MyAppSupportURL "https://github.com/ReneSchustek/EchoPlay/issues"
#define MyAppExeName "EchoPlay.App.exe"
#define PublishDir "..\dist\setup-publish"

[Setup]
; Stabile AppId (nicht ändern — identifiziert die Installation für Upgrade/Deinstallation).
; Entspricht der bisherigen MSIX-Identity, damit die Zuordnung erhalten bleibt.
AppId={{4507e02b-3490-46e6-9510-6f111d5926de}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppSupportURL}
AppUpdatesURL={#MyAppURL}
; {autopf} passt sich der Installationsart an: Programme (alle Benutzer) oder
; %LOCALAPPDATA%\Programs (nur ich). Der Benutzer kann den Pfad frei ändern.
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
AllowUNCPath=no
; Standard ohne Adminrechte (nur ich); im Dialog auf "für alle Benutzer" wechselbar
; (dann UAC-Elevation für den Programme-Ordner).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; Windows 10 Version 1809 (Build 17763) ist das Minimum — ältere Versionen sind nicht
; kompatibel mit WinUI 3 und der verwendeten Windows App SDK-Version.
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=EchoPlay-Setup-v{#MyAppVersion}
LicenseFile=LICENSE.txt
SetupIconFile=..\src\EchoPlay.App\Assets\favicon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Der komplette self-contained Publish (App + gebündelte Laufzeit) wird nach {app} kopiert.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
