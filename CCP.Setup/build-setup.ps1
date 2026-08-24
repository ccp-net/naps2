param([string]$Version = "0.2.6")

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$PublishRoot = Join-Path $PSScriptRoot "publish"
$Win64Dir = Join-Path $PublishRoot "win-x64"
$WorkerDir = Join-Path $PublishRoot "worker-x86"
$SetupDir = Join-Path $PublishRoot "setup"
$CacheDir = Join-Path $PSScriptRoot "cache"
$VcRedist = Join-Path $CacheDir "vc_redist.x64.exe"
$Iss = Join-Path $PSScriptRoot "ccp-scan.iss"
$AppIcon = Join-Path $Root "NAPS2.Lib\Icons\favicon.ico"

function Invoke-Checked([string]$FilePath, [string[]]$Arguments) {
    Write-Host "> $FilePath $($Arguments -join ' ')" -ForegroundColor Cyan
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code ${LASTEXITCODE}: $FilePath" }
}

function Get-InnoSetupPath {
    try {
        $p = Get-Process Compil32 -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($p -and $p.MainModule.FileName) {
            $x = Join-Path (Split-Path $p.MainModule.FileName -Parent) "ISCC.exe"
            if (Test-Path $x) { return $x }
        }
    } catch {}
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $paths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Inno Setup 6\ISCC.exe"
    )
    return @($paths | Where-Object { $_ -and (Test-Path $_) }) | Select-Object -First 1
}

function Find-OrInstallInnoSetup {
    $existing = Get-InnoSetupPath
    if ($existing) { return $existing }
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $winget) { throw "Inno Setup 6 was not found. Install Inno Setup 6 and run again." }
    & $winget.Source install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements --silent
    Start-Sleep -Seconds 2
    $existing = Get-InnoSetupPath
    if (-not $existing) { throw "Inno Setup installation finished but ISCC.exe was not found." }
    return $existing
}

function Reset-Dir([string]$Path) {
    if (Test-Path $Path) { Remove-Item $Path -Recurse -Force }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Test-ClassicIco([byte[]]$Bytes) {
    if ($Bytes.Length -lt 22) { return $false }
    if ($Bytes[0] -ne 0 -or $Bytes[1] -ne 0 -or $Bytes[2] -ne 1 -or $Bytes[3] -ne 0) { return $false }
    $count = [BitConverter]::ToUInt16($Bytes, 4)
    if ($count -lt 1) { return $false }
    $offset = [BitConverter]::ToUInt32($Bytes, 18)
    if ($offset + 4 -gt $Bytes.Length) { return $false }
    # Classic DIB icon frames begin with BITMAPINFOHEADER size 40 (0x28). PNG-compressed frames begin 89 50 4E 47.
    return [BitConverter]::ToUInt32($Bytes, [int]$offset) -eq 40
}

function Write-ClassicIcoFrame([System.Drawing.Bitmap]$Bitmap) {
    $width = $Bitmap.Width
    $height = $Bitmap.Height
    $xorSize = $width * $height * 4
    $maskStride = [int]([Math]::Ceiling($width / 32.0) * 4)
    $maskSize = $maskStride * $height

    $ms = New-Object IO.MemoryStream
    $bw = New-Object IO.BinaryWriter($ms)
    try {
        # BITMAPINFOHEADER. ICO stores the height doubled because XOR and AND masks are stacked vertically.
        $bw.Write([uint32]40)
        $bw.Write([int32]$width)
        $bw.Write([int32]($height * 2))
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]0)
        $bw.Write([uint32]$xorSize)
        $bw.Write([int32]0)
        $bw.Write([int32]0)
        $bw.Write([uint32]0)
        $bw.Write([uint32]0)

        # 32-bit BGRA pixels, bottom-up as required by a DIB.
        for ($y = $height - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $width; $x++) {
                $c = $Bitmap.GetPixel($x, $y)
                $bw.Write([byte]$c.B)
                $bw.Write([byte]$c.G)
                $bw.Write([byte]$c.R)
                $bw.Write([byte]$c.A)
            }
        }

        # With 32-bit alpha the legacy AND mask can be fully transparent/zeroed; Windows uses the alpha channel.
        $bw.Write((New-Object byte[] $maskSize))
        $bw.Flush()
        return $ms.ToArray()
    }
    finally {
        $bw.Dispose()
        $ms.Dispose()
    }
}

