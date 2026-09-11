# Socket 多人联机框架

> 单一物理场景、多房间隔离的 Unity + Mirror 联机框架。房间、对战与配置全部可扩展：**新增玩法 = 一个适配器 + 一份规则资产**。
>
> *A Mirror-based multiplayer framework for Unity — one physical scene, many isolated rooms. Adding a new game mode takes one adapter + one rules asset.*

- Unity **2022.3.16f1** · Mirror **v96.10**（仓库内置源码快照，MIT）· KCP 传输 · UI Toolkit 编辑器工具链
- 演示玩法：**五子棋**（经典交叉点棋盘、对战/观战区分、战绩与本地回放）
- 零付费插件依赖（已移除 Odin 等第三方付费编辑器扩展）

## 特性

**框架层**
- 单一物理在线场景：所有房间共存于同一 scene，通过 `NetworkMatch` + `MatchInterestManagement` 按 `matchId` 隔离可见性
- 策略集中配置：人数 / 观战上限 / 回合时限 / 战绩条数 / 回放开关 / 闲置回收 / 重连窗口 / 各入口限频
- 玩法与框架解耦：`NetworkMatchAdapter`（抽象适配器）+ `MatchRulesConfig`（规则资产抽象基类）

**联机能力**
- 房间：创建 / 加入 / 离开 / 取消 / Leader 转移 / 准备门控 / 中途加入
- 观战：超出座位上限自动转为观战者，成员列表带「对战 / 观战」标注
- 掉线处理：断线判负但保留房间；重连窗口内同名可回到原座位；轮到掉线方时直接判负
- 对局：回合超时判负、主动认输、和棋判定、非法落子结构化拒绝原因
- 房间回收：闲置超时回收 + 空房立即删除 + 成员状态自愈对账
- 局域网发现：UDP 广播 + TTL 去重，扫描列表一键加入

**编辑器工具链（UI Toolkit，无第三方依赖）**
- 联机配置中心：总览 / 全局配置 / 房间模板 / 玩法规则 / 配置检查 / 外观，共 6 页
- 三个检查器：配置、房间模板、玩法规则（带可点击棋盘预览）；在配置中心内自动进入「嵌入模式」
- 主题系统：5 套强调色 + 3 套背景基调（跟随编辑器 / 深蓝灰 / 炭黑），本机保存、即点即换
- 房间模板可直接编辑出生点与交互物布局；配置检查给出可点击跳转的问题清单

**可测试性**
- 纯逻辑内核不依赖 Unity：`MatchSession` / `GomokuRules` 等由 dotnet NUnit 直接编译测试（13 用例）
- Unity EditMode 41 用例；`ci/run-tests.ps1` 一键两层回归；pre-commit 钩子自动跑纯逻辑层

## 快速开始

1. 用 Unity **2022.3.16f1** 打开工程
2. 菜单 `Socket > Multiplayer > Initialize PC Demo` 生成演示资源（配置资产 / 预制体 / 场景）
3. 打开 `Assets/MultiplayerGenerated/Scenes/Bootstrap.unity`，点 Play
4. 点 `Host` 开服 → `Create Room` 建房；第二个客户端点 `Join` 加入同一房间
5. 房内可 `Ready` / `Start`，之后落子、观战、认输、查看战绩与回放都在同一面板内

> 配置入口：`Socket > 多人联机 > 打开配置中心`（改完记得在「快捷操作」里保存）

## 目录结构

```text
Assets/
├─ Multiplayer/                 框架本体（namespace Socket.Multiplayer）
│  ├─ Runtime/
│  │  ├─ Config/                配置资产：MultiplayerConfig、RoomTemplate
│  │  ├─ Match/                 纯逻辑内核：MatchSession、MatchPrimitives、MatchRecords、MatchRulesConfig
│  │  ├─ Games/Gomoku/          五子棋玩法：规则、状态、规则资产
│  │  ├─ Network/               Mirror 适配与房间路由：SocketRoomManager、RoomRegistry、NetworkMatchAdapter…
│  │  └─ Presentation/          运行时 UI（uGUI 构建）与输入
│  ├─ Editor/                   UI Toolkit 配置中心 / 检查器 / 主题（含 Theme/ 配色片段）
│  ├─ Tests/                    EditMode 用例
│  └─ Docs/                     设计文档 01~05
├─ MultiplayerGenerated/        演示资源（可用初始化菜单重新生成）
│  ├─ MultiplayerConfig.asset   全局配置
│  ├─ DefaultRoomTemplate.asset 房间模板（出生点 / 交互物 / 玩法规则）
│  ├─ GomokuRuleConfig.asset    五子棋规则资产
│  ├─ Prefabs/  Scenes/         预制体与 Bootstrap / Lobby 场景
└─ Mirror/                      Mirror v96.10 源码快照（MIT，含原始许可文件）
ci/
├─ run-tests.ps1                两层测试入口（-PureOnly 只跑纯逻辑）
└─ PureLogic.Tests/             dotnet NUnit 工程（net9.0）
```

## 架构分层

