param(
    [string]$Version = "0.2.1"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$PublishRoot = Join-Path $PSScriptRoot "publish"
$Win32Dir = Join-Path $PublishRoot "win-x86"
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

function Find-InnoSetup {
    $CandidatePaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )

    $Candidates = @($CandidatePaths | Where-Object { $_ -and (Test-Path $_) })
    if ($Candidates.Count -gt 0) {
        return $Candidates[0]
    }

    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $cmd) {
        return $cmd.Source
    }

    throw "Inno Setup 6 was not found. Install it from https://jrsoftware.org/isdl.php and run this script again."
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

    # Preserve the same native TWAIN layout used by the upstream package: the x86 worker can resolve the
    # 32-bit DSM while the main application keeps its own architecture-specific native files.
    $WorkerTwainDir = Join-Path $WorkerDir "_win32"
    if (Test-Path $WorkerTwainDir) {
        $DestTwainDir = Join-Path $LibDir "_win32"
        New-Item -ItemType Directory -Path $DestTwainDir -Force | Out-Null
        Copy-Item (Join-Path $WorkerTwainDir "*") $DestTwainDir -Recurse -Force
    }
}

Write-Host "CCP SCAN HO SO DANG VIEN - BUILD WINDOWS SETUP" -ForegroundColor Green
Write-Host "Version: $Version"

$Iscc = Find-InnoSetup
Write-Host "Inno Setup: $Iscc"

New-Item -ItemType Directory -Path $PublishRoot -Force | Out-Null
Prepare-PublishDirectory $Win32Dir
Prepare-PublishDirectory $Win64Dir
Prepare-PublishDirectory $WorkerDir
Prepare-PublishDirectory $SetupDir

Push-Location $Root
try {
    Write-Host "`n[1/5] Publishing 32-bit TWAIN worker..." -ForegroundColor Yellow
    Invoke-Checked "dotnet" @(
        "publish", ".\NAPS2.App.Worker\NAPS2.App.Worker.csproj",
        "-c", "Release", "-r", "win-x86", "--self-contained", "true",
        "-o", $WorkerDir,
        "/p:DebugType=None", "/p:DebugSymbols=false"
    )

    Write-Host "`n[2/5] Publishing CCP Scan Win32..." -ForegroundColor Yellow
    Invoke-Checked "dotnet" @(
        "publish", ".\NAPS2.App.WinForms\NAPS2.App.WinForms.csproj",
        "-c", "Release", "-r", "win-x86", "--self-contained", "true",
        "-o", $Win32Dir,
        "/p:DebugType=None", "/p:DebugSymbols=false"
    )
    Copy-Worker $Win32Dir

    Write-Host "`n[3/5] Publishing CCP Scan Win64..." -ForegroundColor Yellow
    Invoke-Checked "dotnet" @(
        "publish", ".\NAPS2.App.WinForms\NAPS2.App.WinForms.csproj",
        "-c", "Release", "-r", "win-x64", "--self-contained", "true",
        "-o", $Win64Dir,
        "/p:DebugType=None", "/p:DebugSymbols=false"
    )
    Copy-Worker $Win64Dir

    Write-Host "`n[4/5] Building Win32 installer..." -ForegroundColor Yellow
    Invoke-Checked $Iscc @(
        "/DAppArch=x86",
        "/DSourceDir=$Win32Dir",
        "/DOutputDir=$SetupDir",
        "/DAppVersion=$Version",
        $Iss
    )

    Write-Host "`n[5/5] Building Win64 installer..." -ForegroundColor Yellow
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

$Expected = @(
    (Join-Path $SetupDir "CCP_Scan_Ho_so_Dang_vien_v${Version}_Win32.exe"),
    (Join-Path $SetupDir "CCP_Scan_Ho_so_Dang_vien_v${Version}_Win64.exe")
)

Write-Host "`nBuild completed. Expected installers:" -ForegroundColor Green
foreach ($File in $Expected) {
    if (Test-Path $File) {
        $SizeMb = [math]::Round((Get-Item $File).Length / 1MB, 2)
        Write-Host "  OK  $File ($SizeMb MB)" -ForegroundColor Green
    }
    else {
        Write-Warning "Installer not found at expected path: $File"
    }
}
