$ErrorActionPreference = 'Stop'
$quotaRoot = Split-Path $PSScriptRoot -Parent
$quotaBin = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $quotaBin | Out-Null
$quotaCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$quotaTestExe = Join-Path $quotaBin 'QuotaClientTests.exe'
& $quotaCompiler /nologo /target:exe ('/out:' + $quotaTestExe) /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'QuotaClientTests.cs') (Join-Path $quotaRoot 'QuotaClient.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $quotaTestExe
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
