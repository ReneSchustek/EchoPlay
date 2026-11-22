# Abgleich über einen gemeinsamen Ordner – Format

Diese Beschreibung reicht, um eine zweite Umsetzung zu schreiben, etwa für ein Handy, ohne den
C#-Code von EchoPlay zu lesen. Sie gilt für Formatversion 1.

## Ordner

Der gemeinsame Ordner liegt bei einem Cloud-Dienst oder auf einem Netzlaufwerk. Darin:

```
changes/
  <Geräte-Kennung>/
    000000000001.json
    000000000002.json
    000000000002.snapshot.json
    ...
```

- **Geräte-Kennung:** eine GUID als 32 Kleinbuchstaben-Hexziffern ohne Bindestriche. Jedes Gerät
  erzeugt sie einmal und behält sie.
- **Jedes Gerät schreibt nur in seinen eigenen Ordner.** Zwei Geräte schreiben nie dieselbe Datei;
  ein Cloud-Dienst erzeugt deshalb keine Konfliktkopien.
- **Dateinamen:** die laufende Nummer, zwölfstellig mit führenden Nullen, dann `.json` für
  Änderungen oder `.snapshot.json` für einen Gesamtstand. Die Nummer beginnt bei 1 und zählt je
  Gerät lückenlos hoch.
- **Schreiben:** erst unter dem Namen mit angehängtem `.tmp`, dann umbenennen. Leser übergehen
  `.tmp` und alle Namen, die nicht dieser Form folgen.
- **Jede Datei wird einmal geschrieben und nie verändert.**

## Datei

UTF-8 ohne BOM, ein JSON-Objekt:

```json
{
  "formatVersion": 1,
  "deviceId": "eacc19a03d0c4ae1bcb41fbcd4d9caaf",
  "sequence": 12,
  "snapshot": false,
  "records": [
    {
      "kind": "PlaybackState",
      "key": "online:apple:123|apple:501",
      "parent": "online:apple:123|apple:501",
      "stamp": "001795176000007.000000000.eacc19a03d0c4ae1bcb41fbcd4d9caaf",
      "deleted": false,
      "fields": {
        "CompletedAt": null,
        "IsCompleted": "true",
        "LastPlayedAt": "2026-11-20T12:05:00.0000000Z",
        "LastPosition": "00:10:00"
      }
    }
  ]
}
```

| Feld | Bedeutung |
|---|---|
| `formatVersion` | 1. Eine andere Version wird nicht gelesen. |
| `deviceId` | Das schreibende Gerät; muss zum Ordner passen. |
| `sequence` | Die laufende Nummer; muss zum Dateinamen passen. |
| `snapshot` | `true` für einen Gesamtstand. |
| `records` | Die Sätze, höchstens 1.000.000. |

Ein **Gesamtstand** enthält alle Sätze des Geräts und trägt die Nummer der letzten Änderung, die er
einschließt. Danach darf das Gerät seine älteren Dateien löschen, nie aber Dateien anderer Geräte.

## Satz

| Feld | Bedeutung |
|---|---|
| `kind` | Die Art, siehe unten. Unbekannte Arten machen die Datei ungültig. |
| `key` | Der fachliche Schlüssel, auf allen Geräten gleich, höchstens 2.048 Zeichen. |
| `parent` | Schlüssel des übergeordneten Satzes, sonst leer. |
| `stamp` | Logischer Zeitpunkt der letzten Änderung, siehe unten. |
| `deleted` | Ob der Datensatz gelöscht ist (Grabstein). |
| `fields` | Die gemeinsamen Felder, Name und Wert als Text oder `null`. |

## Zeitpunkt

Eine hybride logische Uhr: Wanduhr in Millisekunden seit 1970 (UTC), ein Zähler für Ereignisse in
derselben Millisekunde und die Geräte-Kennung:

```
001795176000007.000000000.eacc19a03d0c4ae1bcb41fbcd4d9caaf
└─ 15 Stellen ─┘ └ 9 St. ┘ └──────── Geräte-Kennung ───────┘
```

