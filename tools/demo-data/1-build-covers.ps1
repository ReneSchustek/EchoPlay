<#
.SYNOPSIS
    Erzeugt zwanzig Cover für die Demo-Bibliothek.

.DESCRIPTION
    Die Cover entstehen aus Farbfläche und Schrift, nicht aus Bildmaterial. So hängt an den
    Aufnahmen nichts, was jemandem gehört, und das Ergebnis ist bei jedem Lauf gleich.

    Die Schriftgröße wird zweifach begrenzt: Das längste Einzelwort muss in eine Zeile passen
    (ein Wort lässt sich nicht umbrechen — „Frankenstein" würde sonst mitten im Wort getrennt),
    und der ganze Titel muss in drei Zeilen passen. Beide Schranken sind nötig; mit nur einer
    von beiden sind in der Erprobung Titel abgeschnitten worden.

.PARAMETER Target
    Ablageordner für die JPG-Dateien. Wird angelegt, wenn er fehlt.
#>
param(
    [Parameter(Mandatory)][string]$Target
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $Target)) { New-Item -ItemType Directory $Target -Force | Out-Null }

# Gedeckte Farben in der Tonart der Anwendung — kein Regenbogen, keine Verläufe.
$titles = @(
    @{ Title = 'Sherlock Holmes';            Author = 'Arthur Conan Doyle'; Back = '#2F4858'; Fore = '#F5F5F5' },
    @{ Title = 'Die Schatzinsel';            Author = 'R. L. Stevenson';    Back = '#1F4A3C'; Fore = '#F0F5F2' },
    @{ Title = 'Alice im Wunderland';        Author = 'Lewis Carroll';      Back = '#5A3A52'; Fore = '#F7F0F5' },
    @{ Title = 'Frankenstein';               Author = 'Mary Shelley';       Back = '#3A2F2A'; Fore = '#F2EDE8' },
    @{ Title = 'Dracula';                    Author = 'Bram Stoker';        Back = '#4A2226'; Fore = '#F5EDEE' },
    @{ Title = 'Die Zeitmaschine';           Author = 'H. G. Wells';        Back = '#26364A'; Fore = '#EEF2F7' },
    @{ Title = 'Das Dschungelbuch';          Author = 'Rudyard Kipling';    Back = '#3E4A22'; Fore = '#F2F5EA' },
    @{ Title = 'Robinson Crusoe';            Author = 'Daniel Defoe';       Back = '#4A3A22'; Fore = '#F7F2E8' },
    @{ Title = 'Die drei Musketiere';        Author = 'Alexandre Dumas';    Back = '#2A3F55'; Fore = '#EEF3F8' },
    @{ Title = 'Der Graf von Monte Christo'; Author = 'Alexandre Dumas';    Back = '#1E3340'; Fore = '#EDF3F6' },
    @{ Title = 'Krieg der Welten';           Author = 'H. G. Wells';        Back = '#402A2A'; Fore = '#F5EDED' },
    @{ Title = 'Moby Dick';                  Author = 'Herman Melville';    Back = '#1C3844'; Fore = '#EAF2F5' },
    @{ Title = 'Oliver Twist';               Author = 'Charles Dickens';    Back = '#3A3428'; Fore = '#F3F0E9' },
    @{ Title = 'Peter Pan';                  Author = 'J. M. Barrie';       Back = '#22424A'; Fore = '#EBF4F6' },
    @{ Title = 'Tom Sawyer';                 Author = 'Mark Twain';         Back = '#4A4022'; Fore = '#F6F3E9' },
    @{ Title = 'Die Insel des Doktor Moreau'; Author = 'H. G. Wells';       Back = '#2E4A3A'; Fore = '#EDF6F1' },
    @{ Title = 'Winnetou';                   Author = 'Karl May';           Back = '#523A28'; Fore = '#F7F1EB' },
    @{ Title = 'Anna Karenina';              Author = 'Leo Tolstoi';        Back = '#3A2A40'; Fore = '#F2EDF5' },
    @{ Title = 'Emma';                       Author = 'Jane Austen';        Back = '#44304A'; Fore = '#F4EFF6' },
    @{ Title = 'Nordsee-Geschichten';        Author = 'Theodor Storm';      Back = '#243F4A'; Fore = '#ECF3F6' }
)

foreach ($entry in $titles) {
    $bitmap = New-Object System.Drawing.Bitmap 600, 600
    $canvas = [System.Drawing.Graphics]::FromImage($bitmap)
    $canvas.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $canvas.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

    $background = [System.Drawing.ColorTranslator]::FromHtml($entry.Back)
    $foreground = [System.Drawing.ColorTranslator]::FromHtml($entry.Fore)
    $canvas.Clear($background)

    # Feine Linie oben und unten statt Zierrat.
    $pen = New-Object System.Drawing.Pen($foreground, 3)
    $canvas.DrawLine($pen, 60, 90, 540, 90)
    $canvas.DrawLine($pen, 60, 510, 540, 510)
    $pen.Dispose()

    $brush = New-Object System.Drawing.SolidBrush($foreground)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center

    $boxWidth = 480
    $boxHeight = 210
    $longestWord = $entry.Title -split ' ' | Sort-Object Length -Descending | Select-Object -First 1
    $fontSize = 24

    for ($candidate = 62; $candidate -ge 24; $candidate -= 2) {
        $probe = New-Object System.Drawing.Font('Segoe UI', $candidate, [System.Drawing.FontStyle]::Bold)
        $wordWidth = $canvas.MeasureString($longestWord, $probe).Width
        $blockSize = $canvas.MeasureString($entry.Title, $probe, $boxWidth)
        $probe.Dispose()
        if ($wordWidth -le $boxWidth -and $blockSize.Height -le $boxHeight) { $fontSize = $candidate; break }
    }

    # Titel und Autor sitzen im oberen Bereich, nicht mittig: Die Kachel in der Mediathek ist
    # breiter als hoch und schneidet den unteren Teil des quadratischen Covers ab.
    $titleFont = New-Object System.Drawing.Font('Segoe UI', $fontSize, [System.Drawing.FontStyle]::Bold)
    $titleBox = New-Object System.Drawing.RectangleF 60, 130, $boxWidth, 220
    $canvas.DrawString($entry.Title, $titleFont, $brush, $titleBox, $format)
    $titleFont.Dispose()

    $authorFont = New-Object System.Drawing.Font('Segoe UI', 24, [System.Drawing.FontStyle]::Regular)
    $authorBox = New-Object System.Drawing.RectangleF 60, 355, $boxWidth, 60
    $canvas.DrawString($entry.Author, $authorFont, $brush, $authorBox, $format)
    $authorFont.Dispose()

    $brush.Dispose()
    $format.Dispose()
    $canvas.Dispose()

    # Der Dateiname muss zu dem passen, den Schritt 2 sucht.
    $fileName = ($entry.Title -replace '[^\w äöüÄÖÜß-]', '') -replace ' ', '_'
    $bitmap.Save((Join-Path $Target "$fileName.jpg"), [System.Drawing.Imaging.ImageFormat]::Jpeg)
    $bitmap.Dispose()
}

"{0} Cover erzeugt in {1}" -f $titles.Count, $Target
