# tools/

Portable Werkzeuge die NICHT als NuGet-Pakete kommen.

## gitleaks

Secret-Scanner (nutzt Shannon-Entropie zur Erkennung hochentroper Strings).
Binary ist via `*.exe` gitignored — vor dem ersten Lauf installieren:

```powershell
pwsh tools/install-gitleaks.ps1
```

Lokaler Aufruf:

```powershell
pwsh tools/secret-scan.ps1
pwsh tools/secret-scan.ps1 -History
```

`pwsh` und nicht `powershell`: Die Skripte hier sind UTF-8 ohne BOM, wie alle Dateien im
Repository. Windows PowerShell 5.1 liest sie deshalb als ANSI — aus einem Gedankenstrich
in einer Ausgabezeile wird ein typografisches Anführungszeichen, und die Zeile bricht mitten
im String ab. PowerShell 7 liest UTF-8 auch ohne BOM richtig.

CI nutzt stattdessen die `gitleaks-action` (siehe `.github/workflows/security.yml`),
braucht das lokale Binary nicht.
