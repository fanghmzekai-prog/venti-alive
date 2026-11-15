# VENTI ALIVE v0.9.7 · Windows x64 源码预览

VENTI ALIVE 是一个使用 WPF 的 Windows 桌宠项目，包含透明桌面窗口、可拖拽角色、待机动画、自主飞行与离线聊天。对应的源码提交写在 `SOURCE_COMMIT.txt` 中。

## 注意事项

- **首次体验**　发布页提供 Windows x64 源码 ZIP。安装 .NET 10 SDK 后即可构建几何圆环，体验透明窗口、点击穿透和拖拽。测试环境和已知限制见[已验证范围](#已验证范围)。
- **角色素材**　这是独立同人项目。完整角色动作及聊天入口需要你有权使用的独立角色资源。温迪立绘、动作图层和衍生封面另行管理，项目 MIT 的适用范围见[许可证](#许可证)。
- **离线聊天和隐私**　聊天使用用户配置的本机后端。已保存的记忆以 JSON 存放在 `%LOCALAPPDATA%\AerisAlive\dialogue-memory.json`，每次打开离线聊天默认读取，可在聊天窗口取消勾选。提交问题时可以附 Windows 版本、屏幕缩放和复现步骤，记忆文件和私人对话请保存在本机。

## 运行几何演示

在 Windows x64 安装 .NET 10 SDK，从源码根目录运行。你会看到主控窗口和一个可拖动的透明圆环。按住圆环拖动，用主控的「退出温迪」结束。这个演示可以直接体验透明窗口和鼠标拖拽。

```powershell
dotnet run --project ./experiments/m1-wpf-window/Aeris.WindowProbe.csproj -- --play
```

## 使用独立角色素材构建

制作安装版需要 PowerShell 7、NSIS 3.13 和你有权使用的角色 PNG。资源目录含 `front.png`、`body-v0.2.png`、`blink.png`、`gaze.png`、`plume.png`、`braid-left.png`、`braid-right.png`、`cape-left.png`、`cape-right.png`，以及 `gestures/` 下的 `lyre.png`、`lyre-ready.png`、`lyre-pluck.png`、`hips.png`、`crossed.png`、`crossed-enter.png`。角色图层使用对齐的 1024×1536 画布。 [NSIS 3.13 下载](https://sourceforge.net/projects/nsis/files/NSIS%203/3.13/)

```powershell
pwsh -NoProfile -STA -File ./installer/Build-VentiSetup.ps1 `
    -Compiler 'C:/Tools/nsis-3.13/makensis.exe' `
    -AssetDirectory 'C:/DesktopPetAssets' `
    -Version 0.9.7 `
    -Destination ./artifacts/local/VENTI-ALIVE-Setup.exe
```

打开「离线聊天」后，可选择包含 `qwen3-4b-instruct-2507-q8_0.gguf` 的模型目录。Vulkan 后端使用 `vulkan-b12386/llama-server.exe`，CPU 后端使用 `llama.cpp/llama-server.exe`。可从第三方声明中的上游链接获取模型和后端。 [组件来源和许可](THIRD_PARTY_NOTICES.md) · [参与开发](CONTRIBUTING.md)

## 已验证范围

Windows 11 x64 上验证过 100% 与 125% 缩放的双屏交互。开机自启动验收使用模拟登录命令。快速往返拖拽仍可能出现可感知的跟手延迟。

## 许可证

项目自有代码、构建脚本、几何演示、风纹图标和项目文档采用 MIT 许可证，署名 Fanless。第三方组件遵循各自许可证。 [MIT 许可证](LICENSE) · [第三方声明](THIRD_PARTY_NOTICES.md)
