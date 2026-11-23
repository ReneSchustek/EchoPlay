# Änderungsverlauf

Alle nennenswerten Änderungen an EchoPlay stehen hier.
Das Format folgt [Keep a Changelog](https://keepachangelog.com/de/1.1.0/),
die Versionierung [Semantic Versioning](https://semver.org/lang/de/).

## [1.6.1] — 2026-11-23

### Geändert

- Abhängigkeiten aktualisiert: Windows App SDK 2.7, Microsoft.Extensions 11 (unter .NET 10),
  Entity Framework Core 10.0.15. Entity Framework Core 11 setzt .NET 11 voraus und folgt erst
  mit dem Wechsel der Laufzeit.

### Behoben

- Zwei Tests schlugen gelegentlich fehl: der Test-Logger war nicht threadsicher, und der
  Zeittreiber der HTTP-Tests drehte die Uhr weiter, während die Pipeline noch rechnete.

## [1.6.0] — 2026-11-22

### Neu

- Seite „Angekündigt“ unter der Startseite: alle angekündigten Folgen der vorhandenen Serien,
  nach Monat geordnet. Die Neuerscheinungen der Startseite zeigen nur noch Erschienenes, und der
  Veröffentlichungskalender zeigt die Folgen des Monats.
- Daten exportieren und auf einem anderen Rechner einspielen: ein Paket mit Prüfsummen, die
  Zugangsdaten durch ein Kennwort geschützt. Eingespielt wird beim nächsten Start, vorher legt
  EchoPlay eine Sicherung an; was nur zum Zielrechner gehört, bleibt erhalten.
- Mediathek packen und auspacken: ein Archiv mit Prüfsumme je Datei, ausgepackt nur in einen
  leeren Ordner, mit neuem Stammpfad beim Einspielen.
- Abgleich zweier Rechner über einen gemeinsamen Ordner, im Hintergrund und in den Einstellungen.
- Titel, Nummer und Jahr einer Folge oder Serie lassen sich in der lokalen Mediathek bearbeiten;
  die Änderung landet auch in den Tags der Dateien und im Ordnernamen.
- Folgenordner lassen sich nach ihren Tags umbenennen, mit Vorschau vor dem Umbenennen.
- Stopp-Taste im Mini-Player und auf der Player-Seite.
- Der Bericht „Fehlende Folgen“ nennt nur Serien mit Befund und kennt auch die neu erschienenen
  Folgen.

### Geändert

- EchoPlay installiert sich unter „Programme“ statt im Benutzerprofil.
- Die Schriften der Farbschemata werden mitgeliefert; ohne installierte Schrift fiel jedes Schema
  bisher auf die Systemschrift zurück.
- Die Cover-Suche fragt Discogs nicht mehr ab; ohne Zugangsschlüssel lieferte es keine Bilder.
- „5 Freunde“ und „Fünf Freunde“ gelten bei Apple Music und Spotify als derselbe Name.
- Cover und Folgenzahlen laden spürbar schneller, das zweite Einlesen einer großen Sammlung
  braucht Sekunden statt Minuten.
- Die Sprungleiste der Mediathek folgt der gewählten Sortierung.
- Kommentare im ganzen Quelltext überarbeitet: Jeder Typ erklärt, wofür es ihn gibt, gewählte
  Zahlen nennen ihren Grund, und einige Beschreibungen, die nicht zum Code passten, sind
  berichtigt. Am Verhalten der Anwendung ändert sich nichts.

### Behoben

- Eine Folge mit gespeicherter Stelle in einer späteren Spur startete auf schnellen Rechnern mit
  „unerwartetem Fehler“; sie setzt jetzt an der gespeicherten Stelle fort.
- Zwei gleichzeitige Starts brechen nicht mehr mit „EchoPlay konnte nicht starten" ab. Ein
  zweiter Start holt jetzt das schon offene Fenster nach vorn und beendet sich, statt
  denselben Zwischenspeicher aufzuräumen wie der erste. Scheitert das Aufräumen des
  Zwischenspeichers trotzdem einmal, startet die Anwendung weiter.
- Beim Beenden während eines laufenden Kalenderaufbaus erscheint keine Warnung mehr im Protokoll.
- Die Player-Seite gibt ihren Speicher beim Verlassen frei.
- Folgen auf mehreren CDs und neue Folgenordner bekannter Serien werden beim Einlesen gefunden;
  Sonderfolgen verdecken keine regulären Nummern mehr.
- Der Filter „unvollständig“ findet wieder Serien.
- Zeitgrenzen der Anfragen an die Anbieter wirken; die Aktualisierungsprüfung beim Start hat
  eine eigene, kurze Frist.
- Spotify wiederholt eine abgelehnte Anfrage nicht mehr mehrfach übereinander, Spurlisten werden
  nicht nach 20 Einträgen abgeschnitten, und ein Serien-Cover kommt nicht mehr aus einer fremden
  Sammlung.
- Fehler, die bisher still geschluckt wurden, stehen im Protokoll; alte JSON-Protokolle werden
  wie die übrigen bereinigt.

### Sicherheit

- Updates werden nur noch installiert, wenn die Setup-Datei die Signatur des Herausgebers
  trägt. Bisher genügte es, dass sie zum Prüfwert im Release-Text passte; wer das Release
  selbst austauschen konnte, hätte Datei und Prüfwert gemeinsam ersetzen können. Die Signatur
  liegt als `.sig` neben der Setup-Datei. Ein Release ohne gültige Signatur wird nicht angeboten.
- Die geladene Setup-Datei bleibt von der Prüfung bis zum Start des Installers gegen Schreiben
  gesperrt, damit sie dazwischen nicht ausgetauscht werden kann.

## [1.5.3] — 2026-09-01

### Behoben

- Neue Folgen kommen in der Online-Mediathek wieder an. Der Anbieter liefert je Abfrage
  höchstens 200 Alben und beginnt bei den ältesten — bei langen Serien fehlten damit
  ausgerechnet die neuen Folgen. „Die drei ???" endete bei Folge 230 vom November 2024,
  während die Startseite längst Folge 240 meldete. Für die betroffenen Serien holt die
  nächste Aktualisierung mehrere hundert fehlende Folgen nach.
- Folgen stehen nur noch einmal in der Liste. Das Nachladen einer Serie legte bisher jede
  Folge des Anbieters neu an, statt nur die fehlenden; startete es neben einem laufenden
  Import, stand anschließend der ganze Bestand doppelt da. Vorhandene Doppel räumt die
  Datenbank beim nächsten Start selbst weg — Gehört-Markierungen und Cover bleiben erhalten.
- Ganze Künstler lassen sich aus der Suche wieder hinzufügen. Wer eine Serie lokal in der
  Bibliothek hatte, bekam sie als „bereits vorhanden" gemeldet und konnte sie nicht in die
  Online-Mediathek holen — und damit auch nicht auf Neuerscheinungen überwachen. Einzelne
  Folgen waren davon nicht betroffen, ganze Künstler schon.

## [1.5.2] — 2026-08-26

### Geändert

- Kommentare im Quelltext der Suche-Seite auf das heutige Suchfeld bezogen.
  Keine Änderung am Verhalten des Programms.

## [1.5.1] — 2026-08-26

### Behoben

- Die Suche beim Anbieter zeigt ihre Treffer wieder. Bisher blieb die Seite leer, bis der
  letzte Künstler bewertet war — bei einem Suchbegriff wie „Bibi" konnte das Minuten dauern.
  Jetzt erscheint jede Kachel, sobald ihr Treffer feststeht, und die erste steht nach gut
  einer Sekunde.
- Suchtreffer zeigen ihr Cover. Bei einzelnen Folgen kommt es vom Anbieter, bei Serien aus
  deren erstem Album, und bei Treffern aus der eigenen Bibliothek aus der Datenbank.
- Wer während einer laufenden Suche weitertippt, findet im Protokoll keine Fehlermeldungen
  mehr über Abbrüche, die keine Fehler waren.

### Geändert

- Die Suche prüft nur noch Künstler, die namentlich oder vom Genre her infrage kommen;
  bekannte Serien werden gar nicht erst geprüft. Das kürzt die Wartezeit erheblich.
- Coverbilder laden nebeneinander statt hintereinander — die Bilder einer Trefferseite
  stehen damit gemeinsam statt nacheinander im Sekundentakt.

## [1.5.0] — 2026-08-24

### Hinzugefügt

- Jeder Hinweis und jede Rückfrage lässt sich über ein Häkchen dauerhaft ausblenden.
  Bei Rückfragen wird nur eine Zustimmung gemerkt — wer abbricht, wird beim nächsten Mal
  wieder gefragt. Unerwartete Fehler und ein fehlgeschlagener Start bleiben immer sichtbar.
- Einstellungen → Verwaltung listet die ausgeblendeten Hinweise mit Datum und holt sie
  einzeln oder gesammelt zurück.

## [1.4.2] — 2026-08-23

### Geändert

- Das Anwendungsrahmenwerk ist eine Nebenversion weiter. Am Verhalten des Programms
  ändert sich dadurch nichts; die Fassung wird nachgezogen, solange kein Grund dagegen
  spricht, und nicht erst, wenn ein Fehler dazu zwingt.

### Sicherheit

- Vor jeder Auslieferung wird der Quellbaum wieder auf versehentlich enthaltene
  Zugangsdaten durchsucht. Die Prüfung war zwischenzeitlich aus dem Ablauf gefallen,
  ohne dass es auffiel.

## [1.4.1] — 2026-08-13

### Behoben

- Der Abspielen-Knopf auf der Player-Seite trug keinen Namen und war damit für eine
  Sprachausgabe stumm — als einziger der Bedienelemente dort.

### Geändert

- Innerer Aufbau: Die Zeitanzeige stand zweimal im Programm, einmal je Wiedergabe-Ansicht.
  Beide rechneten Position, Restzeit und Fortschritt der Folge getrennt voneinander aus.
  Jetzt gibt es eine Rechnung. Ebenso getrennt sind nun das Speichern des Hörstands, der
  Einschlaf-Zeitgeber, das Einlesen der Mediathek und die Suche beim Anbieter — die
  Wiedergabe steuert nur noch das Abspielgerät.

## [1.4.0] — 2026-08-13

### Hinzugefügt

- **Ein Lautstärkeregler in beiden Playern**, mit Stummschaltung. Der eingestellte
  Wert überlebt den Neustart.
- **Fortschrittsbalken für die ganze Folge**, zusätzlich zur Position in der
  laufenden Datei. Er erscheint nur, wenn eine Folge aus mehreren Dateien besteht — sonst
  zeigte er dasselbe wie der Regler darüber.

- Ein Doppelklick auf eine Folge in der lokalen Mediathek startet sie — an der Stelle, an der
  zuletzt aufgehört wurde. Ein Klick auf den Mini-Player unten führt auf die Player-Seite, die
  die laufende Wiedergabe unverändert weiterführt.

### Behoben

- In der lokalen Mediathek erscheint die Spurenliste wieder, sobald eine Folge angeklickt wird.
  Sie blieb bisher aus, ebenso der Knopf zum Abspielen der Folge.

- Schaltflächen tragen jetzt die Farben der gewählten Farbwelt. Bisher kamen Fläche, Rand und
  Beschriftung aus den Windows-Vorgaben; in einigen Farbwelten war eine Schaltfläche vom
  Hintergrund kaum zu unterscheiden. Auch der bestätigende Knopf in Dialogen und der aktive
  Filter folgen nun der Farbwelt statt der Akzentfarbe des Systems.
- Das Suchfeld zeigt jetzt, wo die Tastatur steht: Bisher änderte sich beim Hineinspringen
  nichts am Feld. Auch der ruhende Rahmen ist deutlicher — er war in allen Farbwelten kaum vom
  Hintergrund zu unterscheiden.

- **Eine erneut begonnene Folge steht wieder unter „Angefangen“**, auch wenn sie
  schon als gehört markiert ist. Bisher verschwand sie dort — ein Hörspiel hört man nicht
  nur einmal.
- **Die Wiedergabe setzt an der richtigen Stelle fort.** Gespeichert wird jetzt die Stelle
  in der Folge statt in der laufenden Datei; wer bei Spur 3 aufhörte, begann bisher wieder
  bei Spur 1.
- **Die Folgen einer Serie erscheinen sofort statt nach Sekunden.** Bei einer Serie mit 399
  Folgen dauerte es rund siebzehn Sekunden, bis überhaupt etwas zu sehen war; jetzt steht
  die Liste nach einer halben Sekunde, die Cover kommen währenddessen nach.
- Im Mini-Player überschnitten sich die Bedienknöpfe mit dem Wiedergabe-Balken.
- Der Balken für den Folgenfortschritt trägt jetzt eine Beschriftung — ohne sie sah er
  aus wie ein zweiter Positionsregler.

### Entfernt

- Die zweite, nie erreichbare Protokollansicht ist weg. Das Protokoll steht wie gewohnt in den
  Einstellungen unter „Protokolle" — dort mit Auswahl des Tages, Suche und Ausnahmedetails.

- Die Spurenliste zeigt jetzt den Titel aus der Kennzeichnung der Datei statt des Dateinamens.
  Bisher stand dort der komplette Dateiname samt Endung und samt der Nummer, die daneben ohnehin
  in eigener Spalte steht. Führt eine Datei keinen Titel, bleibt ihr Dateiname stehen — ohne
  Endung und ohne die doppelte Nummer. Das gilt in der Serienansicht, der lokalen Mediathek und
  der Wiedergabeliste des Players; der Mini-Player unten zeigt denselben Titel.

## [1.3.0] — 2026-07-30

### Hinzugefügt

- Die Cover-Suche kann weitere Treffer nachladen. Als letzter Eintrag im Raster steht „Weitere
  Ergebnisse laden"; bleibt eine Runde ohne neue Treffer, verschwindet er wieder.

### Behoben

- Eine Folge, die in beiden Quellen vorliegt, gilt jetzt in beiden als gehört. Bisher hing der
  Hörstatus an einer Quelle: Wer lokal hörte, sah die Folge in der Online-Ansicht weiter als
  offen. Bestehende Abweichungen werden beim Start einmalig nachgezogen.
- Ein fehlgeschlagenes Update sagt es jetzt. Bisher blieb der Klick auf „Jetzt aktualisieren"
  ohne sichtbare Wirkung, wenn die Prüfung der Setup-Datei fehlschlug — der Grund stand nur im
  Protokoll.
- Eine Version, deren Release keinen Prüfwert mitbringt, wird nicht mehr als Update angeboten.
  Bisher wurde sie angeboten und erst beim Klick abgelehnt.

### Geändert

- Der Prüfwert der Setup-Datei steht nicht mehr im Update-Hinweis. Die 64 Zeichen haben den
  eigentlichen Text verdrängt.

## [1.2.1] — 2026-07-30

### Behoben

- Fehlende Cover werden jetzt auch nachträglich gesucht. Bisher lief die Online-Suche
  ausschließlich beim Import — wessen Suche damals scheiterte oder wer eine Serie vor
  Einführung der Suche importiert hat, wartete vergeblich. Der Hintergrunddienst holt das
  jetzt nach, für Folgen und für Serien.
- Serien-Cover werden ebenfalls gesucht. Zuvor trug die App fehlende Cover-Adressen nur für
  Folgen nach, weshalb eine Serie ohne eigenes Bild dauerhaft ohne blieb.
- Erfolglose Cover-Suchen wiederholen sich nicht mehr bei jedem Durchlauf. Ein Eintrag ohne
  Treffer wird erst nach einer Woche erneut geprüft — das schont die Anbieter und den
  Akku.
- Leere Antworten eines Anbieters landen nicht mehr als Cover in der Bibliothek. Bisher
  konnte ein Bild mit null Byte gespeichert werden, wodurch die Kachel weder ein Bild noch
  den Platzhalter zeigte und beim Nachladen als erledigt galt.
- Rückfragen der App scheitern nicht mehr, wenn das Hauptfenster noch nicht steht. In den
  Startfehler-Pfaden konnte das Nachfragen abbrechen, statt zu erscheinen; jetzt wird die
  Meldung protokolliert und die Rückfrage als „abgebrochen" behandelt.
- Der Verbindungstest meldet fehlende Anbieter-Einstellungen auf Deutsch. Der Hinweistext
  war noch englisch geblieben.
- Die Suche über dem Protokoll wirkt jetzt beim Tippen. Bisher zeigte die Liste erst nach
  einem Klick auf „Aktualisieren" das gefilterte Ergebnis.
- Ein abgebrochener Cover-Lauf erscheint nicht mehr als Warnung im Protokoll. Wer eine Seite
  verlassen hat, hat keinen Fehler ausgelöst.

## [1.2.0] — 2026-07-29

### Hinzugefügt

- Das Album einer Folge lässt sich direkt in Spotify öffnen. Ist die Spotify-App
  installiert, wird sie statt des Browsers gestartet — dort ist der Nutzer bereits
  angemeldet. Ohne hinterlegte Album-ID öffnet sich die Suche mit Serie und Folge.
- Das Album einer Folge lässt sich ebenso in Apple Music öffnen.

### Geändert

- Mengenangaben werden richtig gebeugt. Rückfragen im Tag-Manager, die Trefferzahl der
  Cover-Suche, der Serien- und Statistik-Fortschritt, die Statusleiste, die Track-Anzahl
  und die Meldungen zu fehlenden Folgen unterscheiden jetzt Einzahl und Mehrzahl statt
  „1 Dateien" oder „1 fehlende Folge(n)" zu schreiben.
- Die Meldungen der Prüfung auf fehlende Folgen und die Fortschrittstexte des
  Bibliotheks-Abgleichs kommen aus den Sprachdateien; auf englischer Oberfläche standen
  sie bisher auf Deutsch.
- Die Merkliste überwachter Serientitel liegt in einem eigenen Datenservice
  (`IWatchedTitleDataService`) statt im Serien-Service.
- Windows App SDK auf 2.3.1, EF Core und `Microsoft.Extensions.*` auf 10.0.10,
  Test-SDK auf 18.8.1.

### Behoben

- Fehlende lokale Folgen werden zuverlässig erkannt: Die Lückensuche beginnt bei der
  kleinsten tatsächlich vorhandenen Folgennummer statt bei 1 und wertet alle
  Nummernmuster eines Ordnernamens aus. In der Testbibliothek sank die Zahl gemeldeter
  Lücken dadurch von 2978 auf 78 — die verbliebenen sind echte Lücken.
- Das Protokoll zeigt seine Einträge sofort beim Öffnen der Registerkarte; bisher blieb
  die Anzeige bis zum ersten Klick auf „Aktualisieren" leer.
- 35 Schaltflächen mit reinem Symbol haben einen vorlesbaren Namen bekommen. Ein Tooltip
  allein erreicht Screenreader nicht. Der Sprachumschalter in der Statusleiste war die
  letzte namenlose Schaltfläche und hat seinen Namen ebenfalls bekommen.
- Ist die Protokollanzeige leer, steht das jetzt dort. Bisher war eine leere Anzeige nicht
  von einer defekten zu unterscheiden.
- Beim gleichzeitigen Schreiben in die Merkliste (Startabgleich und Favoriten-Klick)
  konnte eine Datenbankmeldung bis in die Oberfläche durchschlagen.

### Sicherheit

- Ein Update wird nur noch installiert, wenn im Release-Text ein SHA-256-Hash steht und
  die heruntergeladene Datei dazu passt. Bisher lief die Installation ohne Prüfung weiter,
  wenn der Hash fehlte. Da das Setup nicht signiert ist, war der Hash die einzige
  Kontrolle — sie ließ sich durch Weglassen der Zeile umgehen. Geprüft wird jetzt vor dem
  Download, ein Release ohne Hash kostet keine Übertragung mehr.
- Die Download-Adresse eines Updates muss über HTTPS auf einen GitHub-Release-Host zeigen.
  Bisher wurde die Adresse aus der Release-Antwort ungeprüft übernommen und das Ergebnis
  als Programm gestartet.
- Spotify-Zugangsdaten werden mit anwendungsspezifischer Entropie verschlüsselt. Ein
  fremdes Programm im selben Benutzerkonto kann sie nicht mehr durch einen blanken
  Entschlüsselungsaufruf lesen. Vorhandene Zugangsdaten bleiben nutzbar und werden beim
  ersten Zugriff umgeschlüsselt.
- Enthält eine Adresse Zugangsdaten (`https://name:kennwort@host/…`), erscheinen sie im
  Protokoll als `***`. Bisher standen sie dort im Klartext.
- Der Start der Setup-Datei läuft über eine eigene Schnittstelle. Bisher startete ein
  Test, der den Erfolgsfall prüfte, tatsächlich eine Datei aus dem Temp-Verzeichnis.

## [1.1.0] — 2026-07-27

### Hinzugefügt

- Eine Serie zu favorisieren aktiviert zugleich die Überwachung auf Neuerscheinungen.
- Die Überwachung überlebt das Leeren der Mediathek: Gemerkte Titel geben neu
  eingelesenen Serien ihre Überwachung zurück.
- Die Startseite zieht neu gefundene Folgen ohne Neustart nach.
- Die Sprache lässt sich zwischen Deutsch und Englisch umschalten.

### Behoben

- Die Sprachumschaltung wirkt auch in der Unpackaged-Installation.
- Das Auge auf der Kachel zieht beim Favorisieren mit.
- Schaltflächen auf Kacheln nennen ihre Funktion statt nur den Serientitel.
- Bedienelemente haben zugängliche Namen, Gruppenüberschriften sind übersetzt.
- Der Import neuer Online-Serien ist durch Sammelabfragen bei Apple deutlich schneller.
- Die Statusanzeige zeigt den tatsächlich nutzbaren Anbieter.
- Die lokale Suche findet Serien auch dann, wenn die Online-Abfrage scheitert.
- Serien-Cover werden wieder geladen.

## [1.0.7] — 2026-07-21

### Behoben

- Serien-Kacheln ziehen ihren Folgenzähler während des Einlesens laufend nach.

## [1.0.6] — 2026-07-21

### Behoben

- Der Abgleich neuer Folgen weicht auf Apple aus, wenn keine Spotify-Zugangsdaten
  hinterlegt sind.
- Die gewählte Folgen-Sortierung bleibt über Programmstarts erhalten.

## [1.0.5] — 2026-07-21

### Geändert

- Folgen werden nach Nummer sortiert.
- Der Abgleich auf neue Folgen läuft mit deutlich weniger Abfragen.

## [1.0.4] — 2026-07-21

Erste offizielle Version nach der Umstellung auf ein selbstinstallierendes Setup
ohne Paket-Identität (Windows 10/11, x64).

[1.5.0]: https://github.com/ReneSchustek/EchoPlay/compare/v1.4.2...v1.5.0
[1.4.2]: https://github.com/ReneSchustek/EchoPlay/compare/v1.4.1...v1.4.2
[1.4.1]: https://github.com/ReneSchustek/EchoPlay/compare/v1.4.0...v1.4.1
[1.4.0]: https://github.com/ReneSchustek/EchoPlay/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/ReneSchustek/EchoPlay/compare/v1.2.1...v1.3.0
[1.2.1]: https://github.com/ReneSchustek/EchoPlay/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/ReneSchustek/EchoPlay/releases/tag/v1.2.0
[1.1.0]: https://github.com/ReneSchustek/EchoPlay/releases/tag/v1.1.0
[1.0.7]: https://github.com/ReneSchustek/EchoPlay/releases/tag/v1.0.7
[1.0.6]: https://github.com/ReneSchustek/EchoPlay/releases/tag/v1.0.6
[1.0.5]: https://github.com/ReneSchustek/EchoPlay/releases/tag/v1.0.5
[1.0.4]: https://github.com/ReneSchustek/EchoPlay/releases/tag/v1.0.4
