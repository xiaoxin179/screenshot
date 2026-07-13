# Screenshot

一款面向 Windows 的轻量截图工具，支持选区截图、标注、复制到剪切板和截图贴图。

## 功能

- 支持自定义全局截图快捷键。
- 支持拖拽选择区域截图，并提供确认和取消操作。
- 支持矩形与画笔标注。
- 截图完成后自动复制到剪切板。
- 支持将截图作为可拖动、可缩放的小卡片置顶在桌面上。
- 支持系统托盘常驻运行和开机自启动配置。

## 下载与运行

请在仓库的 **Releases** 页面下载 `ScreenshotTool.exe`，双击即可运行。

该程序是自包含的 Windows 可执行文件，无需另外安装 .NET 运行时或开发环境。

## 从源码构建

使用 Visual Studio 打开 `ScreenshotTool/ScreenshotTool.csproj`，选择 Windows x64 平台后进行生成。

也可以在项目根目录运行 `build.ps1`，生成自包含的可执行文件。

## 测试计划

手动测试清单见 [TEST_PLAN.md](TEST_PLAN.md)。
