# Socket Multiplayer Framework — 主流 Mirror 框架调研报告

> 状态：调研完成 · 落盘日期 2026-08-31 · 对应仓库基线 `56fd9ab`
> 配套文档：`02_落地实施计划.md`（分阶段详细设计）、`03_任务落地书.md`（可勾选任务清单）

---

## 一、先说结论

本框架最大的问题是 **"重复造了 Mirror 本已提供的轮子，且造得不如原版"**，同时缺少主流框架普遍具备的**服务化能力**。调研发现：主流开源方案几乎都建立在 Mirror 内置的 `NetworkRoomManager / NetworkRoomPlayer / 兴趣管理 / 传输层` 之上，做 **"薄封装 + 垂直功能"**，而不是像现在这样从零手写房间状态机。

本报告为全景、差距与路线图。逐阶段的落地细节见 `02_落地实施计划.md`，可执行任务清单见 `03_任务落地书.md`。

---

## 二、主流基于 Mirror 的开源框架 / 样板

### 🏛️ 官方 / Mirror 核心库自带（本地 `Assets/Mirror/` 已捆绑，可对照源码）

| 能力 | 位置 | 关键点 |
|---|---|---|
| NetworkRoomManager | `Assets/Mirror/Components/NetworkRoomManager.cs` | 成熟房间系统：roomSlots、pendingPlayers、`OnRoom*` 全生命周期虚方法、`ReplacePlayerForConnection` 房间玩家↔游戏玩家替换、headless 空房自动 `StopServer()` |
| NetworkRoomPlayer | `Assets/Mirror/Components/NetworkRoomPlayer.cs` | `readyToBegin`/`index` SyncVar + Hook、客户端进/出房间回调、host 可踢人、`DontDestroyOnLoad` 跨场景存活 |
| 兴趣管理（房间隔离） | `Assets/Mirror/Components/InterestManagement/` | Scene / Match / Distance / SpatialHashing / Team 五套。`MatchInterestManagement` + `NetworkMatch` = 单服务器多房间互不可见的官方方案（本框架完全没有） |
| 匹配 / 房间列表示例 | `Assets/Mirror/Examples/MultipleMatches/` | `MatchMessages.cs` 定义 Create/Cancel/Start/Join/Leave/Ready 六种操作 + `MatchInfo/PlayerInfo` 消息——这就是多人房间 API 的事实标准 |
| 认证 | `Assets/Mirror/Authenticators/` | `UniqueNameAuthenticator`（已用）+ `BasicAuthenticator`（账号密码）+ `DeviceAuthenticator`（设备号）+ `TimeoutAuthenticator`（超时防挂死） |
| 预测 / 回滚 | `Assets/Mirror/Examples/PredictedRigidbody/`、`LagCompensation/` | 客户端预测 + 服务端回滚、历史碰撞盒——FPS 级确定性 |
| 发现 | `Mirror.Discovery`（已用 `NetworkDiscoveryBase`） | 官方模板 + 社区 EnlightenedOne/MirrorNetworkDiscovery（修复版） |
| 房间示例 | `Assets/Mirror/Examples/Room/` | 三场景（Offline/Room/Game）+ 记分 + 返回房间重开 |
| Edgegap 商业化联机 | `Assets/Mirror/Examples/EdgegapLobby/` | 通过 `EdgegapLobbyKcpTransport` 对接 Edgegap 的 Lobby+Relay（云匹配/中继）。项目根目录已有 `Edgegap.csproj`——素材就位但没接线（详见 P3） |

### 🌍 GitHub 社区主流项目

| 项目 | 定位 | 核心功能 | 参考价值 |
|---|---|---|---|
| Sean-Zeo/Multi-Room-Manager-For-Mirror-Unity | 单进程多房间 | 加法场景 + SceneInterestManagement 隔离房间、动态建/删房、空房自动卸载、房间列表 UI、"免费版 Photon" | ⭐⭐⭐ 本框架"共享房间"的下一步 |
| alexandria-p/mirror-lobby | P2P 主菜单样板 | 名字输入→Host/Join、房间列表、回合计时、计分、房间内重开/回菜单、断线/房主退出处理 | ⭐⭐⭐ README 已引用的样板，UI 流程完整 |
| AlexRak2/Coop-Party-Template-Unity-Mirror-Steam | Steam 组队模板 | 创建/加入队伍、Steam 好友邀请、Steam 大厅、Ready、FPS 控制 | ⭐⭐ Steam 大厅管线 |
| Onurkan811/UnitySteamLobbySystem | Steam 大厅 | 好友邀请、大厅搜索、实时玩家列表、Ready 状态、同步 UI | ⭐⭐ |
| ojh123/Unity2D_SteamMultigame | Steam P2P 匹配 | `SteamLobby.cs` 房间增删改查 + RoomListItem UI | ⭐ 房间列表实现 |
| JFroggo-Gaming/Unity-Lobby-Asset | 大厅模板 | 多人大厅封装 | ⭐ |
| EnlightenedOne/MirrorNetworkDiscovery | 发现增强 | <600 行 UDP 广播，4 个 API，独立于传输层 | ⭐⭐ 补 `SocketRoomDiscovery` 的坑 |
| Unity-Technologies/unity-relay-mirror-sample | 官方 Relay 样例 | UTP + Unity Relay 公网 | ⭐ 公网替代 |
| James-Frowen（Mirror 核心维护者） | 传输生态 | FizzySteamworks（Steam 传输）、LiteNetLib4Mirror、Telepathy（MMO TCP）、QuickStartHeadlessMirror（无头服务器） | ⭐⭐⭐ 传输层扩展 |
| DracoArts/Mirror-Unity-Multiplayer-Networking | 教学 | 全核心概念逐条示例 | ⭐ 新手文档 |
| Aurora Engine（Asset Store $30） | 商业 FPS 框架 | 游戏模式、击杀榜、记分板、商店、观战、房间大厅、服务器选项 | ⭐⭐ 功能参照 |