function Ensure-ValidApplicationIcon {
    if (-not (Test-Path $AppIcon)) { throw "Application icon not found: $AppIcon" }

    $bytes = [IO.File]::ReadAllBytes($AppIcon)
    if (Test-ClassicIco $bytes) {
        Write-Host "Application icon is already a classic Windows ICO." -ForegroundColor Green
        return
    }

    Write-Host "Normalizing CCP icon to classic Windows ICO (DIB, no PNG-compressed frames)..." -ForegroundColor Yellow
    Add-Type -AssemblyName System.Drawing

    # Load the existing artwork. It may be either a PNG stored with an .ico extension or a PNG-compressed ICO.
    $sourceBitmap = $null
    $sourceIcon = $null
    try {
        if ($bytes.Length -ge 4 -and $bytes[0] -eq 0 -and $bytes[1] -eq 0 -and $bytes[2] -eq 1 -and $bytes[3] -eq 0) {
            $sourceIcon = New-Object System.Drawing.Icon($AppIcon, 64, 64)
            $sourceBitmap = $sourceIcon.ToBitmap()
        }
        else {
            $image = [System.Drawing.Image]::FromFile($AppIcon)
            try { $sourceBitmap = New-Object System.Drawing.Bitmap($image) } finally { $image.Dispose() }
        }

        $sizes = @(16, 24, 32, 48, 64, 128, 256)
        $frames = New-Object System.Collections.Generic.List[object]
        foreach ($size in $sizes) {
            $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try {
                $g.Clear([System.Drawing.Color]::Transparent)
                $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $g.DrawImage($sourceBitmap, 0, 0, $size, $size)
                $frameBytes = Write-ClassicIcoFrame $bmp
                $frames.Add([PSCustomObject]@{ Size = $size; Bytes = $frameBytes })
            }
            finally { $g.Dispose(); $bmp.Dispose() }
        }

        $temp = "$AppIcon.tmp"
        $fs = [IO.File]::Open($temp, [IO.FileMode]::Create, [IO.FileAccess]::Write)
        $writer = New-Object IO.BinaryWriter($fs)
        try {
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]$frames.Count)

            $offset = 6 + 16 * $frames.Count
            foreach ($frame in $frames) {
                $sizeByte = if ($frame.Size -ge 256) { 0 } else { [byte]$frame.Size }
                $writer.Write([byte]$sizeByte)
                $writer.Write([byte]$sizeByte)
                $writer.Write([byte]0)
                $writer.Write([byte]0)
                $writer.Write([uint16]1)
                $writer.Write([uint16]32)
                $writer.Write([uint32]$frame.Bytes.Length)
                $writer.Write([uint32]$offset)
                $offset += $frame.Bytes.Length
            }
            foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
            $writer.Flush()
        }
        finally { $writer.Dispose(); $fs.Dispose() }

        Move-Item $temp $AppIcon -Force
        $verify = [IO.File]::ReadAllBytes($AppIcon)
        if (-not (Test-ClassicIco $verify)) { throw "Classic ICO verification failed after conversion." }
        Write-Host "Classic Windows ICO created successfully: $AppIcon" -ForegroundColor Green
    }
    finally {
        if ($sourceBitmap) { $sourceBitmap.Dispose() }
        if ($sourceIcon) { $sourceIcon.Dispose() }
    }
}

function Copy-Worker([string]$Destination) {
    $lib = Join-Path $Destination "lib"
    New-Item -ItemType Directory -Path $lib -Force | Out-Null
    Copy-Item (Join-Path $WorkerDir "NAPS2.Worker.exe") (Join-Path $lib "NAPS2.Worker.exe") -Force
    $twain = Join-Path $WorkerDir "_win32"
    if (Test-Path $twain) {
        $dest = Join-Path $lib "_win32"
        New-Item -ItemType Directory -Path $dest -Force | Out-Null
        Copy-Item (Join-Path $twain "*") $dest -Recurse -Force
    }
}

function Get-PeMachine([string]$Path) {
    $s = [IO.File]::OpenRead($Path); $r = New-Object IO.BinaryReader($s)
    try {
        $s.Position = 0x3C; $off = $r.ReadInt32(); $s.Position = $off
        if ($r.ReadUInt32() -ne 0x00004550) { throw "Invalid PE file: $Path" }
        return $r.ReadUInt16()
    } finally { $r.Dispose(); $s.Dispose() }
}

