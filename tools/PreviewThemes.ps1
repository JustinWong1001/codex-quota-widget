param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$quotaSource = Split-Path $PSScriptRoot -Parent
$quotaFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$quotaPreview = Join-Path $quotaSource 'ThemePreview.exe'
$quotaArgs = @('/nologo', '/target:exe', '/main:ThemePreview', ('/out:' + $quotaPreview),
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Web.Extensions.dll',
    '/reference:Microsoft.CSharp.dll', '/reference:System.Xaml.dll',
    ('/reference:' + (Join-Path $quotaFramework 'WPF\PresentationFramework.dll')),
    ('/reference:' + (Join-Path $quotaFramework 'WPF\PresentationCore.dll')),
    ('/reference:' + (Join-Path $quotaFramework 'WPF\WindowsBase.dll')),
    (Join-Path $quotaSource 'QuotaWidget.cs'), (Join-Path $quotaSource 'QuotaThemes.cs'),
    (Join-Path $quotaSource 'QuotaClient.cs'), (Join-Path $PSScriptRoot 'ThemePreview.cs'))
& (Join-Path $quotaFramework 'csc.exe') @quotaArgs
if ($LASTEXITCODE -ne 0) { throw 'Preview compilation failed' }
& $quotaPreview $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Preview rendering failed' }
