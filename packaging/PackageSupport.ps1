$PetAppId = 'DesktopPet.ProkyHoof'
$PetManifestName = '.desktop-pet-install.json'

function Assert-PetPlainPath([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '安装和数据目录不能使用符号链接或目录联接。' }
        }
        $parent = Split-Path -Parent $current
        if ($parent -eq $current) { break }
        $current = $parent
    }
}

function Resolve-PetInstallPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -notmatch '^[A-Za-z]:[\\/]') { throw '请选择本机磁盘上的完整安装目录。' }
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $blocked = @([IO.Path]::GetPathRoot($full).TrimEnd('\'), $env:WINDIR)
    foreach ($special in @('UserProfile','Desktop','MyDocuments','LocalApplicationData','ApplicationData','ProgramFiles','ProgramFilesX86','CommonApplicationData','Programs')) {
        $value = [Environment]::GetFolderPath($special)
        if ($value) { $blocked += [IO.Path]::GetFullPath($value).TrimEnd('\') }
    }
    if ($full -in $blocked -or $full.StartsWith($env:WINDIR.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '请在所选位置创建独立的桌宠文件夹，不要直接使用系统目录或磁盘根目录。' }
    Assert-PetPlainPath $full
    return $full
}

function Get-PetChildPath([string]$Root, [string]$Relative) {
    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    if ([IO.Path]::IsPathRooted($Relative) -or $Relative -match '(^|[\\/])\.\.([\\/]|$)|:') { throw '安装清单包含无效路径。' }
    $full = [IO.Path]::GetFullPath((Join-Path $fullRoot $Relative))
    if (-not $full.StartsWith($fullRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '路径超出安装目录。' }
    Assert-PetPlainPath $full
    return $full
}

function Read-PetManifest([string]$Root) {
    $path = Get-PetChildPath $Root $PetManifestName
    $manifest = [IO.File]::ReadAllText($path) | ConvertFrom-Json
    if ($manifest.appId -ne $PetAppId -or @($manifest.files).Count -lt 3) { throw '安装清单无效，未修改任何文件。' }
    foreach ($relative in $manifest.files) {
        if ($relative -notmatch '^(DesktopPet\.exe|Uninstall\.ps1|PackageSupport\.ps1|assets[\\/]keyboard-knight[\\/](soft-style-arms\.png|rig[\\/]([\w-]+\.png|layers\.tsv|eyelid-(left|right)[\\/]\d{2}\.png)))$') { throw '安装清单包含非程序文件。' }
        $file = Get-PetChildPath $Root $relative
        if (Test-Path -LiteralPath $file -PathType Container) { throw '安装文件被目录替代，无法继续。' }
    }
    return $manifest
}

function Test-PetWritableDirectory([string]$Root) {
    Assert-PetPlainPath $Root
    [void][IO.Directory]::CreateDirectory($Root)
    $probe = Join-Path $Root ('.desktop-pet-write-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try { [IO.File]::WriteAllText($probe, '') }
    catch { throw '所选目录不可写。请选择当前用户可写的目录，或以管理员身份运行安装程序。' }
    finally { if (Test-Path -LiteralPath $probe) { Remove-Item -LiteralPath $probe -Force } }
}

function Grant-PetDataAccess([string]$Root) {
    # An elevated install must still let this Windows user save data normally.
    Assert-PetPlainPath $Root
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl = Get-Acl -LiteralPath $Root
    $rule = New-Object Security.AccessControl.FileSystemAccessRule($identity,'Modify','ContainerInherit,ObjectInherit','None','Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $Root -AclObject $acl
}

function Copy-PetPersonalData([string]$Source, [string]$Target) {
    if (-not (Test-Path -LiteralPath $Source -PathType Container)) { return }
    foreach ($name in @('keyboard_pet_config.json','input_bindings.tsv','ai_chat_config.json','ai_chat_config.json.bak')) {
        $from = Get-PetChildPath $Source $name
        $to = Get-PetChildPath $Target $name
        if ((Test-Path -LiteralPath $from -PathType Leaf) -and -not (Test-Path -LiteralPath $to)) {
            [void][IO.Directory]::CreateDirectory((Split-Path -Parent $to))
            [IO.File]::Copy($from, $to, $false)
        }
    }
    $calendar = Get-PetChildPath $Source 'data/calendar'
    if (Test-Path -LiteralPath $calendar -PathType Container) {
        Assert-PetPlainTree $calendar
        foreach ($file in Get-ChildItem -LiteralPath $calendar -Recurse -File -Force) {
            if ($file.Name -notmatch '\.json(\.bak)?$') { continue }
            $to = Get-PetChildPath $Target ('data/calendar/' + $file.FullName.Substring($calendar.Length + 1))
            if (-not (Test-Path -LiteralPath $to)) {
                [void][IO.Directory]::CreateDirectory((Split-Path -Parent $to))
                [IO.File]::Copy($file.FullName, $to, $false)
            }
        }
    }
}

function Assert-PetPlainTree([string]$Root) {
    Assert-PetPlainPath $Root
    if (-not (Test-Path -LiteralPath $Root)) { return }
    foreach ($item in Get-ChildItem -LiteralPath $Root -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '数据目录包含链接，未执行删除或迁移。' }
        if ($item.PSIsContainer) { Assert-PetPlainTree $item.FullName }
    }
}

function Set-PetShortcuts([string]$Root, [string[]]$Paths) {
    $shell = New-Object -ComObject WScript.Shell
    foreach ($path in $Paths) {
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
        $link = $shell.CreateShortcut($path)
        $link.TargetPath = Join-Path $Root 'DesktopPet.exe'
        $link.WorkingDirectory = $Root
        $link.IconLocation = (Join-Path $Root 'DesktopPet.exe') + ',0'
        $link.Description = '白发小骑士 · 键鼠桌宠'
        $link.Save()
    }
}

function Remove-PetShortcuts([string]$Root, [string[]]$Paths) {
    $shell = New-Object -ComObject WScript.Shell
    foreach ($path in $Paths) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $link = $shell.CreateShortcut($path)
            if ($link.TargetPath -eq (Join-Path $Root 'DesktopPet.exe')) { Remove-Item -LiteralPath $path -Force }
        }
    }
}

function Remove-PetProgramFiles([string]$Root, $Manifest) {
    $directories = @()
    foreach ($relative in $Manifest.files) {
        $file = Get-PetChildPath $Root $relative
        if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file -Force }
        $parent = Split-Path -Parent $file
        while ($parent -and $parent -ne $Root) { $directories += $parent; $parent = Split-Path -Parent $parent }
    }
    foreach ($folder in $directories | Sort-Object -Unique | Sort-Object Length -Descending) {
        if ((Test-Path -LiteralPath $folder -PathType Container) -and @(Get-ChildItem -LiteralPath $folder -Force).Count -eq 0) { Remove-Item -LiteralPath $folder -Force }
    }
    $manifestPath = Get-PetChildPath $Root $PetManifestName
    if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath -Force }
}

function New-PetInstallForm([string]$InitialRoot, [bool]$LegacyAvailable, [bool]$ImportLegacy) {
    $form = New-Object Windows.Forms.Form
    $form.Text = '安装白发小骑士'
    $form.ClientSize = New-Object Drawing.Size(600,300)
    $form.Font = New-Object Drawing.Font('Microsoft YaHei UI',9)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $title = New-Object Windows.Forms.Label
    $title.Text = '选择安装目录'
    $title.Font = New-Object Drawing.Font($form.Font.FontFamily,12,[Drawing.FontStyle]::Bold)
    $title.SetBounds(20,20,550,30)
    $path = New-Object Windows.Forms.TextBox
    $path.Text = $InitialRoot
    $path.Name = 'InstallPath'
    $path.SetBounds(20,65,450,28)
    $browse = New-Object Windows.Forms.Button
    $browse.Text = '浏览…'
    $browse.SetBounds(482,63,98,30)
    $browse.add_Click({
        $dialog = New-Object Windows.Forms.FolderBrowserDialog
        $dialog.Description = '选择白发小骑士的安装文件夹'
        $dialog.SelectedPath = $path.Text
        try { if ($dialog.ShowDialog($form) -eq 'OK') { $path.Text = $dialog.SelectedPath } }
        finally { $dialog.Dispose() }
    }.GetNewClosure())
    $description = New-Object Windows.Forms.Label
    $description.Text = '个人配置、API 设置及日历记录均保存在所选目录的 user-data 文件夹。请选择当前用户可写的独立文件夹。'
    $description.SetBounds(20,108,560,44)
    $desktop = New-Object Windows.Forms.CheckBox
    $desktop.Text = '创建桌面快捷方式'
    $desktop.Name = 'DesktopShortcut'
    $desktop.Checked = $true
    $desktop.SetBounds(20,159,560,26)
    $import = New-Object Windows.Forms.CheckBox
    $import.Text = if ($LegacyAvailable) { '导入旧版个人配置和日历（保留原文件）' } else { '导入旧版个人配置和日历（未检测到旧版数据）' }
    $import.Checked = $ImportLegacy
    $import.Enabled = $LegacyAvailable
    $import.SetBounds(20,189,560,26)
    $install = New-Object Windows.Forms.Button
    $install.Text = '安装'
    $install.SetBounds(370,246,100,32)
    $cancel = New-Object Windows.Forms.Button
    $cancel.Text = '取消'
    $cancel.DialogResult = 'Cancel'
    $cancel.SetBounds(482,246,98,32)
    $install.add_Click({
        try {
            $chosen = Resolve-PetInstallPath $path.Text
            Test-PetWritableDirectory $chosen
            $form.Tag = [pscustomobject]@{InstallRoot=$chosen;DesktopShortcut=$desktop.Checked;ImportLegacy=$import.Checked}
            $form.DialogResult = 'OK'
        } catch { [void][Windows.Forms.MessageBox]::Show($form,$_.Exception.Message,'无法安装','OK','Error') }
    }.GetNewClosure())
    $form.Controls.AddRange(@($title,$path,$browse,$description,$desktop,$import,$install,$cancel))
    $form.AcceptButton = $install
    $form.CancelButton = $cancel
    return $form
}
