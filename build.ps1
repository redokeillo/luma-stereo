param([switch]$Test, [switch]$RenderPreview, [switch]$Package)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = $PSScriptRoot
$deps = Join-Path $projectRoot '.deps'
$output = Join-Path $projectRoot 'bin\Release'
New-Item -ItemType Directory -Force -Path $deps, $output | Out-Null
$packageFile = Join-Path $deps 'naudio.1.10.0.zip'
$expectedHash = 'E4A80EED41DAD794695F9C114432DE4667D20BDFA80825AA94D159708CF29217'
if (-not (Test-Path -LiteralPath $packageFile) -or (Get-FileHash -LiteralPath $packageFile -Algorithm SHA256).Hash -ne $expectedHash) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/naudio/1.10.0/naudio.1.10.0.nupkg' -UseBasicParsing -OutFile ($packageFile + '.download')
            if ((Get-FileHash -LiteralPath ($packageFile + '.download') -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Downloaded package checksum mismatch.' }
            Move-Item -LiteralPath ($packageFile + '.download') -Destination $packageFile -Force
            break
        } catch { if ($attempt -eq 3) { throw }; Start-Sleep -Seconds 2 }
    }
}
if ((Get-FileHash -LiteralPath $packageFile -Algorithm SHA256).Hash -ne $expectedHash) { throw 'NAudio package checksum mismatch. Remove .deps/naudio.1.10.0.zip and retry.' }
Expand-Archive -LiteralPath $packageFile -DestinationPath (Join-Path $deps 'naudio') -Force
$naudio = Join-Path $deps 'naudio\lib\net35\NAudio.dll'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows x64 with .NET Framework 4.8 is required.' }
$arguments = @('/nologo', '/platform:x64', '/optimize+', '/reference:System.Core.dll', '/reference:System.Xaml.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', "/reference:$naudio")
foreach ($name in @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll')) { $arguments += '/reference:' + (Join-Path $framework "WPF\$name") }
$arguments += '/resource:' + (Join-Path $projectRoot 'src\Main.xaml') + ',Main.xaml'
$arguments += '/win32icon:' + (Join-Path $projectRoot 'assets\redo-luma.ico')
$arguments += '/resource:' + (Join-Path $projectRoot 'assets\redo-luma.ico') + ',redo-luma.ico'
$arguments += '/resource:' + (Join-Path $projectRoot 'assets\psz-signature.png') + ',psz-signature.png'
$source = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compiler @arguments /target:winexe ('/out:' + (Join-Path $output 'LumaStereo.exe')) @source
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Copy-Item -LiteralPath $naudio -Destination (Join-Path $output 'NAudio.dll')
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\App.config') -Destination (Join-Path $output 'LumaStereo.exe.config')
foreach ($name in @('LICENSE','NAudio-LICENSE.txt','README.md')) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $output }
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\USER-GUIDE.zh-CN.md') -Destination (Join-Path $output '使用说明.md')
if ($Test -or $RenderPreview) {
    $testOutput = Join-Path $projectRoot 'bin\Tests'
    New-Item -ItemType Directory -Force -Path $testOutput | Out-Null
    & $compiler @arguments /target:exe /main:Tests ('/out:' + (Join-Path $testOutput 'Tests.exe')) @source (Join-Path $projectRoot 'tests\Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    Copy-Item -LiteralPath $naudio -Destination $testOutput
    if ($RenderPreview) { & (Join-Path $testOutput 'Tests.exe') (Join-Path $projectRoot 'docs') } else { & (Join-Path $testOutput 'Tests.exe') }
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
New-Item -ItemType Directory -Force -Path (Join-Path $output 'docs') | Out-Null
Copy-Item -Path (Join-Path $projectRoot 'docs\*') -Destination (Join-Path $output 'docs') -Recurse -Force
if ($Package) {
    $dist = Join-Path $projectRoot 'dist'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    Compress-Archive -Path (Join-Path $output '*') -DestinationPath (Join-Path $dist 'LumaStereo-1.1.2-win-x64.zip') -Force
}
Write-Host "Built: $output\LumaStereo.exe"
