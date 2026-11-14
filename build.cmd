@echo off
rem Builds Gestirn dev into dist\Gestirn-dev. See build.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 pause
