# 白发小骑士 · 键鼠桌宠

Windows 透明桌面宠物，使用 C#、WinForms 和 .NET Framework 编译。支持两种白发造型、键鼠动作反馈、打字表情与骑士盔、音乐律动、文字 AI 聊天、日历计划和本地记录。

## 安装与运行

- 系统：Windows 10/11，需启用 .NET Framework 4.x（通常已内置）。
- 安装版：运行 `DesktopPet-1.1.0-Setup.exe`，在安装界面填写目录或点击“浏览…”选择文件夹。默认目录为 `%LOCALAPPDATA%\Programs\DesktopPet`，默认创建桌面快捷方式，并提供开始菜单入口。可在系统“已安装的应用”中卸载。
- 默认目录无需管理员权限；选择受保护的位置时需以管理员身份运行安装程序。个人数据目录会为安装时的 Windows 用户授予写入权限。
- 便携版：解压 `DesktopPet-1.1.0-portable.zip`，运行 `DesktopPet.exe`。配置和日历同样保存在程序所在目录。
- 源码版：双击 `启动桌宠.bat`，首次运行自动构建。源码有更新时会重新编译。
- 安装包和 ZIP 是构建输出，不提交 Git；可从 GitHub Actions 的 `DesktopPet-Windows` 构建产物下载。

右键人物可以调整大小、发型、按键显示方式、鼠标增幅、保持置顶、开机自启动和音乐律动，也可以打开日历、AI 设置或退出。单击人物展开文字输入，Enter 发送；中文输入法选词期间不会发送。拖动人物移动位置。

## 个人配置与 API 隔离

所有正常运行产生的数据统一保存在 `<安装目录>\user-data`，不再向 AppData 写入应用数据。以下路径均相对于 `user-data`：

| 路径 | 内容 |
| --- | --- |
| `ai_chat_config.json` / `.bak` | API 地址、模型、角色提示及加密密钥 |
| `keyboard_pet_config.json` | 位置、大小、发型、动作等偏好 |
| `input_bindings.tsv` | 个人按键绑定和鼠标增幅 |
| `data/calendar/` | 日历计划、记录、备份和恢复快照 |
| `.legacy-migrated` | 一次性旧数据迁移标记 |

在右键“AI 接口设置…”中填写 OpenAI 兼容地址、API Key 和模型。密钥使用 Windows DPAPI 按当前 Windows 用户加密保存，不能直接跨账户使用；换账户后重新填写。示例见 `config/ai_chat_config.example.json`，不要将真实配置复制进仓库。服务商公开地址和模型预设保留在源码中，个人选择单独保存。

安装时可勾选“导入旧版个人配置和日历”，将 v1.0 的 `%LOCALAPPDATA%\DesktopPet` 数据复制到安装目录，保留原文件，不覆盖目标中已有配置。更早版本放在程序旁的配置和 `data/calendar/` 会一次性复制到 `user-data`。当前版本运行时仅使用安装目录内的数据，不读取旧 AppData 配置。

覆盖升级保留 `user-data`；在卸载窗口中选择“是”保留个人配置和日历，选择“否”删除，选择“取消”退出卸载。保留的数据仍在原安装目录，重新安装到同一位置即可继续使用。卸载只按清单删除桌宠程序文件及其快捷方式，目录中的其他文件会保留。静默卸载默认保留数据，可通过 `-RemoveUserData` 明确删除。

备份时复制整个 `user-data` 文件夹。API Key 仍由 Windows DPAPI 按用户加密；跨 Windows 账户需要重新设置密钥。

`.gitignore` 排除了整个 `user-data`、旧位置的个人配置、日历数据、备份、EXE、安装包、预览及构建缓存。聊天上下文只在内存保留，不写入仓库。键鼠监听不保存输入日志，AI 请求只发送主动提交的文字与聊天上下文；当前聊天不调用麦克风或语音服务。

## 构建与检查

在项目根目录执行 Windows PowerShell：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Build-Installer.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-Package.ps1
```

构建调用 Windows 自带的 `.NET Framework\v4.0.30319\csc.exe`；安装包使用 Windows 自带 IExpress，无第三方打包工具依赖。`Build-Installer.ps1 -Version 1.1.1` 可指定版本，输出位于 `dist/`，包括安装 EXE、便携 ZIP 和 SHA-256 校验清单。打包从运行时图层清单逐项复制素材，不包含个人设置、日历、原始设计图、预览或测试代码。程序嵌入与界面一致的盾形图标，桌面和开始菜单快捷方式使用该图标。

`Test-Package.ps1` 使用含中文和空格的临时安装目录，检查目录选择界面、旧数据导入、目录内持久化、升级保留配置、桌面/开始菜单快捷方式，以及保留/删除数据两条卸载路径，并检查清单路径越界和无关文件保护；不注册真实安装或更改真实个人配置。完整回归测试另覆盖动画、透明边界、窗口交互、日历、API 协议与密钥加密。CI 使用 `-SkipUi` 跳过需要交互桌面的界面测试，执行构建、自检与静默安装包生命周期检查；界面和原生桌面交互回归在本地 Windows 会话运行。

安装脚本提供自定义目录界面、桌面和开始菜单快捷方式、当前用户卸载入口及升级时的退出保护。安装包尚未使用代码签名证书签名。

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
