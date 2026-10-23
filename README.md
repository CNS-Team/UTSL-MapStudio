# UTSL地图生成器 · MapStudio

开发团队：**流光工作室** · 版权署名：**流光-Pigeon-X**

面向 UTSL 三联接缝世界的 Windows 桌面地图生成器。Avalonia 暗色界面，实时显示真实图格生成进度；支持特殊种子、基础环境补全和生成后的三联世界长图。

本项目在 **CNS-Team** 组织下公开、独立维护，维护者为 **Pigeon-X**。这是生成器源码仓库，**不是服务器管理器，也不包含服务器插件发布包**。后续管理器集成以这里的版本为上游，不再复制多个无来源的维护根。

## 功能

- **连续母图 → 西 / 中 / 东**：生成一张连续母图，再切成三张接缝地图；默认每片 8400×2400、大师难度，中间世界为默认出生世界。
- **独立标准三世界 → 同海平面**：分别生成正常地图，以中间地图海水顶面 Y 为基准统一边界；保留内陆地形、建筑和战利品，不全图上下平移。
- 默认总重叠 80 格、单侧 40 格；边缘完整字段比对和保存后重读验证。
- 11 种地图样式、9 种原生特殊种子、37 项种子特性、11 种组合配方；实际支持项由锁定引擎采集，而不是把选项名称当实现。
- 空岛等模式可选择补全地牢、神庙、丛林、腐化、猩红、微光等基础环境；可关闭补全以保留纯原生效果。
- 成功后附 `三联世界长图.png`；只有存在 `COMPLETE.json` 的批次才可使用。取消、失败或有损保护拒绝的批次不冒充成功。
- 生成、原生读图、边界比对、预览及种子配方均有本地测试入口；**不等于真实玩家跨服、边界传送或客户端渲染 E2E 已验证**。

详细参数、保护条件及两种生成模式的差异见 [生成说明](docs/GENERATION.md)。

## 源码目录

```text
src/Studio/         Avalonia 正式界面
src/Generator/      隐藏生成流水线 / CLI / 历史 WinForms 兼容入口
src/NativeWorker/   隐藏原生世界生成工作进程
src/Shared/         实时预览协议、样式和种子目录
assets/branding/    应用图标与来源摘要
assets/seeds/       锁定引擎采集的种子 / 特性清单
scripts/           便携的构建辅助、测试与依赖校验脚本
dependency-manifest.json   本地原生引擎 / TEdit 依赖的 bytes / SHA256 锁
```

地图、日志、数据库、历史回执、生产部署脚本、EdgeStitch 集成目录、编译输出和第三方引擎 DLL 均不入库。EdgeStitch 是独立服务端插件，不由本仓库自动安装或启停。

## 构建

需要 Windows x64、PowerShell 7、.NET 10 SDK（GUI / 流水线）及 .NET 9 运行时（worker）。GUI 的 NuGet 版本由 `src/Studio/packages.lock.json` 锁定。

完整生成器还需要用户自行提供的本地 UnifierTSL / OTAPI 和 TEdit 精确依赖；**仅克隆仓库不等于已具备完整生成环境**。本仓库不上传或自动下载 Terraria 引擎二进制，也不接受用不匹配 DLL 凑引用。来源边界见 [依赖说明](docs/DEPENDENCIES.md)。

```powershell
git clone https://github.com/CNS-Team/UTSL-MapStudio.git
cd UTSL-MapStudio

# 按锁校验所有输入后，导入用户已有的合法依赖；不会覆盖不匹配的文件。
./scripts/Import-Dependencies.ps1 -EngineSource <本地引擎目录> -TEditSource <本地TEdit目录>
./scripts/Test-Dependencies.ps1
./构建.ps1
```

如果只开发 GUI，无需导入引擎即可构建：

```powershell
dotnet build src/Studio/Studio.csproj -c Release -o studio -p:RestoreLockedMode=true
```

`构建.ps1` 只写当前仓库的 `app / worker / studio` 构建输出，不替换桌面程序，不连接或部署生产。CI 只验证不依赖私有引擎的 GUI 构建及源码发布边界，不声称完整世界生成通过。

## 本地测试

每次指定新的结果目录，勿覆盖旧证据：

```powershell
./app/UTSL.ThreeWorlds.exe self-test evidence/self-test-01
./app/UTSL.ThreeWorlds.exe live-test evidence/live-test-01
./app/UTSL.ThreeWorlds.exe foundation-test evidence/foundation-test-01
./app/UTSL.ThreeWorlds.exe normal-stitch-test evidence/normal-stitch-test-01
./app/UTSL.ThreeWorlds.exe seed-recipe-test evidence/seed-recipe-test-01
./studio/UTSL.MapStudio.exe --ui-smoke evidence/ui-smoke-01
```

`generate <options.json>` 执行实际生成，`verify <批次目录>` 核验既有成功结果。实际生成耗时和内存随尺寸、种子与补全策略变化，不把组件断言数当游戏测试数。测试证据不要放入生成批次目录，以免触发非空输出保护。

## 程序布局

```text
UTSL地图生成器/
  UTSL地图生成器.exe
  地图/                 每批独立结果，含成功长图
  程序/
    studio/ app/ worker/ engine/ lib/
```

`scripts/创建根目录EXE.ps1 -PackageRoot <程序根>` 可在依赖已正确复制后生成根 GUI apphost。程序本体与地图分开，不弹出新的命令框；源码仍在本仓库独立维护。

## 许可

源码按 GPL-3.0 分发，见 [LICENSE](LICENSE)。第三方组件、原有修改和分发边界见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。流光工作室 / 流光-Pigeon-X 的署名不改变第三方作者权利；用户提供的图标原图不视为对任何第三方商标或形象的再授权。
