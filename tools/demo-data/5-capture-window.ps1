<#
.SYNOPSIS
    Nimmt das EchoPlay-Fenster als PNG auf.

.DESCRIPTION
    Das Fenster bekommt vorher eine feste Größe statt Vollbild. Grund: Die Bilder werden auf
    der Webseite 800 Punkte breit gezeigt. Ein Vollbild von 2560 Punkten müsste dafür auf ein
    Drittel schrumpfen — die Beschriftungen wären dann nicht mehr lesbar, und die halbe Fläche
    wäre ohnehin leer.

    Zugeschnitten wird über DwmGetWindowAttribute statt über GetWindowRect: Windows meldet dort
    einige Punkte unsichtbaren Rand mit. Wer die verwendet, bekommt einen schwarzen Saum an
    den Kanten.

    Zwei Gegenproben laufen nach der Aufnahme: schwarze Ecken (Ausschnitt danebengelegen) und
    die Zahl der Farbtöne an der rechten Kante (eine einfarbige Randspalte deutet ebenfalls auf
    einen Versatz hin). Beide Werte stehen in der Ausgabe.

.PARAMETER Target
    Zieldatei (.png).

.PARAMETER Width
    Fensterbreite. 1280 hat sich bewährt.

.PARAMETER Height
    Fensterhöhe. 1010 füllt einen Full-HD-Bildschirm ohne Rest.
#>
param(
    [Parameter(Mandatory)][string]$Target,
    [int]$Width = 1280,
    [int]$Height = 1010
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type -Namespace Native -Name Window -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
[DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT val, int size);
public struct RECT { public int Left, Top, Right, Bottom; }
'@ -ErrorAction SilentlyContinue

$process = Get-Process EchoPlay.App -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $process) { throw 'EchoPlay läuft nicht — keine Aufnahme möglich.' }
$handle = $process.MainWindowHandle

# SW_RESTORE (9) holt das Fenster aus dem Vollbild, sonst greift die Größenangabe nicht.
[void][Native.Window]::ShowWindow($handle, 9)
Start-Sleep -Milliseconds 600
# SWP_NOZORDER (0x0004)
[void][Native.Window]::SetWindowPos($handle, [IntPtr]::Zero, 40, 4, $Width, $Height, 0x0004)
Start-Sleep -Seconds 2

# Prüfen, dass EchoPlay wirklich vorn liegt. Schiebt sich ein anderes Fenster davor, zeigt die
# Aufnahme dessen Inhalt — einmal passiert und nur aufgefallen, weil das Bild angesehen wurde.
[void][Native.Window]::SetForegroundWindow($handle)
Start-Sleep -Seconds 2

if ([Native.Window]::GetForegroundWindow() -ne $handle) {
    throw 'Ein anderes Fenster liegt im Vordergrund — Aufnahme abgebrochen.'
}

# DWMWA_EXTENDED_FRAME_BOUNDS = 9: die sichtbaren Kanten ohne den unsichtbaren Rand.
$bounds = New-Object Native.Window+RECT
$result = [Native.Window]::DwmGetWindowAttribute($handle, 9, [ref]$bounds, 16)
if ($result -ne 0) { throw "Fensterkanten nicht lesbar (Fehlercode $result)." }

$width = $bounds.Right - $bounds.Left
$height = $bounds.Bottom - $bounds.Top

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$canvas = [System.Drawing.Graphics]::FromImage($bitmap)
$canvas.CopyFromScreen($bounds.Left, $bounds.Top, 0, 0, (New-Object System.Drawing.Size $width, $height))
$canvas.Dispose()

$corners = @(
    $bitmap.GetPixel(0, 0),
    $bitmap.GetPixel($bitmap.Width - 1, 0),
    $bitmap.GetPixel(0, $bitmap.Height - 1),
    $bitmap.GetPixel($bitmap.Width - 1, $bitmap.Height - 1)
)
$blackCorners = @($corners | Where-Object { $_.R -lt 12 -and $_.G -lt 12 -and $_.B -lt 12 }).Count
$edgeShades = @(0..($bitmap.Height - 1) |
    ForEach-Object { $bitmap.GetPixel($bitmap.Width - 1, $_).ToArgb() } |
    Sort-Object -Unique).Count

$bitmap.Save($Target, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

$sizeKb = [Math]::Round((Get-Item $Target).Length / 1KB)
"{0}  ({1}x{2}, {3} KB, schwarze Ecken: {4}, Farbtöne rechte Kante: {5})" -f `
    (Split-Path $Target -Leaf), $width, $height, $sizeKb, $blackCorners, $edgeShades
