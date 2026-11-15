# Builds Gestirn dev into dist\Gestirn-dev: a self-contained folder that needs nothing else installed.
#   -CompilerSource <dir>  MinGW64 toolchain to bundle (default: Dev-C++'s MinGW64 if present)
#   -NoCompiler            build without bundling a compiler
#   -Package               also write dist\Gestirn-dev-<version>-win64.zip (full) and -lite.zip
# Needs .NET Framework 4.x (built into Windows) and the Roslyn C# compiler from Visual Studio Build Tools.
param(
    [string]$CompilerSource = "${env:ProgramFiles(x86)}\Dev-Cpp\MinGW64",
    [switch]$NoCompiler,
    [switch]$Package
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'dist\Gestirn-dev'
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'

$csc = $null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (Test-Path $vswhere) {
    $csc = & $vswhere -latest -products * -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
}
if (-not $csc) {
    $csc = Get-ChildItem "${env:ProgramFiles(x86)}\Microsoft Visual Studio", "$env:ProgramFiles\Microsoft Visual Studio" -Recurse -Filter csc.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like '*Roslyn*' } | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $csc) { throw 'Roslyn csc.exe not found. Install Visual Studio Build Tools (C# compiler).' }

New-Item -ItemType Directory -Force $out | Out-Null
$refs = 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'Microsoft.VisualBasic.dll' |
    ForEach-Object { "-r:" + (Join-Path $fw $_) }

& $csc -nologo -target:winexe -optimize+ "-win32icon:$root\GestirnDev\GestirnEra-datarot.ico" "-out:$out\Gestirn-dev.exe" $refs "-recurse:$root\GestirnDev\*.cs"
if ($LASTEXITCODE) { throw 'Gestirn-dev.exe build failed' }
& $csc -nologo -target:exe -optimize+ "-out:$out\GestirnRun.exe" "-r:$fw\System.dll" "-r:$fw\System.Core.dll" "$root\GestirnRun\Program.cs"
if ($LASTEXITCODE) { throw 'GestirnRun.exe build failed' }

Copy-Item "$root\GestirnDev\GestirnEra-datarot.ico", "$root\Assets\Branding\GestirnEra-datarot-256.png", "$root\COPYING.txt" $out -Force
New-Item -ItemType Directory -Force "$out\AStyle", "$out\Templates" | Out-Null
Copy-Item "$root\AStyle\AStyle.exe" "$out\AStyle\" -Force
Copy-Item "$root\Templates\*" "$out\Templates\" -Recurse -Force

if (-not $NoCompiler) {
    if (Test-Path "$CompilerSource\bin\g++.exe") {
        # Mirror the toolchain (g++, gcc, gdb, make, windres, headers and libraries).
        robocopy $CompilerSource "$out\MinGW64" /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "Copying the compiler failed ($LASTEXITCODE)" }
        $global:LASTEXITCODE = 0
        Write-Host "Bundled compiler from $CompilerSource"
    } elseif (Test-Path "$out\MinGW64\bin\g++.exe") {
        Write-Host "Keeping the compiler already in $out\MinGW64 ($CompilerSource not found)"
    } else {
        Write-Warning "No compiler bundled: $CompilerSource\bin\g++.exe not found. Pass -CompilerSource <MinGW64 folder>."
    }
}
Write-Host "Built $out"

if ($Package) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $version = (Get-Item "$out\Gestirn-dev.exe").VersionInfo.ProductVersion
    $full = Join-Path $root "dist\Gestirn-dev-$version-win64.zip"
    $lite = Join-Path $root "dist\Gestirn-dev-$version-lite.zip"
    Remove-Item $full, $lite -ErrorAction SilentlyContinue
    [IO.Compression.ZipFile]::CreateFromDirectory($out, $full, [IO.Compression.CompressionLevel]::Optimal, $true)
    $stage = Join-Path $env:TEMP "gestirn-lite-$version"
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    robocopy $out "$stage\Gestirn-dev" /E /XD MinGW64 /NFL /NDL /NJH /NJS /NP | Out-Null
    $global:LASTEXITCODE = 0
    [IO.Compression.ZipFile]::CreateFromDirectory("$stage\Gestirn-dev", $lite, [IO.Compression.CompressionLevel]::Optimal, $true)
    Remove-Item $stage -Recurse -Force
    Write-Host "Packaged $full"
    Write-Host "Packaged $lite"
}
