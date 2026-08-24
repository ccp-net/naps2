param(
    [string]$Version = "0.2.5"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$PublishRoot = Join-Path $PSScriptRoot "publish"
$Win64Dir = Join-Path $PublishRoot "win-x64"
$WorkerDir = Join-Path $PublishRoot "worker-x86"
$SetupDir = Join-Path $PublishRoot "setup"
$PrereqCacheDir = Join-Path $PSScriptRoot "cache"
$VcRedistPath = Join-Path $PrereqCacheDir "vc_redist.x64.exe"
$Iss = Join-Path $PSScriptRoot "ccp-scan.iss"

function Invoke-Checked {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [Parameter(Mandatory=$true)][string[]]$Arguments
    )
    Write-Host "> $FilePath $($Arguments -join ' ')" -ForegroundColor Cyan
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath"
    }
}

function Get-InnoSetupPath {
    try {
        $CompilerProcess = Get-Process -Name "Compil32" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $CompilerProcess) {
            $CompilerExe = $CompilerProcess.MainModule.FileName
            if ($CompilerExe) {
                $FromRunningCompiler = Join-Path (Split-Path $CompilerExe -Parent) "ISCC.exe"
                if (Test-Path $FromRunningCompiler) {
                    return $FromRunningCompiler
                }
            }
        }
    }
    catch {
    }

    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $cmd) {
        return $cmd.Source
    }

    $CandidatePaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Inno Setup 6\ISCC.exe"
    )

    $Candidates = @($CandidatePaths | Where-Object { $_ -and (Test-Path $_) })
    if ($Candidates.Count -gt 0) {
        return $Candidates[0]
    }

    $RegistryKeys = @(
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1"
    )

    foreach ($Key in $RegistryKeys) {
        try {
            if (Test-Path $Key) {
                $Info = Get-ItemProperty $Key -ErrorAction Stop
                if ($Info.InstallLocation) {
                    $FromRegistry = Join-Path $Info.InstallLocation "ISCC.exe"
                    if (Test-Path $FromRegistry) {
                        return $FromRegistry
                    }
                }
            }
        }
        catch {
        }
    }

    return $null
}

function Find-OrInstallInnoSetup {
    $Existing = Get-InnoSetupPath
    if ($null -ne $Existing) {
        return $Existing
    }

    Write-Host "Inno Setup 6 was not detected. Attempting automatic installation with winget..." -ForegroundColor Yellow
    $Winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if ($null -eq $Winget) {
        throw "Inno Setup 6 was not found and winget is unavailable. Install Inno Setup 6 manually, then run this script again."
    }

    & $Winget.Source install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements --silent
    $WingetExitCode = $LASTEXITCODE
    if ($WingetExitCode -ne 0) {
        $ExistingAfterWinget = Get-InnoSetupPath
        if ($null -ne $ExistingAfterWinget) {
            return $ExistingAfterWinget
        }
        throw "Automatic Inno Setup installation failed with exit code ${WingetExitCode}. The compiler may be installed in a custom folder."
    }

    Start-Sleep -Seconds 2
    $Installed = Get-InnoSetupPath
    if ($null -eq $Installed) {
        throw "Inno Setup installation completed but ISCC.exe was not found. Open Inno Setup Compiler and rerun this script."
    }

    return $Installed
}