function Ensure-PdfiumNativeLayout([string]$Destination) {
    $candidates = @(Get-ChildItem $Destination -Recurse -File -Filter pdfium.dll -ErrorAction SilentlyContinue)
    if ($candidates.Count -eq 0) { throw "pdfium.dll missing from Win64 publish output." }
    $pdfium = $candidates | Sort-Object @{Expression={
        if ($_.FullName -match 'runtimes[\\/]win-x64[\\/]native') {0}
        elseif ($_.FullName -match '[\\/]_win64[\\/]') {1}
        elseif ($_.FullName -match '[\\/]win64[\\/]') {2}
        else {3}
    }}, FullName | Select-Object -First 1
    if ((Get-PeMachine $pdfium.FullName) -ne 0x8664) { throw "Selected pdfium.dll is not x64: $($pdfium.FullName)" }

    $siblings = @(Get-ChildItem $pdfium.DirectoryName -File | Where-Object { $_.Extension -in @('.dll','.dat') })
    foreach ($target in @((Join-Path $Destination 'lib\_win64'), (Join-Path $Destination '_win64'), (Join-Path $Destination 'win64'))) {
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        foreach ($file in $siblings) {
            $dst = Join-Path $target $file.Name
            if (-not [string]::Equals([IO.Path]::GetFullPath($file.FullName), [IO.Path]::GetFullPath($dst), [StringComparison]::OrdinalIgnoreCase)) {
                Copy-Item $file.FullName $dst -Force
            }
        }
        $targetPdfium = Join-Path $target 'pdfium.dll'
        if (-not (Test-Path $targetPdfium)) { Copy-Item $pdfium.FullName $targetPdfium -Force }
    }
    Write-Host "Pdfium x64 native layout prepared." -ForegroundColor Green
}

function Ensure-VcRedist {
    New-Item -ItemType Directory -Path $CacheDir -Force | Out-Null
    if ((Test-Path $VcRedist) -and ((Get-Item $VcRedist).Length -gt 1MB)) {
        Write-Host "VC++ x64 redistributable already cached." -ForegroundColor Green
        return
    }
    if (Test-Path $VcRedist) { Remove-Item $VcRedist -Force }
    Write-Host "Downloading Microsoft Visual C++ 2015-2022 x64 Redistributable..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri "https://aka.ms/vs/17/release/vc_redist.x64.exe" -OutFile $VcRedist -UseBasicParsing
    if ((-not (Test-Path $VcRedist)) -or ((Get-Item $VcRedist).Length -le 1MB)) { throw "Invalid vc_redist.x64.exe download." }
}

Write-Host "CCP SCAN HO SO DANG VIEN - BUILD WINDOWS X64 SETUP" -ForegroundColor Green
Write-Host "Version: $Version"
$Iscc = Find-OrInstallInnoSetup
Write-Host "Inno Setup: $Iscc" -ForegroundColor Green

New-Item -ItemType Directory -Path $PublishRoot -Force | Out-Null
Reset-Dir $Win64Dir; Reset-Dir $WorkerDir; Reset-Dir $SetupDir; Ensure-VcRedist; Ensure-ValidApplicationIcon

Push-Location $Root
try {
    Write-Host "`n[1/4] Publishing TWAIN x86 worker..." -ForegroundColor Yellow
    Invoke-Checked "dotnet" @("publish", ".\NAPS2.App.Worker\NAPS2.App.Worker.csproj", "-c", "Release", "-r", "win-x86", "--self-contained", "true", "-o", $WorkerDir, "/p:DebugType=None", "/p:DebugSymbols=false")
    Write-Host "`n[2/4] Publishing CCP Scan Win64..." -ForegroundColor Yellow
    Invoke-Checked "dotnet" @("publish", ".\NAPS2.App.WinForms\NAPS2.App.WinForms.csproj", "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-o", $Win64Dir, "/p:DebugType=None", "/p:DebugSymbols=false")
    Copy-Worker $Win64Dir
    Write-Host "`n[3/4] Preparing Pdfium native files..." -ForegroundColor Yellow
    Ensure-PdfiumNativeLayout $Win64Dir
    Write-Host "`n[4/4] Building Win64 installer..." -ForegroundColor Yellow
    Invoke-Checked $Iscc @("/DSourceDir=$Win64Dir", "/DPrereqDir=$CacheDir", "/DOutputDir=$SetupDir", "/DAppVersion=$Version", $Iss)
}
finally { Pop-Location }

$Expected = Join-Path $SetupDir "CCP_Scan_Ho_so_Dang_vien_v${Version}_Win64.exe"
if (Test-Path $Expected) {
    $SizeMb = [math]::Round((Get-Item $Expected).Length / 1MB, 2)
    Write-Host "`nOK: $Expected ($SizeMb MB)" -ForegroundColor Green
} else { throw "Installer was not created at expected path: $Expected" }
