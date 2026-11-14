# Construit le setup Windows (win-x64) : Control24-win-x64-<version>.exe
# Contient le driver pour REAPER (installé dans %APPDATA%\REAPER\UserPlugins) et l'éditeur de configuration
# Control24 Config (installé dans %LOCALAPPDATA%\Programs\Control24 Config). Aucun droit administrateur requis.
# Prérequis : SDK .NET, outils C++ de Visual Studio (NativeAOT + plugin natif), Inno Setup 6 (winget install JRSoftware.InnoSetup)
# Usage : .\Setup\build-win-x64.ps1 [-Version 1.0.0]
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"

$Here = $PSScriptRoot
$Repo = (Resolve-Path (Join-Path $Here "..\..")).Path
$Out = Join-Path $Here "out"
$Stage = Join-Path $Out "stage"
$Build = Join-Path $Out "build"

function Invoke-Checked([string]$what, [scriptblock]$cmd) {
    & $cmd
    if ($LASTEXITCODE -ne 0) { throw "$what a échoué (code $LASTEXITCODE)" }
}

# Compilateur Inno Setup
$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) { throw "Inno Setup 6 introuvable : winget install JRSoftware.InnoSetup" }

# NativeAOT localise l'éditeur de liens via vswhere.exe, qui doit donc être dans le PATH
$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"

if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force "$Stage\driver", "$Stage\config", $Build | Out-Null

# 1. Driver : publication autonome (runtime .NET embarqué).
#    OutputPath est redirigé : le projet écrit sinon directement dans le dossier UserPlugins de REAPER.
Invoke-Checked "publish driver" { dotnet publish "$Repo\DriverReaperControl24\DriverReaperControl24.vbproj" -c Release -r win-x64 --self-contained `
    -p:DebugType=none "-p:OutputPath=$Build\driver\" -o "$Stage\driver" }

# 2. Bridge REAPER (NativeAOT) — c'est une DLL native : on ne garde que reaper_c24bridge.dll
$bridgeBuild = "$Build\bridge\"
Invoke-Checked "publish bridge" { dotnet publish "$Repo\ReaperC24Bridge\ReaperC24Bridge.csproj" -c Release -r win-x64 "-p:OutputPath=$bridgeBuild" }
#    (OutputPath redirigé : la publication est dans $bridgeBuild\publish, pas dans bin\Release du projet, qui peut être périmé)
$bridgeDll = "$bridgeBuild\publish\reaper_c24bridge.dll"
if (-not (Test-Path $bridgeDll)) { throw "reaper_c24bridge.dll introuvable après publication : $bridgeDll" }
Copy-Item $bridgeDll "$Stage\driver\"

# 3. Plugin natif d'événements (C++)
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild) { throw "MSBuild (Visual Studio) introuvable" }
Invoke-Checked "build plugin natif" { & $msbuild "$Repo\NativeReaperPluginDLL\NativeReaperPluginDLL.vcxproj" /p:Configuration=Release /p:Platform=x64 /v:minimal }
$native = Get-ChildItem "$Repo\x64\Release\reaper_NativeReaperPlugIn.dll", "$Repo\NativeReaperPluginDLL\x64\Release\reaper_NativeReaperPlugIn.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $native) { throw "reaper_NativeReaperPlugIn.dll introuvable après compilation" }
Copy-Item $native.FullName "$Stage\driver\"

# 4. Configuration par défaut (copiée seulement si l'utilisateur n'en a pas déjà une) : VSDEBUG=False
$ini = [IO.File]::ReadAllText("$Repo\ReaperDriverControl24.ini", [Text.Encoding]::UTF8) -replace '(?im)^(VSDEBUG)=.*$', '$1=False'
[IO.File]::WriteAllText("$Stage\driver\ReaperDriverControl24.ini.default", $ini, (New-Object Text.UTF8Encoding $true))
Remove-Item "$Stage\driver\ReaperDriverControl24.ini" -ErrorAction SilentlyContinue

# 5. Éditeur de configuration (Blazor Server) : publication autonome + lanceur
Invoke-Checked "publish config" { dotnet publish "$Repo\DriverConfig.Web\DriverConfig.Web.csproj" -c Release -r win-x64 --self-contained `
    -p:DebugType=none "-p:OutputPath=$Build\config\" -o "$Stage\config" }
Copy-Item "$Repo\DriverConfig.Web\Setup\Control24Config.ps1" "$Stage\config\"
Copy-Item "$Repo\digidesign-control-24.ico" "$Stage\config\AppIcon.ico"

# 5b. Installeur de Npcap (npcap-*.exe à la racine du dépôt), lancé par le setup si Npcap est absent
$npcap = Get-ChildItem "$Repo\npcap-*.exe" | Sort-Object Name | Select-Object -Last 1
if (-not $npcap) { throw "Installeur Npcap introuvable : placez npcap-<version>.exe à la racine du dépôt" }
Copy-Item $npcap.FullName "$Stage\npcap.exe"

# 6. Installeur
Invoke-Checked "Inno Setup" { & $iscc "/DAppVersion=$Version" "/DStageDir=$Stage" "/DOutDir=$Out" "$Here\Control24.iss" }

$exe = Join-Path $Out "Control24-win-x64-$Version.exe"
Write-Host "OK -> $exe"
