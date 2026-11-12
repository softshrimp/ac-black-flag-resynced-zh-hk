$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$manifestPath = Join-Path $projectRoot 'data/manifest.json'
$version = (Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json).version
$buildOutput = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $buildOutput -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '找不到本機 .NET Framework C# 編譯器。' }
$exe = Join-Path $buildOutput ('ACBF-zhHK-' + $version + '.exe')
$compileArgs = @('/nologo','/optimize+','/platform:anycpu','/target:winexe',
    '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll',
    ('/win32manifest:' + (Join-Path $projectRoot 'src/administrator.manifest')),('/out:' + $exe),
    ('/resource:' + $manifestPath + ',manifest.json'),
    ('/resource:' + (Join-Path $projectRoot 'data/ui.resource') + ',ui.resource'),
    ('/resource:' + (Join-Path $projectRoot 'data/dialogue.resource') + ',dialogue.resource'),
    (Join-Path $projectRoot 'src/Installer.cs'))
& $compiler @compileArgs
if ($LASTEXITCODE -ne 0) { throw '編譯失敗。' }
$algorithm = [Security.Cryptography.SHA256]::Create()
$stream = [IO.File]::OpenRead($exe)
try { $hash = ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-','').ToLowerInvariant() }
finally { $stream.Dispose(); $algorithm.Dispose() }
Write-Output ($hash + '  ' + [IO.Path]::GetFileName($exe))
Write-Output '安裝檔已建立；本步驟沒有安裝補丁或啟動遊戲。'
