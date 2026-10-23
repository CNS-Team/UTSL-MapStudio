# 同窗口嵌入

StudioWorkspace 为 Avalonia 宿主提供 View、IsBusy、Cancel 和 Dispose。构造时显式提供宿主 Window、生成流水线运行目录和地图输出目录，再把 View 放入宿主 ContentControl；不另外 Show 地图窗口。对话框和文件选择器使用传入的宿主作为 owner。

生成过程中先 Cancel 并等待 IsBusy 为 false，再 Dispose。宿主切换页面应保留同一 workspace，不在生成期间销毁视图。运行目录结构仍遵循 app/、worker/、engine/、lib/TEdit/。独立 EXE 不设置嵌入目录，继续使用自身目录。

当前 API 按单个 UI 线程、每进程一个 workspace 设计，不声明多个不同运行根可同时嵌入。
