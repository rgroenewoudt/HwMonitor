# Builds RyzenMasterShim.dll into this folder.
# Needs once: VS Build Tools with the C++ workload (see lib/adlx/build.ps1), and the AMD Ryzen Master Monitoring SDK
# installed (https://www.amd.com/en/developer/ryzen-master-monitoring-sdk.html). Only its headers are used, from the
# install folder; they are AMD confidential and must not be copied into this repo.
$ErrorActionPreference = 'Stop'

$sdk = [Environment]::GetEnvironmentVariable('AMDRMMONITORSDKPATH', 'Machine')
if (-not $sdk) { $sdk = (Get-ItemProperty 'HKLM:\Software\AMD\RyzenMasterMonitoringSDK' -ErrorAction SilentlyContinue).InstallationPath }
if (-not $sdk -or -not (Test-Path (Join-Path $sdk 'include\ICPUEx.h'))) { throw 'Ryzen Master Monitoring SDK not found.' }
$include = Join-Path $sdk 'include'

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'VS Build Tools with the C++ workload not found.' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$env:PATH = "$(Split-Path $vswhere);$env:PATH"   # vcvars64.bat calls vswhere too

$out = $PSScriptRoot
# Object files go to a temp folder, only the DLL to this folder.
$work = Join-Path $env:LOCALAPPDATA 'hwmonitor-ryzenmaster-build'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory $work | Out-Null

# /MT: static VC++ runtime, so no VC++ redistributable is needed. /EHa: C++ exceptions and SEH together.
$cl = "cl /nologo /O2 /MT /EHa /W3 /LD /std:c++17 /I`"$include`" `"$out\RyzenMasterShim.cpp`" /Fo`"$work\\`" /Fe`"$work\RyzenMasterShim.dll`" /link /NOIMPLIB /NOEXP"
cmd /c "`"$vcvars`" >nul && $cl"
if ($LASTEXITCODE -ne 0) { throw 'RyzenMasterShim build failed' }

Copy-Item (Join-Path $work 'RyzenMasterShim.dll') $out -Force
Write-Host "Done: $out\RyzenMasterShim.dll"
