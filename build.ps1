# Builds Gestirn dev into dist\Gestirn-dev (a self-contained, portable folder).
# Needs .NET Framework 4.x (built into Windows) and the Roslyn C# compiler from Visual Studio Build Tools.
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

Copy-Item "$root\GestirnDev\GestirnEra-datarot.ico", "$root\Assets\Branding\GestirnEra-datarot-256.png" $out -Force
New-Item -ItemType Directory -Force "$out\AStyle", "$out\Templates" | Out-Null
Copy-Item "$root\AStyle\AStyle.exe" "$out\AStyle\" -Force
Copy-Item "$root\Templates\*" "$out\Templates\" -Recurse -Force
Write-Host "Built $out"
