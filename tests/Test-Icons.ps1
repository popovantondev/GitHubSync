param([string]$PackagePath='')
$ErrorActionPreference='Stop'
if(-not $PackagePath) {
    $root=Split-Path -Parent $PSScriptRoot
    & (Join-Path $root 'tools/Build-Launcher.ps1')
    if($LASTEXITCODE -ne 0){throw 'Cannot build native icon test fixture'}
    $PackagePath=Join-Path $root 'artifacts/build'
}
Add-Type -AssemblyName System.Drawing,System.Windows.Forms
$exe=Join-Path $PackagePath 'GitHubSync.exe'
$assembly=[Reflection.Assembly]::LoadFrom($exe)
$type=$assembly.GetType('WatchdogWindow',$true)
$readTray=$type.GetMethod('ReadTrayIcon',[Reflection.BindingFlags]'Static,NonPublic')
function Assert-ClearIcon([Drawing.Icon]$icon,[string]$name) {
    # .NET Framework Icon.ToBitmap loses PNG alpha on some small ICO frames.
    # Test the actual Win32 drawing path over a contrasting background instead.
    $bitmap=New-Object Drawing.Bitmap $icon.Width,$icon.Height
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    $background=[Drawing.Color]::FromArgb(255,0,124,255)
    try {$graphics.Clear($background);$graphics.DrawIconUnstretched($icon,[Drawing.Rectangle]::new(0,0,$icon.Width,$icon.Height))}finally{$graphics.Dispose()}
    try {
        foreach($point in @([Drawing.Point]::new(0,0),[Drawing.Point]::new(0,[int]($bitmap.Height/2)),[Drawing.Point]::new($bitmap.Width-1,[int]($bitmap.Height/2)))) {
            if($bitmap.GetPixel($point.X,$point.Y).ToArgb() -ne $background.ToArgb()) { throw "Opaque outer tile: $name size=$($icon.Size) point=$point color=$($bitmap.GetPixel($point.X,$point.Y))" }
        }
        $top=$bitmap.Height;$bottom=-1
        for($y=0;$y -lt $bitmap.Height;$y++){for($x=0;$x -lt $bitmap.Width;$x++){
            $pixel=$bitmap.GetPixel($x,$y)
            if($pixel.A -ge 64 -and $pixel.R -lt 100 -and $pixel.G -lt 100 -and $pixel.B -lt 100){$top=[Math]::Min($top,$y);$bottom=[Math]::Max($bottom,$y)}
        }}
        if($bottom-$top+1 -lt [Math]::Floor($bitmap.Height*.9)){throw "Native icon symbol too small: $name"}
    } finally {$bitmap.Dispose()}
}
$shellIcon=[Drawing.Icon]::ExtractAssociatedIcon($exe)
try {Assert-ClearIcon $shellIcon 'EXE Win32 resource / Explorer icon'} finally {$shellIcon.Dispose()}
foreach($name in @('Watchdog.IconIco')+(1..12 | ForEach-Object {'Sync.Frame'+$_})) {
    $stream=$assembly.GetManifestResourceStream($name)
    try {$icon=$readTray.Invoke($null,@($stream));try{Assert-ClearIcon $icon $name}finally{$icon.Dispose()}}finally{$stream.Dispose()}
}
Write-Output 'PASS actual packaged EXE native shell icon and 13 tray ICO resources: transparent outer edges and enlarged symbol. No GUI click, login or transfer.'
