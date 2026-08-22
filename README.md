# AgentStatusBar

Windows 11 任务栏 Agent 运行状态指示器。第一版支持 **ZCode**：实时显示当前会话是在思考、执行工具、等待输入还是出错。

## 特性

- **任务栏状态条**：显示在任务栏最左侧（自动避让播放器歌词等贴任务栏控件）
  - 样式参考 Win11 前台应用图标：极淡底色，与任务栏同高
  - 文本格式：**今日 token 用量（缓/入/出，最前）· 旋转圆环 · N 会话 ·（多会话时）俏皮话 · 工具 耗时**
  - **今日 token 用量**（入/出/缓存，聚合自 ZCode SQLite 的 model_usage 表，经 node 只读查询、45 秒刷新）
  - **俏皮话**（移植自 [claude-code-zh-cn](https://github.com/taekchef/claude-code-zh-cn) 的 187 条 spinner 动词）每 4 秒轮换，切换时 260ms 交叉淡入滑动（秒数跳动等微调不做动效）
  - 运行中文字前有旋转圆环加载指示（25fps）
  - 空闲/无进行中会话时整个状态条自动隐藏，有活动自动回来
  - 3 倍超采样渲染再高质量缩小（配合 CreateDIBSection 保 alpha），小字号文字清晰无锯齿；字体 Segoe UI Variable Text
  - Win11 已移除 DeskBand 扩展点，这里用分层窗口逐像素 alpha 覆盖实现
  - 自动探测空隙位置并跟随变化；全屏应用时自动隐藏；explorer 重启后自动重新停靠
  - 左键弹出详情面板，右键弹出菜单
- **托盘图标**：彩色状态点 + 思考中旋转动画
  - 🔵 思考中（模型生成）· 🟠 工具执行（含当前工具名与耗时）· 🟢 等待输入（模型以 ？ 收尾）· ✔️ 灰绿 已完成 · 🔴 出错 · ⚪ 空闲
- **悬停提示**：总体状态 / 会话数 / 当前工具 / 模型
- **详情面板**：深色玻璃质感圆角浮层（卡片 + 高光描边），**在状态条正上方展开**（托盘图标触发时在右下角），按会话列出**会话标题**（来自 ZCode SQLite 库，经 node 只读查询、45 秒缓存；无 node 时回退项目目录名/短 ID）、相位、模型、轮次/请求/工具/错误统计；原地刷新不重建控件、位置零漂移；Esc 或点击外部关闭
- **开机自启**：右键菜单开关（写入 HKCU Run）
- 单实例互斥；未捕获异常写入 error.log

## 数据源

`~/.zcode/cli/log/zcode-YYYY-MM-DD.jsonl`（ZCode 每日日志，实时追加）。

解析的事件流：`turn.started/completed/failed`、`model.request.completed/failed`、`tool.call.started/completed/failed`、`session.model.updated`、`background_task.tracking.*` 等，按 `sessionId` 聚合成每个会话的状态机，再汇总为总体状态。

## 性能设计

- `FileSystemWatcher` 事件驱动 + 250ms 防抖，只读取日志**新增字节**（增量 tail，记录文件偏移，处理半行缓存与跨天滚动）
- 启动只回看末尾 256KB 做种子，不全量解析
- 图标仅在状态变化（或运行动画帧）时重绘；面板/迷你条不可见时不刷新
- 常驻内存 ~35–60MB（.NET Framework 自带运行时，零依赖单文件 exe，约 50KB）

## 构建与运行

无需安装任何 SDK，使用 Windows 自带的 .NET Framework 编译器：

```cmd
build.cmd
```

```cmd
AgentStatusBar.exe            :: 常驻托盘
AgentStatusBar.exe --status   :: 控制台输出一次解析结果（调试用，也写入 status-dump.txt）
```

配置保存在 `%APPDATA%\AgentStatusBar\config.ini`（迷你条开关与位置）。

## 已知限制（v1）

- 「等待输入」的判定是启发式：轮次结束时读取 `rollout/model-io-<会话>.jsonl` 末尾的最终回复，以 ？/? 收尾才算等待输入，否则为「已完成」——模型不带问号的征询句（如"需要的话说一声"）会被归为已完成
- 统计口径为"启动回看窗口 + 本次运行增量"，重启后今日计数从种子窗口重新累积
- `turn.started` 若发生在种子窗口之前，轮次数会偏小
- 会话显示最近 12 小时内活跃、且**未完成**的（最多 8 个）；已完成会话被过滤，但仍在后台跟踪，再次开始新轮次时自动重新出现；所有会话都完成时总体显示空闲
- 事件流停滞 >90s 自动降级为空闲（防止 ZCode 异常退出后状态卡在"运行中"）

## 路线图

- v2：读取 `db.sqlite` 获取 token 用量 / 成本；监控 `exec/sess_*` 判断进程存活
- 支持更多 Agent（Claude Code、Codex 等，同样基于日志文件探测）
- 设置界面（活跃阈值、展示项、状态条位置微调）
- 如需极致轻量（<10MB 内存）可迁移 Rust + 纯 Win32

## 已知限制（任务栏状态条）

- 仅支持横向任务栏（Win11 默认）；任务栏图标左对齐时无空隙，状态条自动隐藏
- 分辨率/缩放变化后最多 0.5 秒内跟上（500ms 跟随周期）
- 竖向任务栏（注册表修改的非默认布局）不支持

## 代码结构

```
src/
  Program.cs        入口：单实例、DPI、异常日志、--status 调试模式
  StatusEngine.cs   核心：日志 tail + 事件解析 + 会话状态机（无 UI 依赖，可独立复用）
  TaskbarStrip.cs   任务栏状态条：Shell_TrayWnd 空隙探测 + 分层窗口渲染（UpdateLayeredWindow）
  TrayApp.cs        托盘图标绘制、提示、右键菜单、自启动、装配
  PopupForm.cs      详情面板
  StateDot.cs       状态圆点控件
  Ui.cs             颜色/字体/时间格式化
  Native.cs         DWM、DPI、任务栏/分层窗口等 P/Invoke
  Settings.cs       极简 INI 配置
shot.ps1            开发用：截取任务栏区域
tbdiag.ps1          开发用：枚举任务栏子窗口与本程序窗口矩形
```
