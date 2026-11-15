# VENTI ALIVE v0.9.7 · Windows x64 源码预览

VENTI ALIVE 是一个使用 WPF 的 Windows 桌宠项目，包含透明桌面窗口、可拖拽角色、待机动画、自主飞行与离线聊天。对应的源码提交写在 `SOURCE_COMMIT.txt` 中。

## 运行几何演示

在 Windows x64 安装 .NET 10 SDK，从源码根目录运行。你会看到主控窗口和一个可拖动的透明圆环。按住圆环拖动，用主控的「退出温迪」结束。这个演示可以直接体验透明窗口和鼠标拖拽。

```powershell
dotnet run --project ./experiments/m1-wpf-window/Aeris.WindowProbe.csproj -- --play
```

## 使用独立角色素材构建

角色动画和聊天入口依赖独立的角色资源。制作安装版需要 PowerShell 7、NSIS 3.13 和你有权使用的角色 PNG。资源目录含 `front.png`、`body-v0.2.png`、`blink.png`、`gaze.png`、`plume.png`、`braid-left.png`、`braid-right.png`、`cape-left.png`、`cape-right.png`，以及 `gestures/` 下的 `lyre.png`、`lyre-ready.png`、`lyre-pluck.png`、`hips.png`、`crossed.png`、`crossed-enter.png`。角色图层使用对齐的 1024×1536 画布。 [NSIS 3.13 下载](https://sourceforge.net/projects/nsis/files/NSIS%203/3.13/)

```powershell
pwsh -NoProfile -STA -File ./installer/Build-VentiSetup.ps1 `
    -Compiler 'C:/Tools/nsis-3.13/makensis.exe' `
    -AssetDirectory 'C:/DesktopPetAssets' `
    -Version 0.9.7 `
    -Destination ./artifacts/local/VENTI-ALIVE-Setup.exe
```

打开「离线聊天」后，可选择包含 `qwen3-4b-instruct-2507-q8_0.gguf` 的模型目录。Vulkan 后端使用 `vulkan-b12386/llama-server.exe`，CPU 后端使用 `llama.cpp/llama-server.exe`。源包包含本地聊天客户端，上游模型和后端见第三方声明。配置和长期记忆保存在 `%LOCALAPPDATA%\AerisAlive`。离线聊天默认读取已保存的记忆，可在聊天窗口取消勾选。 [组件来源和许可](THIRD_PARTY_NOTICES.md) · [参与开发](CONTRIBUTING.md)

## 已验证范围

Windows 11 x64 上验证过 100% 与 125% 缩放的双屏交互。开机自启动验收使用模拟登录命令。快速往返拖拽仍可能出现可感知的跟手延迟。

这是独立的同人开发项目。温迪立绘、动画图层及其衍生封面另行管理。

## 许可证

项目自有代码、构建脚本、几何演示、风纹图标和项目文档采用 MIT 许可证，署名 Fanless。第三方组件遵循各自许可证。 [MIT 许可证](LICENSE) · [第三方声明](THIRD_PARTY_NOTICES.md)
