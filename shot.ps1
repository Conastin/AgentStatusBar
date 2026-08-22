param([string]$OutFile = "D:\Project\AgentStatusBar\shot-taskbar.png", [int]$Bottom = 90)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$y = $b.Height - $Bottom
$r = New-Object System.Drawing.Rectangle(0, $y, $b.Width, $Bottom)
$bmp = New-Object System.Drawing.Bitmap($r.Width, $r.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Location, [System.Drawing.Point]::Empty, $r.Size)
$bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()
Write-Output ("saved " + $r.ToString())