| 层 | 位置 | 职责 |
|---|---|---|
| 配置层 | `Runtime/Config` + `MatchRulesConfig` | 房间容量、观战、时限、战绩/回放、限频等策略；参与协议签名 |
| 内核层 | `Runtime/Match`、`Runtime/Games` | 不依赖 Unity 的规则内核：会话、事件、裁决、战绩；可被 dotnet 直接测试 |
| 适配层 | `Runtime/Network/NetworkMatchAdapter`（抽象）+ `NetworkGomokuMatch` | 把内核接到 Mirror：同步快照、命令入口、服务器裁决 |
| 房间层 | `SocketRoomManager`（`NetworkManager`）、`RoomRegistry` | 房间生命周期、座位分配、观战、重连、回收、消息路由 |
| 表现层 | `Runtime/Presentation`、`MultiplayerUi.*` | 运行时构建的房间面板、棋盘、聊天、战绩与回放 UI |

**新增一个玩法只需三步**

1. 派生规则资产：`class MyRulesConfig : MatchRulesConfig`（实现 `RulesetName / RulesSummary / AllowsSpectators / SignatureFingerprint`）
2. 派生适配器：`class NetworkMyMatch : NetworkMatchAdapter`（实现落子/出牌等命令入口与会话构造）
3. 建预制体并挂上适配器，在房间模板里绑定规则资产即可；协议签名会自动带上新玩法的指纹

## 配置中心

`Socket > 多人联机 > 打开配置中心`，六个页签：

| 页 | 内容 |
|---|---|
| 总览 | 状态横幅、容量/网络/模板三张卡片、运行环境信息行（协议签名、模板、玩法规则、场景、入口）、快捷操作 |
| 全局配置 | 房间 / 网络 / 场景 / 控制 / 交互 / 界面 六个分区，字段全部中文标签 |
| 房间模板 | 人数、出生点列表、交互物列表、绑定玩法规则资产 |
| 玩法规则 | 当前玩法规则的字段与可视化预览（如五子棋可点击棋盘） |
| 配置检查 | 圆点状态行列出问题（场景未进 Build Settings、人数冲突…） |
| 外观 | 强调色 5 套 + 背景基调 3 套，本机保存、即点即换 |

> 检查器同样使用 UI Toolkit：单独选中资产时显示页签，嵌入配置中心时自动切换为分区堆叠。

## 对局与特殊场景

- **观战**：座位满员后新玩家自动成为观战者，不参与落子；观战权限与人数上限由规则资产 / 模板控制
- **掉线判负**：对局中断线一方判负并记录原因（认输 / 掉线 / 离开 / 超时），房间保留，其余人可继续查看战绩
- **重连**：`reconnectWindow` 秒内用同名重连可回到原座位；窗口过期后座位释放
- **超时**：`turnTimeoutSeconds` 到点由服务器判当前行动方负（0 表示关闭计时）
- **认输**：对局中可随时投降，走同一套结束与战绩流程
- **回收**：房间闲置超过 `roomIdleTimeout` 秒自动回收，成员送回大厅；空房立即删除
- **战绩与回放**：每房保留 `matchRecordLimit` 条战绩；开启 `recordReplays` 时随战绩保存落子序列，可在本地逐步回放

## 测试与 CI

```powershell
# 纯逻辑层（秒级，不需要编辑器）
.\ci\run-tests.ps1 -PureOnly

# 纯逻辑 + Unity EditMode（批处理模式，需关闭已打开的编辑器）
.\ci\run-tests.ps1
```

- 纯逻辑 13 用例：会话裁决、判负原因、战绩簿、协议签名等
- Unity EditMode 41 用例：房间注册表、对战框架、战绩与回放
- `git commit` 时 pre-commit 钩子自动执行纯逻辑层

## 命令行参数

```text
-host            主机模式（服务器 + 客户端）
-server          专用服务器
-client <addr>   客户端并指定地址
-port <port>     覆盖 KCP 端口
-name <player>   覆盖默认玩家名
```

## 已知边界

公共匹配、NAT 穿透、语音、持久化、主机迁移不在本仓库范围内；当前单一物理在线场景是这些能力后续扩展的基础。

## 文档

| 文档 | 内容 |
|---|---|
| `Assets/Multiplayer/Docs/01_主流Mirror框架调研报告.md` | 选型与 Mirror 能力调研 |
| `Assets/Multiplayer/Docs/02_落地实施计划.md` | 实施路线与可视化计划 |
| `Assets/Multiplayer/Docs/03_任务落地书.md` | 任务分解与验收记录 |
| `Assets/Multiplayer/Docs/04_M2重新映射方案.md` | 房间模型重构方案 |
| `Assets/Multiplayer/Docs/05_对战框架与战绩回放.md` | 对战框架分层、战绩与回放设计 |
| `README_MULTIPLAYER.md` | English detail notes（英文说明） |

## 第三方与许可

- `Assets/Mirror`：Mirror Networking v96.10 源码快照，遵循其原始 MIT 许可与许可文件
- 其余第三方组件（KCP、Telepathy、SimpleWebTransport、Mono.Cecil、BouncyCastle 等）随 Mirror 分发，均保留各自 LICENSE
- 本仓库不包含任何付费编辑器扩展
