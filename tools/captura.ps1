# Captura de pantalla para verificar la interfaz.
# Uso: powershell -File tools\captura.ps1 -Salida C:\ruta\captura.png [-Proceso DeskWallpaper]
# Con -Proceso captura solo la ventana principal de ese proceso; sin él, todo el escritorio virtual.
param(
    [Parameter(Mandatory = $true)][string]$Salida,
    [string]$Proceso
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class CapturaNativo {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
}
"@
# Per-Monitor v2: coordenadas en píxeles físicos.
[CapturaNativo]::SetProcessDpiAwarenessContext([IntPtr]::new(-4)) | Out-Null

if ($Proceso) {
    $p = Get-Process -Name $Proceso -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $p) { throw "El proceso $Proceso no tiene ventana." }
    [CapturaNativo]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 400
    $r = New-Object CapturaNativo+RECT
    [CapturaNativo]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
    $x = $r.Left; $y = $r.Top; $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
} else {
    $x = [CapturaNativo]::GetSystemMetrics(76); $y = [CapturaNativo]::GetSystemMetrics(77)
    $w = [CapturaNativo]::GetSystemMetrics(78); $h = [CapturaNativo]::GetSystemMetrics(79)
}

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
$bmp.Save($Salida, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "Captura guardada: $Salida ($w x $h)"
