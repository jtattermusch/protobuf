#!/usr/bin/env powershell

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

$WorkingDir = $PSScriptRoot
$TempDir = Join-Path $WorkingDir 'obj'
$InstallScriptUrl = 'http://dot.net/v1/dotnet-install.ps1'
$InstallScriptPath = Join-Path $TempDir 'dotnet-install.ps1'
$GlobalJsonPath = Join-Path $WorkingDir '..' | Join-Path -ChildPath 'global.json'

function Ensure-Dir([string]$path) {
    if (!(Test-Path $path -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $path | Out-Null
    }
}

# Resolve SDK version
$GlobalJson = Get-Content -Raw $GlobalJsonPath | ConvertFrom-Json
$SDKVersion = $GlobalJson.sdk.version

# Download install script
Ensure-Dir $TempDir
Write-Host "Downloading install script: $InstallScriptUrl => $InstallScriptPath"
Invoke-WebRequest -Uri $InstallScriptUrl -OutFile $InstallScriptPath
&$InstallScriptPath -Version $SDKVersion
