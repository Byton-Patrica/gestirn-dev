# Measures how many compiler errors the error dictionary can translate.
#   tools\errcov\run.ps1 [-Compiler <folder with g++.exe>]
# Compiles ~220 typical beginner mistakes, feeds the real compiler output through Gestirn dev's parser and
# dictionary, prints what is left untranslated and fails when fewer than 95% of the errors have a hand-written reading.
param([string]$Compiler = "$PSScriptRoot\..\..\dist\Gestirn-dev\MinGW64\bin")
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\..\.."
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
python "$PSScriptRoot\run_corpus.py" (Resolve-Path $Compiler)
$refs = 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'Microsoft.VisualBasic.dll', 'System.Security.dll' | ForEach-Object { "-r:" + (Join-Path $fw $_) }
$src = 'Ui', 'Editor', 'Core', 'Shell', 'Terminal' | ForEach-Object { Get-ChildItem "$root\GestirnDev\$_\*.cs" | ForEach-Object FullName }
& $csc -nologo -target:exe "-out:$PSScriptRoot\Cov.exe" $refs $src "$PSScriptRoot\Cov.cs"
if ($LASTEXITCODE) { throw 'build failed' }
& "$PSScriptRoot\Cov.exe" $PSScriptRoot
exit $LASTEXITCODE
