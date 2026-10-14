# Compila somente os fontes próprios em src; não lê certificados ou dados do usuário.
param([string]$Output = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$reaCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$reaSources=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& $reaCompiler /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 "/out:$Output\XmlA1.exe" "/win32manifest:$PSScriptRoot\app.manifest" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Xml.Linq.dll /reference:System.Security.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll $reaSources
if($LASTEXITCODE -ne 0){throw 'Falha na compilação'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'XmlA1.exe.config') -Destination $Output
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $Output
Write-Output "Aplicativo: $Output\XmlA1.exe"
