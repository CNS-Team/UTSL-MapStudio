# 第三方组件与来源边界

本仓库只分发生成器源码和资源，不分发下述引擎或本地依赖 DLL，不将它们冒称为独立原创引擎。

- **Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent 12.1.2**，以及其 NuGet 依赖 SkiaSharp / HarfBuzzSharp 等：GUI 依赖，精确版本见 `src/Studio/packages.lock.json`。保留各组件原有许可证和版权。
- **UnifierTSL / OTAPI / OTAPI.Runtime**：原生生成工作进程使用的本地依赖；锁定输入来自用户已有的 UnifierTSL 构建。UnifierTSL 上游采用 GPL-3.0。本工具没有改写锁定的 `UnifierTSL.dll`；生成钩子仅存在于离线 worker 进程。
- **TEdit.Terraria / TEdit.Common**：锁定输入来自本地 TEdit 构建，包含此前已有的 `PreserveBorderBrickStyles` 修改。不是保证兼容任意官方版本；完整字节锁见 `dependency-manifest.json`。
- **ReactiveUI / Splat / System.Reactive / Newtonsoft.Json / ColorMinePortable / PropertyModels** 及其他锁定输入：TEdit / UTSL 的依赖，原有署名和许可证不因本工具署名而转移。
- `assets/seeds/engine-1.4.5.8.json` 是从锁定原生世界实例采集的功能清单及本地化描述，不代表对游戏资源重新授权。
- 应用图标由用户提供的原图缩制；原图及衍生资源摘要见 `assets/branding/source-manifest.json`。保留潜在第三方形象 / 商标权利，不作额外授权承诺。

源码中的上游 GPL-3.0 派生部分与生成器新增源码按 GPL-3.0 分发，见 `LICENSE`。构建或另行分发带依赖的程序时，分发者仍须逐组件核对许可、对应源码、通知与再分发条件；本通知不是对全部依赖统一重新授权。

本仓库不含 Terraria 客户端素材、游戏安装包、生产账号数据或服务端部署包。不要将本工具构建输出复制到生产 `plugins` 作为服务端插件。
