# 白发小骑士 · 键鼠桌宠

Windows 透明桌面宠物，使用 C#、WinForms 和 .NET Framework 编译。支持两种白发造型、键鼠动作反馈、打字表情与骑士盔、音乐律动、文字 AI 聊天、日历计划和本地记录。

## 安装与运行

- 系统：Windows 10/11，需启用 .NET Framework 4.x（通常已内置）。
- 安装版：运行 `DesktopPet-1.0.0-Setup.exe`，确认后安装到 `%LOCALAPPDATA%\Programs\DesktopPet`，无需管理员权限。从开始菜单“白发小骑士”启动，可在系统“已安装的应用”中卸载。
- 便携版：解压 `DesktopPet-1.0.0-portable.zip`，运行 `DesktopPet.exe`。个人数据同样保存在用户目录。
- 源码版：双击 `启动桌宠.bat`，首次运行自动构建。源码有更新时会重新编译。
- 安装包和 ZIP 是构建输出，不提交 Git；可从 GitHub Actions 的 `DesktopPet-Windows` 构建产物下载。

右键人物可以调整大小、发型、按键显示方式、鼠标增幅、保持置顶、开机自启动和音乐律动，也可以打开日历、AI 设置或退出。单击人物展开文字输入，Enter 发送；中文输入法选词期间不会发送。拖动人物移动位置。

## 个人配置与 API 隔离

所有正常运行产生的数据保存在 `%LOCALAPPDATA%\DesktopPet`，与源码和安装目录分开：

| 路径 | 内容 |
| --- | --- |
| `ai_chat_config.json` / `.bak` | API 地址、模型、角色提示及加密密钥 |
| `keyboard_pet_config.json` | 位置、大小、发型、动作等偏好 |
| `input_bindings.tsv` | 个人按键绑定和鼠标增幅 |
| `data/calendar/` | 日历计划、记录、备份和恢复快照 |
| `.legacy-migrated` | 一次性旧数据迁移标记 |

在右键“AI 接口设置…”中填写 OpenAI 兼容地址、API Key 和模型。密钥使用 Windows DPAPI 按当前 Windows 用户加密保存，不能直接跨账户使用；换账户后重新填写。示例见 `config/ai_chat_config.example.json`，不要将真实配置复制进仓库。服务商公开地址和模型预设保留在源码中，个人选择单独保存。

首次运行会从程序旁的旧版配置和 `data/calendar/` 复制数据到用户目录，保留旧文件，不覆盖用户目录已有文件。迁移完成后不会再次导入已删除的旧凭据。升级沿用用户目录；卸载保留用户数据。备份或迁移日历时复制整个用户数据目录；跨 Windows 账户需要重新设置 API Key。

`.gitignore` 排除了旧位置的个人配置、日历数据、备份、EXE、安装包、预览及构建缓存。聊天上下文只在内存保留，不写入仓库。键鼠监听不保存输入日志，AI 请求只发送主动提交的文字与聊天上下文；当前聊天不调用麦克风或语音服务。

## 构建与检查

在项目根目录执行 Windows PowerShell：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Build-Installer.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-Package.ps1
```

构建调用 Windows 自带的 `.NET Framework\v4.0.30319\csc.exe`；安装包使用 Windows 自带 IExpress，无第三方打包工具依赖。`Build-Installer.ps1 -Version 1.0.1` 可指定版本，输出位于 `dist/`，包括安装 EXE、便携 ZIP 和 SHA-256 校验清单。打包从运行时图层清单逐项复制素材，不包含个人设置、日历、原始设计图、预览或测试代码。

`Test-Package.ps1` 将安装包解压到临时目录，检查私有文件排除、安装、覆盖升级、自检与卸载，不注册真实安装或更改真实个人配置。完整回归测试另覆盖动画、透明边界、窗口交互、日历、API 协议与密钥加密。CI 执行构建、自检与安装包生命周期检查；原生桌面交互回归适合在本地 Windows 会话运行。

安装脚本提供确认窗口、开始菜单快捷方式、当前用户卸载入口及升级时的退出保护。安装包尚未使用代码签名证书签名。

## 项目结构

- 根目录 `*.cs`：桌宠窗口、动画、输入、统一界面、聊天、日历和音量检测。
- `assets/keyboard-knight/rig/` 与 `soft-style-arms.png`：运行时素材；其中 `helmet-assembled-reference.png` 供回归测试检查接缝，不进入安装包。
- `assets/keyboard-knight/` 的其余源图与提示词：当前素材的重建资料。
- `tools/`：启动、构建、打包、素材重建、预览和性能检查。
- `tests/`：本地回归与安装包检查。
- `packaging/`：安装、升级与卸载脚本。
- `config/`：不含真实密钥的配置示例。
- `.github/workflows/windows.yml`：自动构建并保存安装产物。

`tools/Preview-Ui.ps1`、`tools/Preview-BoundInput.ps1` 可重新生成预览。生成的 `assets/previews/` 不进入 Git。已停用的背剑、旧道具与历史头盔设计不参与当前构建。

## GitHub 管理

仓库：<https://github.com/ProkyHoof/desktop_proky>

修改源码后提交源文件与必要素材；安装程序通过 Actions 产物分发，或手动上传到 GitHub Release。提交前可以运行 `git status --short` 和 `git diff --cached --stat` 检查内容。不要使用 `git add -f` 强行提交个人配置。
