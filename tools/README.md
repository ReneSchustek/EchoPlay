# tools/

Portable Werkzeuge die NICHT als NuGet-Pakete kommen.

## gitleaks

Secret-Scanner (nutzt Shannon-Entropie zur Erkennung hochentroper Strings).
Binary ist via `*.exe` gitignored — vor dem ersten Lauf installieren:

```powershell
powershell tools/install-gitleaks.ps1
```

Lokaler Aufruf:

```powershell
powershell _ai/_global/scripts/secret-scan.ps1
powershell _ai/_global/scripts/secret-scan.ps1 -History
```

CI nutzt stattdessen die `gitleaks-action` (siehe `.github/workflows/security.yml`),
braucht das lokale Binary nicht.
