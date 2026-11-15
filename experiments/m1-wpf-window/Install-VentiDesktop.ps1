param(
    [ValidateSet('Install','Uninstall')][string]$Action = 'Install',
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'AerisAlive/Desktop'),
    [string]$ShortcutPath = (Join-Path ([Environment]::GetFolderPath('Programs')) 'VENTI ALIVE.lnk')
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($InstallRoot)
$shortcut = [IO.Path]::GetFullPath($ShortcutPath)
$marker = Join-Path $root '.aeris-alive-install'
$versions = Join-Path $root 'versions'

if ($Action -eq 'Uninstall' -and !(Test-Path -LiteralPath $root)) { return }
for ($ancestor = [IO.DirectoryInfo]::new($root); $null -ne $ancestor; $ancestor = $ancestor.Parent) {
    if ($ancestor.Exists -and ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Installation path contains a link: $($ancestor.FullName)"
    }
}
if ([IO.Path]::GetPathRoot($root) -eq $root -or
    $shortcut.StartsWith($root.TrimEnd('\') + '\',[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Pick a dedicated installation folder and an external shortcut path.'
}
function Assert-PackagePayload([string]$Directory, [System.Collections.IDictionary]$Hashes) {
    $files = foreach ($tree in @('runtime','assets')) {
        $payload = Get-Item -LiteralPath (Join-Path $Directory $tree) -Force
        if (!$payload.PSIsContainer -or ($payload.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "安装包文件夹无效：$tree。请重新解压完整安装包。"
        }
        foreach ($entry in (Get-ChildItem -LiteralPath $payload.FullName -Recurse -Force)) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw '安装包包含链接。请重新解压完整安装包。'
            }
            if (!$entry.PSIsContainer) { $entry }
        }
    }
    if (@($files).Count -ne $Hashes.Count) {
        throw '安装包文件与清单不符。请重新解压完整安装包。'
    }
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($Directory, $file.FullName).Replace('\','/')
        $expected = $Hashes[$relative]
        if ($expected -isnot [string] -or $expected -notmatch '^[A-Fa-f0-9]{64}$' -or
            (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $expected) {
            throw "安装包校验失败：$relative。请重新解压完整安装包。"
        }
    }
}

if ($Action -eq 'Install') {
    $requiredFiles = @(
        'runtime/Aeris.WindowProbe.exe', 'runtime/Aeris.WindowProbe.dll',
        'runtime/Aeris.WindowProbe.deps.json', 'runtime/Aeris.WindowProbe.runtimeconfig.json',
        'runtime/coreclr.dll', 'runtime/hostfxr.dll', 'runtime/hostpolicy.dll',
        'assets/front.png', 'assets/body-v0.2.png', 'assets/blink.png', 'assets/gaze.png',
        'assets/plume.png', 'assets/braid-left.png', 'assets/braid-right.png',
        'assets/cape-left.png', 'assets/cape-right.png',
        'assets/gestures/lyre-ready.png', 'assets/gestures/lyre.png', 'assets/gestures/lyre-pluck.png',
        'assets/gestures/hips.png',
        'assets/gestures/crossed-enter.png', 'assets/gestures/crossed.png'
    )
    foreach ($relativePath in $requiredFiles) {
        $entry = Get-Item -LiteralPath (Join-Path $PSScriptRoot $relativePath) -ErrorAction SilentlyContinue
        if (!$entry -or $entry.PSIsContainer -or $entry.Length -eq 0) {
            throw "安装包缺少必要文件：$relativePath。请重新解压完整安装包。"
        }
    }
}
if ($Action -eq 'Install') {
    $manifest = Join-Path $PSScriptRoot 'package-sha256.json'
    try {
        $hashes = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json -AsHashtable
        if ($hashes -isnot [System.Collections.IDictionary] -or !$hashes.Count) {
            throw 'Invalid SHA-256 manifest.'
        }
    } catch {
        throw '安装包校验清单缺失或无效。请重新解压完整安装包。'
    }
    Assert-PackagePayload $PSScriptRoot $hashes
}
if (Test-Path -LiteralPath $root) {
    $entry = Get-Item -LiteralPath $root -Force
    if (!$entry.PSIsContainer -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        !(Test-Path -LiteralPath $marker -PathType Leaf) -or (Get-Content -Raw -LiteralPath $marker).Trim() -ne 'VENTI ALIVE Desktop 1') {
        throw "The installation folder belongs to another application or contains existing files: $root"
    }
} else {
    New-Item -ItemType Directory -Path $root | Out-Null
    Set-Content -LiteralPath $marker -Value 'VENTI ALIVE Desktop 1' -Encoding ascii
}

$mutex = [Threading.Mutex]::new($false,'Local\AerisAlive.Installation')
try {
    try { $entered = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $entered = $true }
    if (!$entered) { throw 'Another VENTI ALIVE installation is running.' }

    $shell = New-Object -ComObject WScript.Shell
    $managedPrefix = $versions.TrimEnd('\') + '\'
    $runKey = 'HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run'
    $startup = [Microsoft.Win32.Registry]::GetValue($runKey, 'AerisAlive', $null)
    $managedStartup = $startup -is [string] -and $startup.StartsWith(
        ('"' + $managedPrefix), [StringComparison]::OrdinalIgnoreCase)
    if (Test-Path -LiteralPath $shortcut) {
        $existing = $shell.CreateShortcut($shortcut)
        if (!$existing.TargetPath.StartsWith($managedPrefix,[StringComparison]::OrdinalIgnoreCase)) {
            throw "A different shortcut already exists at: $shortcut"
        }
    }

    if ($Action -eq 'Uninstall') {
        # The marker and resolved target restrict removal to the app's installation folder.
        $resolved = (Resolve-Path -LiteralPath $root).ProviderPath
        if ($resolved -ne $root -or ($root.TrimEnd('\') + '\').StartsWith(
            ($PSScriptRoot.TrimEnd('\') + '\'),[StringComparison]::OrdinalIgnoreCase)) {
            throw "Uninstall from an independently extracted copy of the package."
        }
        $active = @(Get-Process -Name Aeris.WindowProbe -ErrorAction SilentlyContinue | Where-Object {
            $_.Path -and $_.Path.StartsWith($managedPrefix,[StringComparison]::OrdinalIgnoreCase)
        })
        if ($active.Count) { throw 'Exit VENTI ALIVE before uninstalling.' }
        $unexpected = @(Get-ChildItem -LiteralPath $resolved -Force | Where-Object Name -notin @('.aeris-alive-install','versions'))
        if ($unexpected.Count) { throw 'The installation folder contains additional files. Move them out before uninstalling.' }
        $reparseEntries = @(Get-ChildItem -LiteralPath $resolved -Recurse -Force -Attributes ReparsePoint)
        if ($reparseEntries.Count) { throw 'The installation tree contains links. Remove them before uninstalling.' }
        Remove-Item -LiteralPath $resolved -Recurse -ErrorAction Stop
        if ($managedStartup) {
            Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'AerisAlive'
        }
        if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut }
        Write-Host "Removed $root"
        return
    }

    if (Test-Path -LiteralPath $versions) {
        $versionEntry = Get-Item -LiteralPath $versions -Force
        if (!$versionEntry.PSIsContainer -or ($versionEntry.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'The installed versions folder is invalid.'
        }
    }

    $destination = Join-Path $versions ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $destination | Out-Null
    foreach ($folder in @('runtime','assets')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $folder) -Destination (Join-Path $destination $folder) -Recurse
    }
    Assert-PackagePayload $destination $hashes
    $runtime = Join-Path $destination 'runtime\Aeris.WindowProbe.exe'
    $assets = Join-Path $destination 'assets'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $shortcut) | Out-Null
    $pendingPath = Join-Path (Split-Path -Parent $shortcut) ("." + [guid]::NewGuid().ToString('N') + ".lnk")
    $updatedStartup = $false
    try {
        $link = $shell.CreateShortcut($pendingPath)
        $link.TargetPath = $runtime
        $link.WorkingDirectory = Split-Path -Parent $runtime
        $link.Arguments = '--preview --sprite "{0}" --motion "{1}" --play' -f (Join-Path $assets 'front.png'),$assets
        $link.Description = 'VENTI ALIVE Windows Desktop'
        $link.Save()
        if ($managedStartup) {
            [Microsoft.Win32.Registry]::SetValue($runKey, 'AerisAlive', ('"{0}" --startup' -f $runtime))
            $updatedStartup = $true
        }
        [IO.File]::Move($pendingPath,$shortcut,$true)
    } catch {
        if ($updatedStartup) { [Microsoft.Win32.Registry]::SetValue($runKey, 'AerisAlive', $startup) }
        throw
    } finally {
        if (Test-Path -LiteralPath $pendingPath) { Remove-Item -LiteralPath $pendingPath }
    }
    [pscustomobject]@{ InstallPath=$destination; Shortcut=$shortcut }
} finally {
    if ($entered) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
