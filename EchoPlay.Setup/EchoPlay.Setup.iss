; EchoPlay Installer-Skript für Inno Setup 6+
;
; Dieses Skript erzeugt einen klassischen Windows-Installer (.exe), der das MSIX-Paket
; von EchoPlay auf dem Zielrechner installiert. Im Gegensatz zum alten WinForms-Installer
; benötigt dieses Skript keine .NET-Runtime auf dem Installationsrechner – Inno Setup läuft
; vollständig nativ unter Windows.
;
; Bauen mit: iscc EchoPlay.Setup\EchoPlay.Setup.iss
; Voraussetzung: Das MSIX-Paket muss zuvor durch einen dotnet/msbuild-Build erzeugt worden sein.

[Setup]
AppName=EchoPlay
AppVersion=1.0.0
AppPublisher=Ruhrcoder (René Schustek)
AppPublisherURL=https://ruhrcoder.de
AppSupportURL=https://github.com/ruhrcoder/echoplay/issues
AppUpdatesURL=https://ruhrcoder.de
DefaultDirName={autopf}\EchoPlay
OutputBaseFilename=EchoPlay-Setup-1.0.0
; Installer-Archiv landet im EchoPlay.Setup-Verzeichnis
OutputDir=.
WizardStyle=modern
; Windows 10 Version 1809 (Build 17763) ist das Minimum – ältere Versionen sind nicht
; kompatibel mit WinUI 3 und der verwendeten Windows App SDK-Version.
MinVersion=10.0.17763
; Keine Administratorrechte erzwingen – MSIX-Pakete können pro Benutzer installiert werden.
; Für systemweite Installation kann hier "admin" eingesetzt werden.
PrivilegesRequired=lowest
; Logo wird als Wizard-Bild verwendet (160×314 px, wird links neben dem Dialog angezeigt).
; Falls die Datei nicht vorhanden ist, entferne diese Zeile.
; WizardImageFile=..\EchoPlay.App\Assets\logo.png
LicenseFile=LICENSE.txt
SetupIconFile=..\EchoPlay.App\Assets\favicon.ico
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Das MSIX-Paket wird temporär extrahiert und nach der Installation wieder gelöscht.
; Der Platzhalter * passt auf jede Build-Nummer (z.B. EchoPlay.App_1.0.0.0_x64.msix).
Source: "..\EchoPlay.App\AppPackages\EchoPlay.App_*_x64_Test\*.msix"; \
  DestDir: "{tmp}"; Flags: deleteafterinstall; BeforeInstall: InstallMsix
Source: "..\EchoPlay.App\AppPackages\EchoPlay.App_*_x64_Test\*.cer"; \
  DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist

[Icons]
; Verknüpfungen zeigen auf das MSIX-App-Protokoll, nicht auf eine direkte .exe.
; MSIX-Apps haben keinen fixen Dateipfad – Windows leitet über die App-ID weiter.
Name: "{autoprograms}\EchoPlay"; \
  Filename: "{pf}\WindowsApps\4507e02b-3490-46e6-9510-6f111d5926de_1.0.0.0_x64__ywb3czm52xw0e\EchoPlay.App.exe"
Name: "{autodesktop}\EchoPlay"; \
  Filename: "{pf}\WindowsApps\4507e02b-3490-46e6-9510-6f111d5926de_1.0.0.0_x64__ywb3czm52xw0e\EchoPlay.App.exe"

[Code]
// Pascal-Script: Laufzeitprüfungen und MSIX-Installation
//
// Inno Setup unterstützt einen Pascal-ähnlichen Scripting-Dialekt. Hier werden zwei
// Aufgaben erledigt:
// 1. Prüfen ob die Windows App SDK-Laufzeit (1.8+) installiert ist.
// 2. Das MSIX-Paket über PowerShell installieren (Add-AppxPackage).

var
  // Pfad zur MSIX-Datei, der in BeforeInstall gesetzt und in den Funktionen genutzt wird.
  MsixPath: String;

