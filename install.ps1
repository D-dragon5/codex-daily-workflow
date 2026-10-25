param(
    [ValidateSet('all','review','widget')][string]$Component='all',
    [string]$InstallRoot=(Join-Path $env:LOCALAPPDATA 'CodexDailyWorkflow'),
    [string]$DataDir=(Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'CodexDailyWorkflow'),
    [string]$SkillRoot=(Join-Path ([Environment]::GetFolderPath('UserProfile')) '.agents\skills'),
    [switch]$NoShortcut,
    [switch]$StartWidget
)
$ErrorActionPreference='Stop'
$source=$PSScriptRoot
function FullPath([string]$p){return [IO.Path]::GetFullPath($p)}
function WriteJson([string]$p,$value){$parent=Split-Path -Parent $p;New-Item -ItemType Directory -Path $parent -Force | Out-Null;[IO.File]::WriteAllText($p,($value|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))}
function EnsureParent([string]$p){New-Item -ItemType Directory -Path (Split-Path -Parent $p) -Force | Out-Null}
function ResolveConfigPath($config,[string]$key){$p=[string]$config.$key;if([IO.Path]::IsPathRooted($p)){return FullPath $p};return FullPath (Join-Path $InstallRoot $p)}
function InstallSkill([string]$from,[string]$name){
    $to=Join-Path $SkillRoot $name
    if(Test-Path -LiteralPath $to){
        if(-not(Test-Path -LiteralPath (Join-Path $to '.codex-daily-workflow-owned')) -or (Get-Content -LiteralPath (Join-Path $to '.codex-daily-workflow-owned') -Raw -Encoding UTF8).Trim() -ne 'codex-daily-workflow'){throw ('已有同名技能，未覆盖：'+$to+'。请指定其他 -SkillRoot。')}
    }
    New-Item -ItemType Directory -Path $to -Force | Out-Null
    Get-ChildItem -LiteralPath $from -Force | Copy-Item -Destination $to -Recurse -Force
    [IO.File]::WriteAllText((Join-Path $to '.codex-daily-workflow-owned'),'codex-daily-workflow',[Text.UTF8Encoding]::new($false))
    return $to
}
$InstallRoot=FullPath $InstallRoot;$DataDir=FullPath $DataDir;$SkillRoot=FullPath $SkillRoot
$marker=Join-Path $InstallRoot '.codex-workflow-install.json'
if((Test-Path -LiteralPath $InstallRoot) -and -not(Test-Path -LiteralPath $marker) -and @(Get-ChildItem -LiteralPath $InstallRoot -Force).Count -gt 0){throw '安装目录非空且不属于本项目，未修改。请选择新的 -InstallRoot。'}
if(Test-Path -LiteralPath $marker){$owned=Get-Content -LiteralPath $marker -Raw -Encoding UTF8|ConvertFrom-Json;if($owned.package -ne 'codex-daily-workflow'){throw '安装目录标记不属于本项目，未修改。'}}
$reviewSource=Join-Path $source 'modules\daily-review'
$widgetSource=Join-Path $source 'modules\desktop-board'
$components=@()
if(($Component -eq 'all' -or $Component -eq 'review') -and (Test-Path -LiteralPath $reviewSource)){$components+='review'}
if(($Component -eq 'all' -or $Component -eq 'widget') -and (Test-Path -LiteralPath $widgetSource)){$components+='widget'}
if($components.Count -eq 0 -or ($Component -ne 'all' -and $components -notcontains $Component)){throw '安装包缺少所选模块。'}
# Preflight skills before writing to the selected destinations.
$wantedSkills=@('setup-daily-workflow')
if($components -contains 'review'){$wantedSkills+='daily-review'}
if($components -contains 'widget'){$wantedSkills+='desktop-board'}
foreach($name in $wantedSkills){$to=Join-Path $SkillRoot $name;if(Test-Path -LiteralPath $to){$ownedSkill=Join-Path $to '.codex-daily-workflow-owned';if(-not(Test-Path -LiteralPath $ownedSkill) -or (Get-Content -LiteralPath $ownedSkill -Raw -Encoding UTF8).Trim() -ne 'codex-daily-workflow'){throw ('已有同名技能，未覆盖：'+$to)}}}
New-Item -ItemType Directory -Path $InstallRoot,$DataDir,$SkillRoot -Force | Out-Null
if(-not(Test-Path -LiteralPath $marker)){WriteJson $marker @{package='codex-daily-workflow';status='installing';components=@();appPath=$null}}
if(Test-Path -LiteralPath (Join-Path $source 'docs')){Copy-Item -LiteralPath (Join-Path $source 'docs') -Destination $InstallRoot -Recurse -Force}
$configPath=Join-Path $InstallRoot 'workflow.config.json'
if(Test-Path -LiteralPath $configPath){$config=Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json}
else {
 $config=[ordered]@{schemaVersion=1;timezone='local';ledgerPath=(Join-Path $DataDir '每日任务与科研总账.md');tasksPath=(Join-Path $DataDir 'tasks.json');overviewPath=(Join-Path $DataDir '任务总览.xlsx');detailsPath=(Join-Path $DataDir '任务详情.html');weeklyReportsDir=(Join-Path $DataDir 'weekly-reports');plannerPath=(Join-Path $DataDir '日程配置.json');settingsPath=(Join-Path $InstallRoot 'desktop-board\显示设置.json');dailyTime='05:30';weeklySubmissionDay=$null}
 WriteJson $configPath $config
}
$config=Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
$ledger=ResolveConfigPath $config 'ledgerPath';$tasks=ResolveConfigPath $config 'tasksPath'
if($components -contains 'review' -and -not(Test-Path -LiteralPath $ledger) -and (Test-Path -LiteralPath $tasks)){throw '已有任务JSON但缺少主账本。为保护原数据，未创建示例账本或刷新视图；请先将任务导入账本，或指定新的-DataDir。'}
$skills=@();$appPath=$null;$refreshed=$false
if(Test-Path -LiteralPath (Join-Path $source 'skills\setup-daily-workflow')){$skills+=InstallSkill (Join-Path $source 'skills\setup-daily-workflow') 'setup-daily-workflow'}
if($components -contains 'review'){
 $runtime=Join-Path $InstallRoot 'daily-review'
 New-Item -ItemType Directory -Path $runtime -Force | Out-Null
 Get-ChildItem -LiteralPath $reviewSource -Force | Where-Object {$_.Name -notin @('dist','__pycache__')} | Copy-Item -Destination $runtime -Recurse -Force
 $skills+=InstallSkill (Join-Path $reviewSource 'skills\daily-review') 'daily-review'
 if(-not(Test-Path -LiteralPath $ledger)){EnsureParent $ledger;Copy-Item -LiteralPath (Join-Path $reviewSource 'skills\daily-review\assets\ledger-template.md') -Destination $ledger}
 $python=Get-Command python -ErrorAction SilentlyContinue
 $py=Get-Command py -ErrorAction SilentlyContinue
 if($python -and $python.Source -notmatch 'WindowsApps'){
   & $python.Source (Join-Path $runtime 'scripts\refresh.py') --config $configPath
   if($LASTEXITCODE -ne 0){throw '视图刷新失败；账本保留，未登记为完整安装。'}
   $refreshed=$true
 } elseif($py){
   & $py.Source -3 (Join-Path $runtime 'scripts\refresh.py') --config $configPath
   if($LASTEXITCODE -ne 0){throw '视图刷新失败；账本保留，未登记为完整安装。'}
   $refreshed=$true
 } else {Write-Warning '未找到Python3，技能与模板已安装；请在Codex运行setup技能生成视图。'}
}
if(-not(Test-Path -LiteralPath $tasks)){
 EnsureParent $tasks
 $sample=Join-Path $source 'examples\tasks.example.json'
 if(Test-Path -LiteralPath $sample){Copy-Item -LiteralPath $sample -Destination $tasks}
 else {WriteJson $tasks @{schemaVersion=1;updatedAt=[DateTimeOffset]::Now.ToString('o');timezone=$config.timezone;tasks=@()}}
}
if($components -contains 'widget'){
 $appDir=Join-Path $InstallRoot 'desktop-board'
 & (Join-Path $widgetSource 'build.ps1') -OutputFolder $appDir -SkipDemo
 if(-not $?){throw '日程板构建失败。'}
 $appPath=Join-Path $appDir 'DesktopBoard.exe'
 if(-not(Test-Path -LiteralPath $appPath)){throw '构建未生成DesktopBoard.exe。'}
 $skills+=InstallSkill (Join-Path $widgetSource 'skills\desktop-board') 'desktop-board'
 if(-not $NoShortcut){
  $shell=New-Object -ComObject WScript.Shell
  $shortcutPath=Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Codex每日任务板.lnk'
  if(Test-Path -LiteralPath $shortcutPath){$old=$shell.CreateShortcut($shortcutPath);if($old.TargetPath -ne $appPath){throw '已有同名桌面入口，未覆盖。可用-NoShortcut安装。'}}
  $shortcut=$shell.CreateShortcut($shortcutPath);$shortcut.TargetPath=$appPath;$shortcut.Arguments='--config "'+$configPath+'"';$shortcut.WorkingDirectory=$appDir;$shortcut.Description='今日任务、课程与截止事项';$shortcut.Save()
 }
}
if(Test-Path -LiteralPath $marker){$previous=Get-Content -LiteralPath $marker -Raw -Encoding UTF8|ConvertFrom-Json;$components=@($components+@($previous.components)|Sort-Object -Unique);if(-not $appPath){$appPath=$previous.appPath}}
$metadata=[ordered]@{package='codex-daily-workflow';version='0.1.1';configPath=$configPath;installRoot=$InstallRoot;components=@($components);appPath=$appPath;viewsRefreshed=$refreshed;scheduleRegistered=$false}
WriteJson $marker $metadata
foreach($skill in $skills){WriteJson (Join-Path $skill 'installation.json') $metadata}
Write-Output ('安装完成：'+($components -join ', '))
Write-Output ('配置：'+$configPath)
Write-Output '下一步在Codex发送：使用 $setup-daily-workflow 完成当前安装的本机配置，并按我的时间创建每日回顾任务。'
Write-Output '安装器不会直接复制或改写Codex定时任务文件；首次注册由本机Codex完成。'
if($StartWidget -and $appPath){Start-Process -FilePath $appPath -ArgumentList ('--config "'+$configPath+'"') -WindowStyle Normal}
