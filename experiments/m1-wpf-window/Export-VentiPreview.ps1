param(
    [string]$PublishDirectory = "$PSScriptRoot/../../artifacts/local/m3-publish",
    [string]$LocalDialogueModelDirectory,
    [string]$AssetDirectory,
    [Parameter(Mandatory)][string]$Destination
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
if (!(Test-Path -LiteralPath (Join-Path $publish 'Aeris.WindowProbe.exe') -PathType Leaf)) {
    throw 'Build the self-contained win-x64 Release preview first.'
}
$destinationPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)
$archivePath = "$destinationPath.zip"
if ((Test-Path -LiteralPath $destinationPath) -or (Test-Path -LiteralPath $archivePath)) {
    throw 'Use a new destination for the preview package.'
}
$modelDirectory = if ($LocalDialogueModelDirectory) {
    $directory = (Resolve-Path -LiteralPath $LocalDialogueModelDirectory).Path
    foreach ($file in @('qwen3-4b-instruct-2507-q8_0.gguf','vulkan-b12386/llama-server.exe','llama.cpp/llama-server.exe')) {
        if (!(Test-Path -LiteralPath (Join-Path $directory $file) -PathType Leaf)) {
            throw "Missing local dialogue file: $file"
        }
    }
    $directory
}
if ($AssetDirectory) {
    $layerRoot = (Resolve-Path -LiteralPath $AssetDirectory).Path
    $frontPath = Join-Path $layerRoot 'front.png'
} else {
    $art = $root
    if (!(Test-Path -LiteralPath (Join-Path $root 'art/review/m2'))) {
        $gitDirectory = git -C $root rev-parse --path-format=absolute --git-common-dir
        if ($LASTEXITCODE -ne 0) { throw 'Supply a licensed asset directory with -AssetDirectory.' }
        $art = Split-Path -Parent $gitDirectory.Trim()
    }
    $layerRoot = Join-Path $art 'art/review/m2'
    $frontPath = Join-Path $art 'art/review/m1/venti-front-v0.2.png'
}
$front = (Resolve-Path -LiteralPath $frontPath).Path
$layers = foreach ($name in @('body-v0.2.png','blink.png','gaze.png','plume.png','braid-left.png','braid-right.png','cape-left.png','cape-right.png')) {
    (Resolve-Path -LiteralPath (Join-Path $layerRoot $name)).Path
}
New-Item -ItemType Directory -Path $destinationPath | Out-Null
Copy-Item -LiteralPath $publish -Destination (Join-Path $destinationPath 'runtime') -Recurse
$assets = (New-Item -ItemType Directory -Path (Join-Path $destinationPath 'assets')).FullName
Copy-Item -LiteralPath $front -Destination (Join-Path $assets 'front.png')
Copy-Item -LiteralPath $layers -Destination $assets
if ($modelDirectory) {
    Set-Content -LiteralPath (Join-Path $assets 'local-model-directory.txt') -Value $modelDirectory -Encoding utf8NoBOM
}
$gestures = Join-Path $layerRoot 'gestures'
$gestureAssets = (New-Item -ItemType Directory -Path (Join-Path $assets 'gestures')).FullName
foreach ($name in @('lyre','lyre-ready','lyre-pluck','hips','crossed','crossed-enter')) {
    Copy-Item -LiteralPath (Join-Path $gestures "$name.png") -Destination $gestureAssets
}
Set-Content -Encoding ascii -LiteralPath (Join-Path $destinationPath 'Start Venti.cmd') -Value @'
@echo off
start "" "%~dp0runtime\Aeris.WindowProbe.exe" --preview --sprite "%~dp0assets\front.png" --motion "%~dp0assets" --play
'@
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-VentiDesktop.ps1') -Destination $destinationPath
Set-Content -Encoding utf8NoBOM -LiteralPath (Join-Path $destinationPath '试用说明.md') -Value @'
# VENTI ALIVE 温迪桌面样片

Windows x64 本机试用。解压后双击 Start Venti.cmd。点击温迪会有目光回应。拖动时温迪会提脚、展开翅膀，身旁会出现风流。松开时翅膀逐渐收拢并接上落地动作。

主控窗口的「动作」区可以触发目光回应、弹竖琴、叉腰、交叉抱臂和小憩。勾选随机待机可让温迪偶尔自己小憩。点一下或拖动可以叫醒他。拨弦时会飘出音符，弹竖琴是无声动作。下方可把温迪召回主屏或退出。

在主控窗口的「主动互动」区可以独立切换主动说话和自主飞行。约 100 秒没有和温迪互动时，他会在身边显示由本地模型生成的第一条短弹幕。停留时间可选 5、7 或 10 秒，默认 7 秒，悬停时暂停。点击弹幕或「回复」即可就地输入。空闲时每隔约 15 秒在当前屏幕随机飞一段，结束后会收翅落地。正在互动或播放动作时，飞行会顺延。抓住他即可接管。

主控窗口「桌面设置」提供「开机自启动」。开启后，在当前用户登录 Windows 时启动温迪，主控窗口最小化到任务栏。

点击控制窗口的「离线聊天」，等模型就绪后输入文字。首次使用时，可通过「选择模型文件夹」定位本机 Qwen 模型与推理程序。此自用包可以预先指定本机路径，模型文件单独存放。聊天窗口打开时，桌宠暂时退出置顶，关窗会停止推理并恢复原来的置顶选项。每次打开离线聊天默认读取已保存的记忆，可随时取消勾选。可在「管理记忆」中自行添加、编辑、删除和导出条目。对话文字保存在当次会话的内存中。

若启动提示指出某个角色素材缺失或无法读取，请把试用包完整解压到新文件夹，再双击 Start Venti.cmd。

设置保存在当前用户的本地应用数据中。在解压目录的 PowerShell 7 中运行 .\Install-VentiDesktop.ps1，可以安装到当前用户并建立开始菜单快捷方式。安装时会核对运行时和素材的 SHA-256。升级前请先退出温迪，再从新包运行同一命令。回退时从之前的包重新运行安装。卸载请运行 .\Install-VentiDesktop.ps1 -Action Uninstall，设置和源压缩包保留。

角色造型基于 HoYoverse《原神》温迪。图层为 VENTI ALIVE 的二创样片，供本地评审。
'@
# Hash the copies that the ZIP will actually contain.
$hashes = [ordered]@{}
foreach ($tree in @('runtime','assets')) {
    Get-ChildItem -LiteralPath (Join-Path $destinationPath $tree) -File -Force -Recurse |
        Sort-Object FullName | ForEach-Object {
            $relative = [IO.Path]::GetRelativePath($destinationPath, $_.FullName).Replace('\','/')
            $hashes[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
}
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destinationPath 'package-sha256.json') -Encoding utf8NoBOM
Compress-Archive -LiteralPath $destinationPath -DestinationPath $archivePath -CompressionLevel Optimal
Get-Item -LiteralPath $archivePath
