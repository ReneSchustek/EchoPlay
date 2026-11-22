; Inno-Setup-Skript für EchoPlay — erzeugt eine Setup.exe aus dem self-contained
; Publish (dist\setup-publish). Die App bringt die .NET- und Windows-App-SDK-Laufzeit
; selbst mit (WindowsAppSDKSelfContained), daher braucht der Zielrechner keine
; vorinstallierte Runtime und das Paket braucht kein Code-Signing-Zertifikat. Die Echtheit
; der fertigen Setup.exe prüft der Auto-Updater über eine eigene Signatur (tools\sign-release.ps1).
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
; Entspricht der MSIX-Identity aus Package.appxmanifest, damit beide Installationswege dieselbe Anwendung meinen.
AppId={{4507e02b-3490-46e6-9510-6f111d5926de}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppSupportURL}
AppUpdatesURL={#MyAppURL}
; Mit Adminrechten löst {autopf} auf C:\Program Files auf. Der Benutzer kann den Pfad
; frei ändern; ein früher gewählter Ordner wird nicht vorgeschlagen, weil ältere Versionen
; ins Benutzerprofil installierten und der Vorschlag dorthin zurückführte.
DefaultDirName={autopf}\{#MyAppName}
UsePreviousAppDir=no
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
AllowUNCPath=no
; Programme gehören nach C:\Program Files, nicht ins Benutzerprofil. Das Setup fragt
; deshalb immer nach Adminrechten (UAC); die eingebaute Aktualisierung startet es über die
; Shell, sodass Windows die Abfrage zeigt.
PrivilegesRequired=admin
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

[CustomMessages]
german.OldUserInstallRemovalFailed=Die bisherige Installation im Benutzerprofil ließ sich nicht entfernen. Bitte EchoPlay über „Apps & Features“ deinstallieren und das Setup erneut starten. Einstellungen und Mediathek bleiben dabei erhalten.
english.OldUserInstallRemovalFailed=The previous installation in the user profile could not be removed. Please uninstall EchoPlay via "Apps & features" and run the setup again. Settings and library are kept.

[Code]
// Ältere Versionen installierten ohne Adminrechte nach %LOCALAPPDATA%\Programs und trugen
// sich unter HKCU als installiert ein. Bleibt diese Installation liegen, stünde EchoPlay
// zweimal auf dem Rechner und in der Liste der Apps. Das Setup entfernt sie deshalb still,
// bevor es unter Program Files installiert. Einstellungen und Mediathek liegen getrennt
// unter %LOCALAPPDATA%\EchoPlay und bleiben unberührt.
const
  UserUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{4507e02b-3490-46e6-9510-6f111d5926de}_is1';

function RemoveUserProfileInstallation(): Boolean;
var
  UninstallString: String;
  ResultCode: Integer;
begin
  Result := True;
  if not RegQueryStringValue(HKCU, UserUninstallKey, 'UninstallString', UninstallString) then
    Exit;

  UninstallString := RemoveQuotes(UninstallString);
  if not FileExists(UninstallString) then
    Exit;

  Log('Installation im Benutzerprofil gefunden, wird entfernt: ' + UninstallString);
  Result := Exec(UninstallString, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not RemoveUserProfileInstallation() then
    Result := ExpandConstant('{cm:OldUserInstallRemovalFailed}');
end;
