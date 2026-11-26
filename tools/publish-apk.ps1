param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [switch]$Push
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

if (-not $env:JAVA_HOME) {
    $env:JAVA_HOME = 'C:\Program Files\Android\openjdk\jdk-21.0.8'
}
if (-not $env:ANDROID_HOME) {
    $env:ANDROID_HOME = 'C:\Program Files (x86)\Android\android-sdk'
}

Write-Host "Building BusPing $Version" -ForegroundColor Cyan
dotnet build "$repo\BusPing\BusPing.csproj" -f net10.0-android `
    -p:EmbedAssembliesIntoApk=true -v q
if ($LASTEXITCODE -ne 0) {
    throw "Build failed"
}

$apk = Get-ChildItem "$repo\BusPing\bin\Debug\net10.0-android" -Filter '*Signed.apk' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

$outDir = Join-Path $repo 'artifacts'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$name = "BusPing-$Version.apk"
$target = Join-Path $outDir $name
Copy-Item $apk.FullName $target -Force
Write-Host "APK: $target" -ForegroundColor Green

if ($Push) {
    $adb = Join-Path $env:ANDROID_HOME 'platform-tools\adb.exe'
    & $adb push $target "/sdcard/Download/$name"
    Write-Host "Pushed to /sdcard/Download/$name" -ForegroundColor Green
}
