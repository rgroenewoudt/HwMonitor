# Builds ADLXWrapper.dll + ADLXBindings.dll into this folder.
# Needs once: VS Build Tools with the C++ workload, and swig:
#   winget install Microsoft.VisualStudio.2022.BuildTools --override "--add Microsoft.VisualStudio.Workload.VCTools --includeRecommended --passive --wait"
#   winget install SWIG.SWIG
$ErrorActionPreference = 'Stop'

$wrapperRepo = 'https://github.com/Rem0o/ADLXWrapper.git'
$wrapperCommit = '9a39c81f557668f3af4e5171bc642f62c93e81cd'   # "ADLX 1.5"
$sdkRepo = 'https://github.com/GPUOpen-LibrariesAndSDKs/ADLX.git'
$sdkTag = 'v1.5'

$out = $PSScriptRoot
# Not under %TEMP%: MSBuild warns about C++ output there (MSB8029).
$work = Join-Path $env:LOCALAPPDATA 'hwmonitor-adlx-build'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }

git clone --quiet $wrapperRepo $work
git -C $work checkout --quiet $wrapperCommit
# The vcxproj expects the SDK in <repo>\ADLX (like fetch_adlx.cmd does).
git clone --quiet --depth 1 --branch $sdkTag $sdkRepo (Join-Path $work 'ADLX')

# Change 1: a pointer helper for IADLXFPS*, so C# can call GetCurrentFPS. swig regenerates the bindings in the build.
$iFile = Join-Path $work 'ADLXBindings\ADLXBindings.i'
$anchor = '%pointer_functions(IADLXGPUMetrics*, metricsP_Ptr);'
$i = Get-Content $iFile -Raw
if (-not $i.Contains($anchor)) { throw "Anchor not found in ADLXBindings.i" }
Set-Content $iFile ($i.Replace($anchor, "$anchor`r`n%pointer_functions(IADLXFPS*, fpsP_Ptr);")) -NoNewline

# swig must be on PATH for the CustomBuild step. Use the real swig.exe, not the winget link:
# through the link swig does not find its Lib folder.
$swig = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter swig.exe -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $swig) { $swig = Get-Command swig -ErrorAction SilentlyContinue | ForEach-Object { Get-Item $_.Source } }
if (-not $swig) { throw 'swig not found. Run: winget install SWIG.SWIG' }
$env:PATH = "$($swig.DirectoryName);$env:PATH"
$env:SWIG_LIB = Join-Path $swig.DirectoryName 'Lib'

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild with C++ tools not found. Install VS Build Tools with the C++ workload.' }

# Change 2: static VC++ runtime (static-crt.props), so the DLL does not need the VC++ redistributable.
# The project asks for toolset v145 (VS 2026); use the one that is installed.
$toolset = if ($msbuild -match '\\2022\\') { 'v143' } else { 'v145' }
& $msbuild (Join-Path $work 'ADLXBindings\ADLXBindings.vcxproj') /nologo /v:minimal /m `
    /p:Configuration=Release /p:Platform=x64 /p:PlatformToolset=$toolset `
    "/p:SolutionDir=$work\" "/p:ForceImportAfterCppTargets=$out\static-crt.props"
if ($LASTEXITCODE -ne 0) { throw 'ADLXBindings build failed' }

dotnet build (Join-Path $work 'ADLXWrapper\ADLXWrapper.csproj') -c Release --nologo -v minimal "-p:SolutionDir=$work\"
if ($LASTEXITCODE -ne 0) { throw 'ADLXWrapper build failed' }

Copy-Item (Join-Path $work 'x64\Release\ADLXBindings.dll') $out -Force
Copy-Item (Join-Path $work 'ADLXWrapper\bin\Release\netstandard2.0\ADLXWrapper.dll') $out -Force
Write-Host "Done: $out\ADLXBindings.dll, $out\ADLXWrapper.dll"