---

## 三、关键洞察：主流方案都在用而本框架没用的东西

1. **`NetworkRoomManager` 该继承而不是重写。** 当前手写 `SocketNetworkManager.cs` 的房间逻辑（ready/phase/leader/start）与 `NetworkRoomManager` 高度重叠，但没有 roomSlots 管理、pendingPlayers 场景加载竞态处理、`ReplacePlayerForConnection` 状态迁移、headless 空房回收。改继承 `NetworkRoomManager` 可白得这些。→ 见 P1。

2. **单服务器多房间 = Scene/Match 兴趣管理 + 加法场景。** 这是 Sean-Zeo 方案和 Mirror `MultipleMatches` 的共同路径。本框架目前是"一进程一房间一端口"，离"可运营"差一个兴趣管理层。→ 见 P2。

3. **UI 是主流框架的标配，本框架 demo 是空壳。** 所有样板都有完整的主菜单/房间列表/Ready/开始/计分 UI 流程。本框架 `PcRoomHud`（IMGUI）与 uGUI 空壳并存。→ 见 P0（HUD 去重）与 P2（房间列表 UI）。

4. **商业化联机路径就摆在本地。** Edgegap（csproj 已生成）+ Unity Relay + Steam 传输（FizzySteamworks）——公网/NAT 穿透的官方三选一。README 声称 *"public matchmaking and NAT traversal are intentionally separate services"*——现在是补齐它们的最好时机。→ 见 P3。

---

## 四、本框架 vs 主流：差距表

| 维度 | 现状 | 主流 | 优先级 |
|---|---|---|---|
| 房间状态机 | 手写（有权限漏洞、Leader 边角） | `NetworkRoomManager` 全生命周期钩子 + pending 竞态处理 | 🔴 高 |
| 单服务器多房间 | ❌ 无 | Scene/Match 兴趣管理 + 加法场景 | 🔴 高 |
| 房间列表/匹配 | `SocketRoomDiscovery` 广播但无人消费 + 端口丢失 bug | RoomListItem UI + 心跳清理 | 🔴 高 |
| UI 流程 | IMGUI + uGUI 空壳 | 完整主菜单/房间列表/Ready/计分 | 🟠 中 |
| 认证 | UniqueName 仅 | 四种官方 Authenticator | 🟠 中 |
| 公网联机 | ❌ | Edgegap / Unity Relay / Steam / LiteNetLib | 🟠 中 |
| 反作弊 | ❌ | Mirror Guard（可选付费）、服务器权威 + 校验 | 🟠 中 |
| 预测/回滚 | ❌（服务器权威移动） | Mirror 原生 Prediction / LagCompensation | 🟡 低（按需） |
| 测试/CI | 零测试 | 骨架项目普遍带测试 | 🟡 低 |

---

## 五、完善路线图（按依赖顺序）

| 阶段 | 内容 | 预估 | 状态 |
|---|---|---|---|
| **P0** | 地基修复：Prefab 断链、HUD 重复、入口文档、`ServerReturnToLobby` 权限校验、git 基线 | 半天 | 详见落地计划 |
| **P1** | 架构升级：继承 `NetworkRoomManager/NetworkRoomPlayer`，`ReplacePlayerForConnection` 迁移 | 1–2 天 | 详见落地计划 |
| **P2** | 房间化：Scene/Match 兴趣管理 + 加法场景实现一端口多房间；补房间列表 UI | 2–3 天 | 详见落地计划 |
| **P3** | 商业化联机：Edgegap / Unity Relay / FizzySteamworks 三选一；Timeout/Basic Authenticator | 按需 | 详见落地计划 |
| **P4** | 工程化：InteractionLeaseRules / Leader 转移 / 房间权限 NUnit 测试；`OnClientSceneChanged` 冒烟脚本 | 1 天 | 详见落地计划 |

> **与报告初稿的差异勘误**：本仓库 git 已存在（基线提交 `56fd9ab`），报告初稿"无 git"已过时；`Assets/MultiplayerGenerated/` 已初始化（demo 已跑过），P0 不再是"从零 init"，而是"检查已生成资产的一致性"。
