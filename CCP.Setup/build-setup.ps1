param(
    [string]$Version = "0.2.3"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$PublishRoot = Join-Path $PSScriptRoot "publish"
$Win64Dir = Join-Path $PublishRoot "win-x64"
$WorkerDir = Join-Path $PublishRoot "worker-x86"
$SetupDir = Join-Path $PublishRoot "setup"
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

Write-Host "CCP SCAN HO SO DANG VIEN - BUILD WINDOWS X64 SETUP" -ForegroundColor Green
Write-Host "Version: $Version"

$Iscc = Find-OrInstallInnoSetup
Write-Host "Inno Setup: $Iscc" -ForegroundColor Green

New-Item -ItemType Directory -Path $PublishRoot -Force | Out-Null
Prepare-PublishDirectory $Win64Dir
Prepare-PublishDirectory $WorkerDir
Prepare-PublishDirectory $SetupDir

Push-Location $Root
try {
    Write-Host "`n[1/3] Publishing 32-bit TWAIN compatibility worker..." -ForegroundColor Yellow
    Invoke-Checked "dotnet" @(
        "publish", ".\NAPS2.App.Worker\NAPS2.App.Worker.csproj",
        "-c", "Release", "-r", "win-x86", "--self-contained", "true",
        "-o", $WorkerDir,
        "/p:DebugType=None", "/p:DebugSymbols=false"
    )

    Write-Host "`n[2/3] Publishing CCP Scan Win64..." -ForegroundColor Yellow
    Invoke-Checked "dotnet" @(
        "publish", ".\NAPS2.App.WinForms\NAPS2.App.WinForms.csproj",
        "-c", "Release", "-r", "win-x64", "--self-contained", "true",
        "-o", $Win64Dir,
        "/p:DebugType=None", "/p:DebugSymbols=false"
    )
    Copy-Worker $Win64Dir

    Write-Host "`n[3/3] Building Win64 installer..." -ForegroundColor Yellow
    Invoke-Checked $Iscc @(
        "/DAppArch=x64",
        "/DSourceDir=$Win64Dir",
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
