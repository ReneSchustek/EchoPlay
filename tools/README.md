# tools/

Werkzeuge, die nicht als NuGet-Paket kommen.

## SymbolRenamer/

Benennt Bezeichner über eine ganze Projektmappe um (Typen, Member, Parameter und lokale
Namen), ohne Kommentare, Zeichenketten und Ressourcenschlüssel anzufassen. Aufträge kommen als
JSON-Datei, `--dry-run` zeigt vorher, was passieren würde: siehe `SymbolRenamer/README.md`.

## Xunit1051Fixer/

Übergibt in den Testprojekten `TestContext.Current.CancellationToken` überall dort, wo ein Aufruf
ein Token annimmt und keines bekommt (Meldung xUnit1051). Das erste Argument ist der Ordner mit
den Testprojekten, das zweite die gespeicherte Build-Ausgabe mit den Meldungen; sie wird nur
gezählt. Geändert wird über Roslyn:

```powershell
dotnet build EchoPlay.slnx -c Release > build.txt
dotnet run --project tools\Xunit1051Fixer -- tests build.txt
```

## Messprojekt (`tests/EchoPlay.Benchmarks`)

Kein Werkzeug in diesem Ordner, aber hier beschrieben, weil es wie eines aufgerufen wird:

```powershell
dotnet run -c Release --project tests\EchoPlay.Benchmarks -- --filter '*' --artifacts <Ordner>
```

`--filter` wählt die Messklassen (`*Query*`, `*Scan*`, `*Text*`, `*Cover*`, `*Migration*`, `*Log*`),
`--artifacts` den Ablageort der Berichte; ohne ihn schreibt BenchmarkDotNet nach
`BenchmarkDotNet.Artifacts` im aktuellen Ordner. Die Werte gehören mit Datum, Fassung und
Rechnername in die Ablage der Messreihe; Zahlen verschiedener Rechner sind nicht vergleichbar.

Die Cover-Messungen legen eine Datei in der Größe der Produktivdatenbank an, rund 1,3 GB im
Temp-Ordner; sie wird nach dem Lauf gelöscht.

```powershell
dotnet run -c Release --project tests\EchoPlay.Benchmarks -- --plans <Datei.md>
```

misst nichts, sondern schreibt zu jeder gemessenen Abfrage den Plan, den SQLite wählt, als Markdown.
Der Plan ist ein Beleg, wo gesucht wird; wie lange es dauert, sagt nur die Messung.

## demo-data/

Baut eine Demo-Bibliothek aus gemeinfreien Titeln und nimmt daraus Bilder der Anwendung auf,
für Webseite, Tutorials und Release-Notizen, ohne die private Sammlung zu zeigen. Fünf
Schritte mit eigener Anleitung: siehe `demo-data/README.md`.

## gitleaks

Secret-Scanner (nutzt Shannon-Entropie zur Erkennung hochentroper Strings).
Binary ist via `*.exe` gitignored, daher vor dem ersten Lauf installieren:

```powershell
pwsh tools/install-gitleaks.ps1
```

Lokaler Aufruf:

```powershell
pwsh tools/secret-scan.ps1
pwsh tools/secret-scan.ps1 -History
```

`pwsh` und nicht `powershell`: Die Skripte hier sind UTF-8 ohne BOM, wie alle Dateien im
Repository. Windows PowerShell 5.1 liest sie deshalb als ANSI, und aus einem Gedankenstrich
in einer Ausgabezeile wird ein typografisches Anführungszeichen, und die Zeile bricht mitten
im String ab. PowerShell 7 liest UTF-8 auch ohne BOM richtig.

CI nutzt stattdessen die `gitleaks-action` (siehe `.github/workflows/security.yml`),
braucht das lokale Binary nicht.
