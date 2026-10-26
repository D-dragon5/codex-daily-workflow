param([string]$OutputFolder=(Join-Path ([IO.Path]::GetTempPath()) ('codex-board-smoke-'+[Guid]::NewGuid().ToString('N'))))
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputFolder)
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..')).TrimEnd('\')
if($out.Equals($repository,[StringComparison]::OrdinalIgnoreCase) -or $out.StartsWith($repository+'\',[StringComparison]::OrdinalIgnoreCase)){throw '冒烟检查输出目录必须位于公开仓库之外。'}
New-Item -ItemType Directory -Path $out -Force | Out-Null
& (Join-Path $PSScriptRoot '..\build.ps1') -OutputFolder $out -SkipDemo
$configPath=Join-Path $out 'workflow.config.json'
$taskPath=Join-Path $out 'synthetic-tasks.json'
$ledgerPath=Join-Path $out 'synthetic-ledger.md'
$settingsPath=Join-Path $out 'untouched-settings.json'
$plannerPath=Join-Path $out 'untouched-planner.json'
$encoding=New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText($taskPath,'{"schemaVersion":1,"tasks":[{"id":"synthetic_A","title":"合成验证任务","status":"进行中","next":"展开查看详情","progress":"示例进展","requirement":"仅用于隔离测试","deadlineNote":"日期待定"}]}',$encoding)
[IO.File]::WriteAllText($ledgerPath,"# Synthetic legacy ledger`nNo marked task table; use the synthetic JSON.",$encoding)
# These invalid sentinels make accidental preference/planner loading observable.
[IO.File]::WriteAllText($settingsPath,'synthetic settings sentinel',$encoding)
[IO.File]::WriteAllText($plannerPath,'synthetic planner sentinel',$encoding)
$config=@{schemaVersion=1;timezone='UTC';tasksPath=$taskPath;ledgerPath=$ledgerPath;overviewPath='synthetic-overview.xlsx';detailsPath='synthetic-details.html';weeklyReportsDir='synthetic-weekly';plannerPath=$plannerPath;settingsPath=$settingsPath}
[IO.File]::WriteAllText($configPath,($config|ConvertTo-Json -Depth 6),$encoding)
$before=@{}
foreach($path in @($configPath,$taskPath,$ledgerPath,$settingsPath,$plannerPath)){$before[$path]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
$exe=Join-Path $out 'DesktopBoard.exe'
$checkOut=Join-Path $out 'check-data.json';$checkError=Join-Path $out 'check-data-error.txt'
$process=Start-Process -FilePath $exe -ArgumentList @('--check-data',('"'+$taskPath+'"'),'--config',('"'+$configPath+'"')) -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput $checkOut -RedirectStandardError $checkError
if($process.ExitCode -ne 0){throw ('只读数据检查失败：'+[IO.File]::ReadAllText($checkError))}
$checked=Get-Content -LiteralPath $checkOut -Raw -Encoding UTF8|ConvertFrom-Json
if(-not $checked.passed -or $checked.configuredTimezone -ne 'UTC' -or @($checked.tasks).Count -ne 1 -or $checked.tasks[0].title -ne '合成验证任务'){throw '只读数据检查结果或UTF-8编码错误。'}
$process=Start-Process -FilePath $exe -ArgumentList @('--smoke-test','--config',('"'+$configPath+'"')) -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(45000)){Stop-Process -Id $process.Id;throw '隔离冒烟检查超时；仅终止了本脚本启动的测试进程。'}
if($process.ExitCode -ne 0){throw ('隔离冒烟检查失败：'+[IO.File]::ReadAllText((Join-Path $out 'smoke-error.txt')))}
$result=Get-Content -LiteralPath (Join-Path $out 'smoke-result.json') -Raw -Encoding UTF8|ConvertFrom-Json
if(-not $result.passed -or $result.startupChanged -or $result.desktopIconsRead -or $result.existingPreferencesRead){throw '隔离冒烟检查结果或隔离声明错误。'}
foreach($path in $before.Keys){if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $before[$path]){throw ('输入文件被测试改写：'+$path)}}
Write-Output ('PASS: isolated GUI smoke and read-only data check; '+@($result.tests).Count+' GUI checks; artifacts: '+$out)