function Prepare-PublishDirectory {
    param([string]$Path)
    if (Test-Path $Path) {
        Remove-Item $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Copy-Worker {
    param([string]$Destination)

    $LibDir = Join-Path $Destination "lib"
    New-Item -ItemType Directory -Path $LibDir -Force | Out-Null
    Copy-Item (Join-Path $WorkerDir "NAPS2.Worker.exe") (Join-Path $LibDir "NAPS2.Worker.exe") -Force

    $WorkerTwainDir = Join-Path $WorkerDir "_win32"
    if (Test-Path $WorkerTwainDir) {
        $DestTwainDir = Join-Path $LibDir "_win32"
        New-Item -ItemType Directory -Path $DestTwainDir -Force | Out-Null
        Copy-Item (Join-Path $WorkerTwainDir "*") $DestTwainDir -Recurse -Force
    }
}

function Get-PeMachine {
    param([Parameter(Mandatory=$true)][string]$Path)

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    $reader = New-Object System.IO.BinaryReader($stream)
    try {
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset
        $signature = $reader.ReadUInt32()
        if ($signature -ne 0x00004550) {
            throw "Not a valid PE image: $Path"
        }
        return $reader.ReadUInt16()
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Ensure-PdfiumNativeLayout {
    param([string]$Destination)

    $Candidates = @(Get-ChildItem $Destination -Recurse -File -Filter "pdfium.dll" -ErrorAction SilentlyContinue)
    if ($Candidates.Count -eq 0) {
        throw "pdfium.dll was not found in the Win64 publish output. PDF Import would fail after installation, so setup creation was stopped."
    }

    # Prefer NuGet's canonical x64 runtime asset. Never copy the first arbitrary pdfium.dll because the package may
    # contain x86/ARM64 assets as well.
    $Preferred = $Candidates |
        Sort-Object @{ Expression = {
            if ($_.FullName -match "runtimes[\\/]win-x64[\\/]native") { 0 }
            elseif ($_.FullName -match "win-x64") { 1 }
            elseif ($_.DirectoryName -eq $Destination) { 2 }
            else { 3 }
        } }, FullName |
        Select-Object -First 1

    $machine = Get-PeMachine $Preferred.FullName
    if ($machine -ne 0x8664) {
        throw ("Selected pdfium.dll is not x64 (PE machine 0x{0:X4}): {1}" -f $machine, $Preferred.FullName)
    }

    # Current NAPS2 searches lib\_win64. Keep additional compatibility copies because earlier CCP previews used win64.
    $TargetDirs = @(
        (Join-Path $Destination "lib\_win64"),
        (Join-Path $Destination "_win64"),
        (Join-Path $Destination "win64")
    )
    foreach ($TargetDir in $TargetDirs) {
        New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
        Copy-Item $Preferred.FullName (Join-Path $TargetDir "pdfium.dll") -Force
    }

    Write-Host "Pdfium x64 source: $($Preferred.FullName)" -ForegroundColor Green
    Write-Host "Pdfium native layout prepared under lib\_win64, _win64 and win64." -ForegroundColor Green
}

function Ensure-VcRedist {
    New-Item -ItemType Directory -Path $PrereqCacheDir -Force | Out-Null
    if (Test-Path $VcRedistPath) {
        if ((Get-Item $VcRedistPath).Length -gt 1MB) {
            Write-Host "VC++ x64 redistributable: $VcRedistPath" -ForegroundColor Green
            return
        }
        Remove-Item $VcRedistPath -Force
    }

    Write-Host "Downloading Microsoft Visual C++ 2015-2022 x64 Redistributable..." -ForegroundColor Yellow
    $Url = "https://aka.ms/vs/17/release/vc_redist.x64.exe"
    Invoke-WebRequest -Uri $Url -OutFile $VcRedistPath -UseBasicParsing
    if (-not (Test-Path $VcRedistPath) -or (Get-Item $VcRedistPath).Length -le 1MB) {
        throw "Failed to download a valid vc_redist.x64.exe."
    }
    Write-Host "VC++ x64 redistributable downloaded: $VcRedistPath" -ForegroundColor Green
}

Write-Host "CCP SCAN HO SO DANG VIEN - BUILD WINDOWS X64 SETUP" -ForegroundColor Green
Write-Host "Version: $Version"

$Iscc = Find-OrInstallInnoSetup
Write-Host "Inno Setup: $Iscc" -ForegroundColor Green

New-Item -ItemType Directory -Path $PublishRoot -Force | Out-Null
Prepare-PublishDirectory $Win64Dir
Prepare-PublishDirectory $WorkerDir
Prepare-PublishDirectory $SetupDir
Ensure-VcRedist

Push-Location $Root
try {
    Write-Host "`n[1/4] Publishing 32-bit TWAIN compatibility worker..." -ForegroundColor Yellow
    Invoke-Checked "dotnet" @(
        "publish", ".\NAPS2.App.Worker\NAPS2.App.Worker.csproj",
        "-c", "Release", "-r", "win-x86", "--self-contained", "true",
        "-o", $WorkerDir,
        "/p:DebugType=None", "/p:DebugSymbols=false"
    )

    Write-Host "`n[2/4] Publishing CCP Scan Win64..." -ForegroundColor Yellow
    Invoke-Checked "dotnet" @(
        "publish", ".\NAPS2.App.WinForms\NAPS2.App.WinForms.csproj",
        "-c", "Release", "-r", "win-x64", "--self-contained", "true",
        "-o", $Win64Dir,
        "/p:DebugType=None", "/p:DebugSymbols=false"
    )
    Copy-Worker $Win64Dir

    Write-Host "`n[3/4] Preparing Pdfium and native prerequisites..." -ForegroundColor Yellow
    Ensure-PdfiumNativeLayout $Win64Dir

    Write-Host "`n[4/4] Building Win64 installer..." -ForegroundColor Yellow
    Invoke-Checked $Iscc @(
        "/DSourceDir=$Win64Dir",
        "/DPrereqDir=$PrereqCacheDir",
        "/DOutputDir=$SetupDir",
        "/DAppVersion=$Version",
        $Iss
    )
}
finally {
    Pop-Location
}

$Expected = Join-Path $SetupDir "CCP_Scan_Ho_so_Dang_vien_v${Version}_Win64.exe"

Write-Host "`nBuild completed. Expected installer:" -ForegroundColor Green
if (Test-Path $Expected) {
    $SizeMb = [math]::Round((Get-Item $Expected).Length / 1MB, 2)
    Write-Host "  OK  $Expected ($SizeMb MB)" -ForegroundColor Green
}
else {
    Write-Warning "Installer not found at expected path: $Expected"
}
