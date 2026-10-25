param([string]$OutputFolder=(Join-Path $PSScriptRoot 'dist'),[switch]$SkipDemo)
$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){throw '桌面日程板需要 Windows 10/11；其他平台可单独使用每日回顾模块。'}
$source=Join-Path $PSScriptRoot 'src'
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if(-not(Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))){$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'}
if(-not(Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))){throw '未找到 .NET Framework 编译器，请启用或安装 .NET Framework 4.8 桌面运行环境。'}
New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
$out=[IO.Path]::GetFullPath($OutputFolder)
$refs=@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\UIAutomationClient.dll','WPF\UIAutomationTypes.dll')
$arguments=@('/nologo','/target:winexe','/platform:anycpu','/optimize+','/codepage:65001',('/resource:'+(Join-Path $source 'Theme.xaml')+',Theme.xaml'),('/win32manifest:'+(Join-Path $source 'app.manifest')),('/out:'+(Join-Path $out 'DesktopBoard.exe')))
foreach($ref in $refs){$item=Join-Path $framework $ref;if(-not(Test-Path -LiteralPath $item)){throw ('缺少桌面程序集：'+$ref)};$arguments+=('/reference:'+$item)}
foreach($name in @('Models.cs','NativeDesktop.cs','IconGeometry.cs','Program.cs')){$arguments+=(Join-Path $source $name)}
& (Join-Path $framework 'csc.exe') @arguments
if($LASTEXITCODE -ne 0){throw '编译失败'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE-UPSTREAM.txt') -Destination (Join-Path $out 'LICENSE-UPSTREAM.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD_PARTY.md') -Destination (Join-Path $out 'THIRD_PARTY.md') -Force
# A fresh standalone build gets synthetic examples; existing data is never replaced.
if(-not $SkipDemo){
    if(-not(Test-Path -LiteralPath (Join-Path $out 'workflow.config.json'))){Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'examples\workflow.config.json') -Destination (Join-Path $out 'workflow.config.json')}
    New-Item -ItemType Directory -Path (Join-Path $out 'data') -Force | Out-Null
    foreach($file in @('tasks.json','每日任务与科研总账.md')){if(-not(Test-Path -LiteralPath (Join-Path $out ('data\'+$file)))){Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('examples\data\'+$file)) -Destination (Join-Path $out ('data\'+$file))}}
}
Write-Output ('已构建：'+(Join-Path $out 'DesktopBoard.exe'))
