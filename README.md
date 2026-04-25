# BetterNL5

`BetterNL5` 是一个面向神舟 NL5 / Tongfang 同系机型的控制中心替代方案。

它继续复用原版 `ControlCenter.exe`、驱动、WMI/ACPI 接口和电源计划，只绕开原版在新系统上会触发 `PerformanceCounter` 崩溃的路径。

## 当前状态

- 已完成三层结构：`BetterNL5.Core`、`BetterNL5.Cli`、`BetterNL5` WinUI 3 GUI
- 已升级到 `.NET 10`
- GUI 为中文界面，使用 WinUI 3 默认控件和原生 `NavigationView`
- GUI 支持性能模式切换、自动刷新、风扇曲线读取/保存、灯效控制
- CLI 支持状态读取、电源模式、风扇控制和灯效命令

## 项目结构

- `BetterNL5.Core`
  - 共享核心桥接层
  - 负责反射加载原版 `ControlCenter.exe`
  - 封装电源、风扇、灯效、WMI/ACPI 调用
- `BetterNL5.Cli/`
  - 命令行入口
  - 薄封装 `BetterNL5.Core`
- `BetterNL5/`
  - WinUI 3 图形界面
  - 依赖共享核心，不通过 CLI 转发
- `Probes/ControlCenterLoadProbe`
  - 用于验证原版程序集加载和关键入口可调用性

## 依赖条件

- 已安装原版控制中心，默认目录：`C:\Program Files (x86)\NL5\ControlCenter`
- 关键文件需存在：
  - `ControlCenter.exe`
  - `ControlCenterC64.dll`
  - `ControlCenter64.sys`
  - `Gaming.pow`
  - `HighPerformance.pow`
  - `office.pow`
- Windows App Runtime 需可用，才能运行 WinUI 3 GUI

## 构建

建议串行构建，避免共享输出目录引发锁文件问题。

```powershell
dotnet build .\BetterNL5.sln -c Release -p:Platform=x64 -m:1

dotnet build .\BetterNL5.Core\BetterNL5.Core.csproj -c Release -p:Platform=x64
dotnet build .\BetterNL5.Cli\BetterNL5.Cli.csproj -c Release -p:Platform=x64
dotnet build .\BetterNL5\BetterNL5.csproj -c Release -p:Platform=x64
```

如果 `BetterNL5.exe` 正在运行，默认 Release 输出可能会被锁住。先关闭运行中的 GUI，再重新构建。

如果 WinUI 单项目构建偶发 `XamlCompiler.exe exit 1`，优先使用顺序解决方案构建：`-m:1`。

## 运行

### GUI

Debug 示例：

```powershell
.\BetterNL5\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\BetterNL5.exe
```

GUI 目前采用自动刷新，不再提供单独的手动刷新按钮。

### GUI 发布与安装

当前 GUI 是 `WindowsPackageType=None` 的 WinUI 3 桌面程序，最简单的分发方式是发布整个目录后直接运行 `BetterNL5.exe`。

建议使用独立中间目录发布，避免 WinUI XAML 中间产物锁文件问题：

```powershell
dotnet publish .\BetterNL5\BetterNL5.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -p:BaseIntermediateOutputPath=.\.artifacts\publish-obj\ -o .\.artifacts\gui-publish\
```

发布完成后：

- 把 `.artifacts\gui-publish\` 整个目录打包成 zip
- 在目标机器解压到任意目录
- 运行 `BetterNL5.exe`

如果你想做真正的“安装包”，下一步应加一个 MSIX 或 Inno Setup 打包流程；当前仓库默认还是“解压即用”发布方式。

## MSIX 自动更新

仓库已开始切到单项目 MSIX 路径，目标是通过 GitHub Actions 构建 `.msix` 和 `.appinstaller`，并通过固定 URL 提供更新。

设计目标：

- GitHub Actions 在 `main` 和 tag 上生成签名后的 `MSIX`
- `.appinstaller` 指向稳定地址
- 用户首次安装 `.appinstaller`
- 之后应用启动时自动检查更新

计划中的更新地址：

- `https://moeclubm.github.io/BetterNL5/install/BetterNL5.appinstaller`

GitHub Actions 行为：

- 生成签名后的 `MSIX`
- 生成对应的 `.appinstaller`
- 把安装/update 文件发布到 GitHub Pages
- 在 tag 发布时同时把 `MSIX` 和 `.appinstaller` 上传到 GitHub Releases

首次安装方式：

1. 打开 `https://moeclubm.github.io/BetterNL5/install/BetterNL5.appinstaller`
2. 下载并双击 `.appinstaller`
3. 允许 App Installer 安装应用
4. 后续应用启动时会自动检查更新

这套模式要稳定工作，必须满足两个条件：

1. 包身份固定不变
2. 签名证书固定不变

因此你后续需要把同一个发布证书长期保存在 GitHub Secrets 中。

建议的仓库 Secrets：

- `MSIX_CERT_BASE64`
- `MSIX_CERT_PASSWORD`

证书用途：

- GitHub Actions 解码 `.pfx`
- 每次构建使用同一发布证书签名
- Windows 才会把新包识别为同一应用的更新

注意：

- GitHub Pages 必须在仓库设置中启用，并使用 **GitHub Actions** 作为来源
- 包身份 `Name` 和 `Publisher` 以后不能随意改，否则更新链会断
- 发布证书也必须长期保持一致，否则新包不会被识别为旧包更新

### CLI

查看状态：

```powershell
dotnet .\BetterNL5.Cli\bin\x64\Release\net10.0-windows10.0.19041.0\BetterNL5.Cli.dll status
```

切换性能模式：

```powershell
dotnet .\BetterNL5.Cli\bin\x64\Release\net10.0-windows10.0.19041.0\BetterNL5.Cli.dll power set audio
dotnet .\BetterNL5.Cli\bin\x64\Release\net10.0-windows10.0.19041.0\BetterNL5.Cli.dll power set gaming
dotnet .\BetterNL5.Cli\bin\x64\Release\net10.0-windows10.0.19041.0\BetterNL5.Cli.dll power set high
```

风扇相关：

```powershell
dotnet .\BetterNL5.Cli\bin\x64\Release\net10.0-windows10.0.19041.0\BetterNL5.Cli.dll fan status
dotnet .\BetterNL5.Cli\bin\x64\Release\net10.0-windows10.0.19041.0\BetterNL5.Cli.dll fan enable 1
dotnet .\BetterNL5.Cli\bin\x64\Release\net10.0-windows10.0.19041.0\BetterNL5.Cli.dll fan apply 1
```

灯效相关：

```powershell
dotnet .\BetterNL5.Cli\bin\x64\Release\net10.0-windows10.0.19041.0\BetterNL5.Cli.dll led module game
dotnet .\BetterNL5.Cli\bin\x64\Release\net10.0-windows10.0.19041.0\BetterNL5.Cli.dll led brightness game 2
```

## 已知限制

- 不实现原版 `CPUOC` / `PerformanceCounter` 路径，避免原版在新系统上崩溃
- 三风扇机型上目前仍存在 `sysTemp` 有值但 `sysRpm=0` 的情况，仍在继续排查
- 本项目依赖原版安装目录中的 OEM 运行时文件，不是独立重写驱动方案
- 复杂 XAML 布局在 WinUI 3 / Windows App SDK 1.7 下可能触发静默编译问题，因此界面保持较保守的原生布局

## 调试

- GUI 启动日志：`C:\Users\QwQ\AppData\Local\BetterNL5\startup.log`
- WinUI 构建诊断日志：`msbuild-winui.log`

如果 GUI 能启动但没有窗口，先看 `startup.log`。
