param([string]$OutputFolder=(Join-Path $PSScriptRoot '..\dist\tests'))
$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){throw '本模块数据检查使用 .NET Framework 编译器，需要 Windows。'}
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if(-not(Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))){$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'}
New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
$out=[IO.Path]::GetFullPath($OutputFolder)
$argsList=@('/nologo','/target:exe','/codepage:65001',('/out:'+(Join-Path $out 'ModelsTests.exe')),('/reference:'+(Join-Path $framework 'System.Web.Extensions.dll')),('/reference:'+(Join-Path $framework 'System.Core.dll')),(Join-Path $PSScriptRoot '..\src\Models.cs'),(Join-Path $PSScriptRoot 'ModelsTests.cs'))
& (Join-Path $framework 'csc.exe') @argsList
if($LASTEXITCODE -ne 0){throw '数据测试编译失败'}
& (Join-Path $out 'ModelsTests.exe') (Join-Path $out 'fixtures')
if($LASTEXITCODE -ne 0){throw '数据测试失败'}
