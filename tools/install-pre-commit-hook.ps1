#Requires -Version 7.0
<#
.SYNOPSIS
    Installiert einen Git-Pre-Commit-Hook, der KI-Spuren in Commits verhindert.

.DESCRIPTION
    Schreibt einen Bash-Hook nach .git/hooks/pre-commit, der die Commit-Message
    und die geaenderten Inhalte gegen KI-Schluesselworte prueft. Trifft etwas,
    bricht der Commit ab. Bestehende Hook-Dateien werden mit -Force ueberschrieben.

    Aufruf vom Repo-Root:
        powershell tools/install-pre-commit-hook.ps1
        powershell tools/install-pre-commit-hook.ps1 -Force
#>

[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$hookDir  = Join-Path $repoRoot '.git\hooks'
$hookFile = Join-Path $hookDir 'pre-commit'
$msgHook  = Join-Path $hookDir 'commit-msg'

if (-not (Test-Path $hookDir)) {
    Write-Error "Kein .git/hooks-Verzeichnis im Repo-Root gefunden ($hookDir). Repo initialisiert?"
    exit 2
}

$hookContent = @'
#!/usr/bin/env bash
# pre-commit: blockiert KI-Spuren im Diff der gestageten Aenderungen.

set -e

# KI-Schluesselworte, die nicht in Code/Doku auftauchen duerfen.
patterns='Claude|Anthropic|claude\.ai|noreply@anthropic|🤖 Generated|Generated with .*Claude|Co-Authored-By: Claude'

# Pfade ausschliessen, die KI-Schluesselworte explizit dokumentieren oder als
# Workflow-Patterns enthalten (interne .ai-Doku, der Hook-Installer selbst,
# Workflow-Skripte, die "Claude-Delegation" als Konzept beschreiben).
ignore_paths='^(_ai/|tools/install-pre-commit-hook\.ps1$|\.git/hooks/|CLAUDE\.md$)'

# Geprueft werden NUR Dateien ausserhalb der Ausnahme-Pfade.
files_to_check=$(git diff --cached --name-only --diff-filter=ACM | grep -vE "$ignore_paths" || true)
violations=""

while IFS= read -r f; do
    [ -z "$f" ] && continue
    file_diff=$(git diff --cached -- "$f" 2>/dev/null || true)
    if echo "$file_diff" | grep -nE "^\+.*($patterns)" >/dev/null 2>&1; then
        violations="$violations\n$f:"
        violations="$violations\n$(echo "$file_diff" | grep -nE "^\+.*($patterns)" | head -3)"
    fi
done <<< "$files_to_check"

if [ -n "$violations" ]; then
    echo "[pre-commit] FEHLER: KI-Spur im Diff erkannt:" >&2
    printf "%b\n" "$violations" >&2
    echo "" >&2
    echo "Suchmuster: $patterns" >&2
    echo "Bitte die Markierung entfernen oder paraphrasieren, dann erneut committen." >&2
    exit 1
fi

exit 0
'@

$msgHookContent = @'
#!/usr/bin/env bash
# commit-msg: blockiert KI-Footer in Commit-Messages.

set -e
msg_file="$1"
patterns='Claude|Anthropic|claude\.ai|noreply@anthropic|🤖 Generated|Generated with .*Claude|Co-Authored-By: Claude'

if grep -nE "$patterns" "$msg_file" >/dev/null 2>&1; then
    echo "[commit-msg] FEHLER: KI-Spur in der Commit-Message erkannt." >&2
    echo "Bitte den Footer entfernen, dann erneut committen." >&2
    exit 1
fi

exit 0
'@

if ((Test-Path $hookFile) -and -not $Force) {
    Write-Host "[pre-commit] Vorhandener Hook bleibt unveraendert. Mit -Force ueberschreiben." -ForegroundColor Yellow
} else {
    Set-Content -Path $hookFile -Value $hookContent -NoNewline
    if ($IsLinux -or $IsMacOS) {
        & chmod +x $hookFile
    }
    Write-Host "[pre-commit] Hook installiert: $hookFile" -ForegroundColor Green
}

if ((Test-Path $msgHook) -and -not $Force) {
    Write-Host "[commit-msg] Vorhandener Hook bleibt unveraendert. Mit -Force ueberschreiben." -ForegroundColor Yellow
} else {
    Set-Content -Path $msgHook -Value $msgHookContent -NoNewline
    if ($IsLinux -or $IsMacOS) {
        & chmod +x $msgHook
    }
    Write-Host "[commit-msg] Hook installiert: $msgHook" -ForegroundColor Green
}

Write-Host "[hooks] KI-Spur-Blockade aktiv. Commits werden bei Funden abgewiesen." -ForegroundColor Cyan
exit 0
