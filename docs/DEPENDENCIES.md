# 本地依赖恢复

## 不随源码分发的内容

`engine/` 与 `lib/TEdit/` 不纳入 Git。输入必须由用户合法取得，并与 `dependency-manifest.json` 的每项相对路径、长度、SHA256 完全一致。

引擎锁定输入来自本地 UnifierTSL 的 Release / net9.0 构建输出；TEdit 输入来自本地切图工具依赖构建，其中含已有的 `PreserveBorderBrickStyles` 修改。这里锁定的是**实际输入字节**，不是声称任何同版本上游构建都可重现它，也不冒称已具有这些依赖的完整源码到二进制证明。不能重现时应报告依赖缺失或不匹配，而不是修改清单以接受任意 DLL。

关键输入：

| 路径 | bytes | SHA256 |
| --- | ---: | --- |
| engine/UnifierTSL.dll | 1504768 | CE949F2100110A3F134519CCB445B23F832A2509F354E7B7B71794F6668A999D |
| engine/OTAPI.dll | 30047744 | 60C43A2AACC46844A32EF63672DFFA8BF827A754E69A0C87BA1188793B00777F |
| engine/OTAPI.Runtime.dll | 30151168 | 00AC2C82930A8F4DEAC64158F90460C08F6D03C963B300EBDF1155ABD7D4B588 |
| lib/TEdit/TEdit.Terraria.dll | 8019968 | CD469BED498A6260E8617846653CBF298FA38B03DD2F788BE7E30BCBB039AA4D |

## 导入与校验

```powershell
./scripts/Import-Dependencies.ps1 -EngineSource <引擎DLL所在目录> -TEditSource <TEdit依赖DLL所在目录>
./scripts/Test-Dependencies.ps1
./构建.ps1
```

导入器先验证全部输入和已存在的目标，再复制缺少的目标。遇到不匹配即拒绝；不会静默覆盖、不删除文件、不联网、不修改桌面或生产。

这份锁只包含本工具使用的 51 个托管依赖文件，不是可直接启动生产 UTSL 的完整服务端包。NuGet GUI 依赖另由 `src/Studio/packages.lock.json` 和 NuGet 自身的内容摘要约束。

## 构建与运行区别

- GUI 可单独构建，完整生成流水线则必须恢复引擎和 TEdit 输入。
- Worker 目标为 net9.0；运行需 .NET 9 运行时。GUI / 流水线目标为 net10.0-windows，需 Windows Desktop 运行时。
- 引擎被离线 worker 加载，用于生成 / 读图；生成器不因此启动游戏监听端口或装载生产插件。
- CI 不持有上述引擎，因此只跑 GUI 构建和源码边界检查，不能以 CI 绿色冒充世界生成或真人 E2E PASS。
