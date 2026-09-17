$ErrorActionPreference = 'Stop'
$quotaFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$quotaArgs = @('/nologo', '/target:winexe', ('/out:' + (Join-Path $PSScriptRoot 'CodexQuota.exe')),
    ('/win32icon:' + (Join-Path $PSScriptRoot 'CodexQuota.ico')),
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Web.Extensions.dll',
    '/reference:Microsoft.CSharp.dll', '/reference:System.Xaml.dll',
    ('/reference:' + (Join-Path $quotaFramework 'WPF\PresentationFramework.dll')),
    ('/reference:' + (Join-Path $quotaFramework 'WPF\PresentationCore.dll')),
    ('/reference:' + (Join-Path $quotaFramework 'WPF\WindowsBase.dll')),
    (Join-Path $PSScriptRoot 'QuotaWidget.cs'), (Join-Path $PSScriptRoot 'QuotaClient.cs'))
& (Join-Path $quotaFramework 'csc.exe') @quotaArgs
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed. Close CodexQuota before building.' }
