# 第三方声明

## 随应用安装的组件

| 组件 | 来源与声明 |
| --- | --- |
| Microsoft .NET 与 Windows Desktop Runtime | 自包含 Windows 构建使用 `Microsoft.NETCore.App.Runtime.win-x64` 和 `Microsoft.WindowsDesktop.App.Runtime.win-x64`。发布时从实际解析的 NuGet 运行时包复制许可证及所附第三方声明。已验收版本为 10.0.15，两个运行时包内的主许可证均为 MIT。 [核心运行时包](https://www.nuget.org/packages/Microsoft.NETCore.App.Runtime.win-x64/10.0.15) · [桌面运行时包](https://www.nuget.org/packages/Microsoft.WindowsDesktop.App.Runtime.win-x64/10.0.15) |
| WPF 与 System.Drawing | 自包含运行时所用的上游声明随构建保存。仓库中的声明取自已验收运行时对应的 v10.0.15 标签。 [WPF 第三方声明](https://github.com/dotnet/wpf/blob/v10.0.15/THIRD-PARTY-NOTICES.TXT) · [WinForms 第三方声明](https://github.com/dotnet/winforms/blob/v10.0.15/THIRD-PARTY-NOTICES.TXT) |
| NSIS 与 Modern UI 2 | 安装版使用 NSIS 3.13 生成安装和卸载程序，采用原版 LZMA 压缩模块。其 CPL 1.0 特别链接例外及 NSIS 其余声明见官方发行包所附 `COPYING`。 [NSIS 许可证](https://nsis.sourceforge.io/License) · [NSIS 3.13 发行文件和源代码](https://sourceforge.net/projects/nsis/files/NSIS%203/3.13/) |

安装后的声明位于应用运行目录下的 `runtime/licenses/`。

## 离线聊天的可选依赖

- 已实测的 Qwen3-4B-Instruct-2507 及 ggml-org Q8_0 GGUF 转换均标为 Apache-2.0。用户可以从上游取得模型。 [Qwen 原始模型](https://huggingface.co/Qwen/Qwen3-4B-Instruct-2507) · [固定 GGUF 转换版本](https://huggingface.co/ggml-org/Qwen3-4B-Instruct-2507-Q8_0-GGUF/tree/e6f794d44f9395d0184a966c27b5ae99ea356fcb)
- 已实测的 llama.cpp b12386 上游许可证为 MIT。Windows 预编译包附带 LLVM OpenMP 等组件声明。离线对话使用用户本机配置的 CPU 或 Vulkan 后端。 [llama.cpp 许可证](https://github.com/ggml-org/llama.cpp/blob/b12386/LICENSE) · [固定版本发行包](https://github.com/ggml-org/llama.cpp/releases/tag/b12386)

## 角色与项目素材

温迪与《原神》相关角色设定来自米哈游／HoYoverse。当前插图是项目制作的二创样片，欢迎封面使用同一角色图。项目源码和美术素材的发布范围由项目所有者确定。 [《原神》官方条款](https://genshin.hoyoverse.com/en-us/company/terms)