Verglichen wird erst die Wanduhr, dann der Zähler, dann die Kennung als Text. Weil alle Teile mit
fester Breite stehen, ist der Textvergleich (ordinal) gleich dem Zeitvergleich. Wer einen fremden
Zeitpunkt liest, stellt seine Uhr mindestens darauf; so wird eine falsch gehende Uhr nicht zum
Problem. Ein Satz ohne bekannten Zeitpunkt trägt
`000000000000000.000000000.00000000000000000000000000000000`.

## Arten und Schlüssel

| Art | Schlüssel | Übergeordnet |
|---|---|---|
| `Series` | `online:apple:<Künstler>`, `online:spotify:<Künstler>`, `online:title:<Titel>` oder `local:title:<Titel>` | – |
| `Episode` | Schlüssel der Serie, dann `\|apple:<Album>`, `\|spotify:<Album>`, `#<Nummer>` oder `\|title:<Titel>` | Serie |
| `PlaybackState` | Schlüssel der Folge | Folge |
| `WatchedTitle` | der normalisierte Titel | – |
| `DialogSuppression` | Name des Hinweises | – |
| `Settings` | `settings` | – |
| `DashboardOrder` | Abschnitt der Startseite | – |

Titel werden vor dem Schlüssel normalisiert: in Kleinbuchstaben (sprachunabhängig), Umlaute in
ihre ASCII-Umschrift (Grundbuchstabe plus e, ß als doppeltes s), danach wird alles entfernt, was
nicht `a`–`z`, `0`–`9` oder Leerraum ist. Leerraum bleibt, wie er ist. `DashboardOrder` hat genau ein Feld `Order` mit den Serien-Schlüsseln in Reihenfolge, je
Zeile einer (`\n`).

## Feldwerte

| Typ | Form |
|---|---|
| Wahrheitswert | `true` oder `false` |
| Ganzzahl | Ziffern, optional mit `-`, ohne Gruppierung |
| Kommazahl | mit Punkt, Rundreise-Form |
| Zeitspanne | `[-][d.]hh:mm:ss[.fffffff]` |
| Zeitpunkt | ISO 8601, Rundreise-Form (`2026-11-20T12:05:00.0000000Z`) |
| Aufzählung | der Name des Werts |
| leer | `null` |

Ein Feld, das ein Gerät nicht kennt, bleibt dort unverändert. Felder, die nur für ein Gerät gelten
(Pfade, Lautstärke, Zugangsdaten), und Cover stehen nie im Protokoll.

## Zusammenführen

Jedes Gerät führt alle Stände eines Datensatzes (gleiche Art, gleicher Schlüssel) zusammen. Die
Regeln sind kommutativ, assoziativ und idempotent; Reihenfolge und doppeltes Lesen ändern nichts.

- **Grundregel:** Der Stand mit dem jüngsten Zeitpunkt gewinnt ganz, Löschung eingeschlossen. Bei
  gleichem Zeitpunkt entscheidet der größere Text des ganzen Satzes (ordinal).
- **`PlaybackState`:** Felder und Löschung vom jüngsten Stand, aber:
  - `LastPosition` und `LastPlayedAt` vom Stand mit dem jüngsten `LastPlayedAt`, bei Gleichstand der
    größeren `LastPosition`;
  - `IsCompleted` ist `true`, sobald ein Stand `true` sagt;
  - `CompletedAt` ist dann der früheste Wert unter den Ständen mit `IsCompleted` `true`.
- **`DashboardOrder`:** die ganze Liste des jüngsten Stands.

## Lesen

Je fremdem Gerät merkt sich ein Gerät die höchste übernommene Nummer. Gelesen wird danach:

1. der jüngste Gesamtstand mit höherer Nummer, falls es einen gibt;
2. danach die Änderungen ohne Lücke. An einer Lücke wird angehalten; die fehlende Datei wird
   vielleicht noch übertragen.

Eine Datei, die sich nicht lesen lässt, abgeschnitten ist, nicht zu Ordner oder Namen passt oder
gegen eine Grenze verstößt, hält das Lesen dieses Geräts an. Der Bestand bleibt unberührt, und beim
nächsten Lauf wird es erneut versucht.

## Grenzen

| Was | Höchstens |
|---|---|
| Datei | 256 Mio. Zeichen |
| Sätze je Datei | 1.000.000 |
| Schlüssel | 2.048 Zeichen |
| Feldname | 64 Zeichen |
| Feldwert | 65.536 Zeichen |