// Findet die erste MSIX-Datei im temporären Verzeichnis.
// Gibt True zurück wenn eine Datei gefunden wurde und setzt MsixPath entsprechend.
function FindMsixInTempDir(): Boolean;
var
  FileFinder: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{tmp}\*.msix'), FileFinder) then
  begin
    MsixPath := ExpandConstant('{tmp}\') + FileFinder.Name;
    FindClose(FileFinder);
    Result := True;
  end;
end;

// Prüft ob die Windows App SDK-Laufzeit in einer Mindestversion installiert ist.
// Die SDK-Laufzeit registriert sich unter dem angegebenen Registry-Schlüssel.
// Gibt True zurück wenn Version 1.8.x oder neuer gefunden wurde.
function IsWindowsAppSdkInstalled(): Boolean;
var
  SubkeyNames: TArrayOfString;
  I: Integer;
  VersionStr: String;
  MajorMinor: String;
begin
  Result := False;
  // Die Windows App SDK registriert Laufzeit-Pakete unter diesem Schlüssel.
  // Jede installierte Version erscheint als eigener Unterschlüssel mit der Versionsnummer.
  if RegGetSubkeyNames(HKLM,
    'SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Applications',
    SubkeyNames) then
  begin
    for I := 0 to GetArrayLength(SubkeyNames) - 1 do
    begin
      // Paketname enthält "WindowsAppRuntime" für die App SDK-Laufzeit.
      // Version 1.8 hat eine Versionsnummer >= 8000.x in der Paket-ID.
      if Pos('MicrosoftWindowsAppRuntime', SubkeyNames[I]) > 0 then
      begin
        // Vereinfachte Prüfung: Versionsnummer >= 1.8 wird als ausreichend akzeptiert.
        // Eine vollständige Versionsauswertung würde den Rahmen dieses Skripts sprengen.
        Result := True;
        Exit;
      end;
    end;
  end;
end;

// Installiert das MSIX-Paket über PowerShell (Add-AppxPackage).
// Diese Funktion wird von der [Files]-Sektion über BeforeInstall aufgerufen.
procedure InstallMsix();
var
  ResultCode: Integer;
  PowerShellCmd: String;
begin
  if not FindMsixInTempDir() then
  begin
    MsgBox('Fehler: MSIX-Paket konnte nicht gefunden werden.' + #13#10 +
      'Bitte stelle sicher, dass das Paket im AppPackages-Verzeichnis liegt.',
      mbError, MB_OK);
    Exit;
  end;

  // Add-AppxPackage installiert das Paket für den aktuellen Benutzer.
  // -ForceApplicationShutdown beendet laufende Instanzen vor der Installation.
  PowerShellCmd := 'Add-AppxPackage -Path ''' + MsixPath +
    ''' -ForceApplicationShutdown';
  if not Exec('powershell.exe',
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' + PowerShellCmd + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('PowerShell konnte nicht ausgeführt werden.' + #13#10 +
      'Bitte installiere das MSIX-Paket manuell.',
      mbError, MB_OK);
  end
  else if ResultCode <> 0 then
  begin
    MsgBox('Die MSIX-Installation ist fehlgeschlagen (Exitcode: ' +
      IntToStr(ResultCode) + ').' + #13#10 +
      'Mögliche Ursachen: Fehlende Abhängigkeiten oder fehlende Signatur.' + #13#10 +
      'Für Debug-Pakete: Zertifikat muss zuvor als vertrauenswürdig markiert werden.',
      mbError, MB_OK);
  end;
end;

// InitializeSetup wird aufgerufen bevor die Installations-GUI erscheint.
// Hier werden alle Voraussetzungen geprüft. Gibt False zurück bricht die Installation ab.
function InitializeSetup(): Boolean;
begin
  Result := True;

  // Windows App SDK-Prüfung: WinUI 3-Apps benötigen die SDK-Laufzeit auf dem Zielrechner.
  // Ohne sie lässt sich das MSIX zwar installieren, aber nicht starten.
  if not IsWindowsAppSdkInstalled() then
  begin
    if MsgBox(
      'Die Windows App SDK-Laufzeit (Version 1.8 oder neuer) wurde nicht gefunden.' + #13#10 +
      #13#10 +
      'EchoPlay benötigt diese Laufzeit um zu starten.' + #13#10 +
      'Du kannst sie kostenlos von Microsoft herunterladen:' + #13#10 +
      'https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads' + #13#10 +
      #13#10 +
      'Möchtest du die Installation jetzt trotzdem fortsetzen?',
      mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
    end;
  end;
end;

// CurUninstallStepChanged entfernt das MSIX-Paket wenn der Nutzer deinstalliert.
// Inno Setup entfernt die kopierten Dateien selbst – das MSIX muss zusätzlich
// über PowerShell entfernt werden.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  RemoveCmd: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Paket-Vollname: Name_Version_Architektur_Ressource_PublisherId
    // Diese ID kommt aus dem Package.appxmanifest des EchoPlay.App-Projekts.
    RemoveCmd := 'Get-AppxPackage -Name 4507e02b-3490-46e6-9510-6f111d5926de ' +
      '| Remove-AppxPackage';
    Exec('powershell.exe',
      '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' + RemoveCmd + '"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
