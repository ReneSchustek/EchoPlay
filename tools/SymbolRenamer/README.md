# SymbolRenamer

Benennt Bezeichner über eine ganze Projektmappe um — Typen, Member, Parameter und lokale
Namen. Dahinter steckt dieselbe Maschinerie, die die Entwicklungsumgebung hinter „Symbol
umbenennen" benutzt: Referenzen in allen geladenen Projekten wandern mit, **Kommentare,
Zeichenketten und Ressourcenschlüssel bleiben unberührt**. Es ist kein Textersatz.

Genau darin liegt der Wert. Ein Suchen-und-Ersetzen über den Baum trifft auch das Wort im
Kommentar, den Schlüssel in der Sprachdatei und den Namen, der zufällig gleich lautet.

## Aufruf

```powershell
dotnet build tools\SymbolRenamer -c Release
tools\SymbolRenamer\bin\Release\net10.0\SymbolRenamer.exe <projektwurzel> <auftrag.json> --dry-run
```

Ohne `--dry-run` wird geschrieben. **Der Trockenlauf ist keine Formsache** — siehe unten.

## Auftragsdatei

Einzelne Umbenennungen, jede ausdrücklich benannt:

```json
{
  "projects": ["src/EchoPlay.App/EchoPlay.App.csproj"],
  "renames": [
    { "kind": "type",   "container": null,          "from": "SuchePage",  "to": "SearchPage" },
    { "kind": "member", "container": "SearchPage",  "from": "IstOffen",   "to": "IsOpen" }
  ]
}
```

Oder segmentweise über ein Wörterbuch, wenn es um viele Namen auf einmal geht:

```json
{
  "projects": ["src/EchoPlay.App/EchoPlay.App.csproj"],
  "segmentDictionary": "tools/SymbolRenamer/glossar-segmente.txt",
  "localProjects": ["EchoPlay.App"],
  "autoMembers": true,
  "parameters": true
}
```

Das Wörterbuch ordnet **Segmente** zu, nicht ganze Namen: `eigeneVerweisQuellen` zerfällt in
`eigene|Verweis|Quellen` und wird zu `ownReferenceSources`. Format ist `deutsch=Englisch`,
eine Zeile je Segment, `#` leitet einen Kommentar ein.

## Der Trockenlauf ist der eigentliche Arbeitsschritt

Ein Segment ohne Eintrag bleibt stehen. Das ist die richtige Voreinstellung — es führt aber
zu **halb übersetzten Namen**, und die sind schlechter als der Ausgangszustand. Beim ersten
Lauf dieses Werkzeugs auf sich selbst kam unter anderem heraus:

| Vorschlag | Was fehlt |
|---|---|
| `SammleUmzubenennendeSymbole` → `CollectUmzubenennendeSymbole` | `umzubenennende` |
| `VorlagenSammeln` → `TemplatesSammeln` | `sammeln` |
| `LadeWoerterbuch` → `LoadWoerterbuch` | `wörterbuch` |
| `EigenschaftZu` → `PropertyToo` | falscher Eintrag: `zu=Too` passt hier nicht |

Der letzte Fall ist der lehrreiche: Ein vorhandener Eintrag kann sachlich falsch sein, und
das Werkzeug merkt es nicht. **Die Liste vor dem echten Lauf durchgehen, nicht überfliegen** —
fehlende Segmente ergänzen, unpassende Einträge vorher aus dem Wörterbuch nehmen.

## Was das Werkzeug nicht kann

- **Dateien umbenennen.** Eine Klasse `SuchePage` in `SuchePage.xaml.cs` wird im Quelltext
  umbenannt; die Datei behält ihren Namen. Das gehört von Hand nachgezogen, bei XAML-Seiten
  zusammen mit der `.xaml`-Datei daneben.
- **Ressourcenschlüssel mitziehen.** `x:Uid="NavUeber"` holt seinen Text über `NavUeber.Content`
  aus den Sprachdateien. Wird der Bezeichner umbenannt, bleibt der Schlüssel unangetastet —
  richtig so, aber wer den Schlüssel selbst ändern will, ändert ihn in **allen** Sprachdateien
  von Hand.
- **Bindungen prüfen.** `{Binding …}` löst zur Laufzeit auf. Ein umbenanntes Merkmal bindet
  danach still ins Leere. Nach jedem Lauf gehört das Programm gestartet, nicht nur gebaut.
