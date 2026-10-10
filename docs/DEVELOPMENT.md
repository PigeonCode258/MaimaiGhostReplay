# maimai DX (SDEZ 1.70 / Sinmai) Mod 开发方案

> 本文面向**想给这个游戏写 Mod 的人**：环境结论、可行路线、补丁点地图、编译工具链、避坑清单。

> **📌 本文是「怎么给 maimai DX (SDEZ / Sinmai) 写 Mod」的通用方案文档。**
> 本仓库自己那个「记录模式」Mod 的说明在 [项目首页](../README.md) 与 [INTERNALS.md](INTERNALS.md)。

> 本文所有结论都来自对一份实际安装（SDEZ 1.70）的反编译源码与运行日志的实际读取与实测，不是通用模板。
> 标注 ✅ 的是在验证环境中跑通过的，标注 ⚠️ 的是未验证／需要自行确认的。

---

## 一、环境体检结论（已实测）

| 项目 | 实测结果 |
|---|---|
| 游戏 | `Sinmai`，SDEZ **1.70**，Unity **2018.4.7f1**，x64 |
| 脚本后端 | ✅ **MonoBleedingEdge（Mono），不是 IL2CPP** |
| 游戏逻辑程序集 | `Sinmai_Data\Managed\Assembly-CSharp.dll`（4.3 MB）→ 已完整反编译为 2502 个 `.cs` |
| Mod 加载器 | **MelonLoader v0.6.4 Open-Beta**，Runtime Type = `net35` |
| 已有 Mod | **AquaMai**（由 MuMod 接管，运行时从缓存加载）+ **MuMod** + XUnity.AutoTranslator 等 |
| 联机方案 | segatools + AquaDX（`play.mumur.net`）+ `mai2hook.dll` + `amdaemon.exe` |
| 验证环境编译器 | ❌ 无 dotnet SDK、无 Visual Studio、无 JetBrains、无 dnSpy<br>✅ 有系统自带的 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（C# 5）和 `MSBuild.exe` |

### 关键结论

**1) Mono 后端 = 天大的好消息。**
`Assembly-CSharp.dll` 是真正的托管 IL，所以你可以用 Harmony 在**运行时**打补丁，完全不用改游戏文件、不用碰 `global-metadata`、不用处理 IL2CPP 的符号问题。难度比 IL2CPP 游戏低一个数量级。

**2) ⚠️ 不要写死游戏安装路径**

`MelonLoader\Latest.log` 里会记录实际运行路径（`Core::BasePath = ...`），
而日志里也常常残留着别的机器、别的游戏副本留下的旧路径。

**一切以当前日志里 `Core::BasePath` 那一行为准**；构建时用 `-GameRoot` 指到实际安装目录即可，
不要照抄本文或别处文档里的示例路径。

**3) AquaMai v1.9.7 的结构（决定了你能怎么扩展它）**
`AquaMai.dll` 里内嵌了压缩子程序集：

```
AquaMai.Core.dll.compressed
AquaMai.Mods.dll.compressed
AquaMai.Config.dll.compressed
AquaMai.Config.Interfaces.dll.compressed
AquaMai.ErrorReport.exe.compressed
```

它是个**引导器**：启动时解压并加载真正的功能代码。所以你**不能**"往 AquaMai 里丢一个 dll"来加功能——要么 fork 源码重编，要么写独立 Mod（见下方路线选择）。

---

## 二、四条可行路线（按推荐度排序）

### 🥇 路线 A：独立 MelonLoader Mod + Harmony 补丁

**这是首选，我已经帮你把整套工程搭好并实测编译通过。**

- 不动游戏本体任何文件，卸载 = 删掉一个 dll
- 与 AquaMai / MuNet 共存（MelonLoader 支持多 Mod）
- 用 Harmony 2.10.2（MelonLoader 自带 `0Harmony.dll`）

适用：90% 的需求（改判定、加 UI、改行为、加功能、数据记录、自动化……）

### 🥈 路线 B：Fork AquaMai 源码，加自己的模块

适用场景：你想要的是**"给 AquaMai 加一个开关/功能"**，并且想白嫖它的基础设施：

- TOML 配置系统（`AquaMai.toml`，1189 行，自动生成带中文注释的配置）
- 运行时 Mod 设置界面（游戏内 GUI）
- i18n（en/zh）
- 已有 36 个功能模块的代码可参考

代价：需要拉源码、按它的框架写 `IModule`、重新构建整个 AquaMai。
仓库：`https://github.com/MuNET-OSS/AquaMai`

### 🥉 路线 C：直接改 `Assembly-CSharp.dll`（dnSpy 直改）

用 dnSpy 打开 → 编辑 IL/C# → 保存。

- 优点：不用加载器，改完就生效
- 缺点：**破坏了与 AquaMai 的兼容基础**（AquaMai 的补丁基于原方法签名/IL，你改了它就可能失效或崩溃）；升级版本要重做；难以维护

**只在"一次性、极简改动"时考虑。** 有 MelonLoader 在，没必要。

### 路线 D：纯资源替换（零代码）

AquaMai 已经内置了这些，**先确认你要的东西是不是已经有了**：

| 需求 | AquaMai 配置节 | 放文件的位置 |
|---|---|---|
| 换封面/底板/边框/立绘/头像 | `[GameSystem.Assets.LoadLocalImages]` | `LocalAssets\` |
| 自定义 PV（mp4 / 用封面代替） | `[GameSystem.Assets.MovieLoader]` | `LocalAssets\` |
| 换字体（修中文字形缺失） | `[GameSystem.Assets.Fonts]` | `LocalAssets\Fonts\` |
| 换 Logo / 店名 / 按键贴图 / 投币文案 | `[Fancy.CustomLogo]` `[Fancy.CustomPlaceName]` `[Fancy.CustomButton]` `[Fancy.CustomCreditsString]` | `LocalAssets\Buttons` 等 |
| 服务器公告 / 通知 / 资源 | `[Enhancement.ServerAnnouncement]` `[Enhancement.ServerNotice]` `[Enhancement.ServerResources]` | — |
| **自制谱面** | 用 **MaiChartManager**（AquaMai 官方推荐配套工具）<br>`https://github.com/MuNET-OSS/MaiChartManager` | — |

---

## 三、本仓库的工程结构

```
MaimaiGhostReplay\
├── src\                       ← Mod 源码（7 个 .cs，全部参与编译）
├── build.ps1                  ← 一键编译（零安装，用系统自带 csc.exe）  ✅ 实测通过
├── MaimaiGhostReplay.csproj   ← 装了 .NET SDK 后的现代构建方式          ⚠️ 未验证
├── tools\FindMethod.ps1       ← 用 Mono.Cecil 查游戏类/方法签名         ✅ 实测通过
├── tools\VerifyPatches.ps1    ← 从产物反解所有 HarmonyPatch 目标并核对  ✅ 实测通过
├── examples\MinimalMod.cs     ← 最小示例（不参与编译）
├── docs\                      ← 使用 / 实现 / 本文
└── build\MaimaiGhostReplay.dll← 编译产物（不参与提交）
```

### 实测结果

```
编译器: %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
引用 20 个程序集
[OK] built: build\MaimaiGhostReplay.dll

补丁检查    checked=37  ok=37  bad=0
反射查找    ok=34  bad=0
```

产物元数据（用 Cecil 复核）：

```
Assembly: MaimaiGhostReplay
引用: MelonLoader 0.6.4.0 / 0Harmony 2.10.2.0 / Assembly-CSharp 0.0.0.0 / mscorlib 4.0.0.0
```

### 怎么用

```powershell
# 1) 编译。-GameRoot 默认是「本仓库上一级 \SDEZ170\Package」，
#    仓库没和游戏放在一起时必须显式指定
cd <仓库所在目录>\MaimaiGhostReplay
.\build.ps1 -GameRoot "<游戏目录>\Package"

# 2) 编译 + 直接部署到游戏（务必先完全退出游戏，否则 dll 被占用）
.\build.ps1 -GameRoot "<游戏目录>\Package" -Deploy

# 3) 静态校验产物里的 Harmony 补丁目标是否真的存在
.\tools\VerifyPatches.ps1 -GameRoot "<游戏目录>\Package"

# 4) 查游戏里的方法签名（写补丁前必备）
.\tools\FindMethod.ps1 -Type Manager.GameManager -Methods
.\tools\FindMethod.ps1 -Type NoteBase -Methods
.\tools\FindMethod.ps1 -Search Judge
```

> ⚠️ `build.ps1` 与 `VerifyPatches.ps1` **必须分成两个独立的 PowerShell 进程**运行 ——
> 后者会用 Mono.Cecil 内存映射产物 dll，同进程再编译会报 `CS0016 文件被占用`。

---

## 四、补丁点位地图（Hook Map）

以下签名全部用 Mono.Cecil 从**真实的** `Assembly-CSharp.dll` 里读出来的，可以直接照着写 Harmony 补丁。

### 4.1 判定 / 得分（最常改的地方）

| 目的 | 类型 | 成员签名 |
|---|---|---|
| 单个 note 的判定入口 | `Monitor.NoteBase` | `protected virtual bool Judge()` |
| 判定结果写入成绩 | `Monitor.NoteBase` | `protected virtual void SetPlayResult()` |
| 判定时机算法（核心） | `NoteJudge` ⚠️全局命名空间 | `static ETiming GetJudgeTiming(ref float _fMsec, float optionJudgeTiming, EJudgeType type)` |
| 滑键判定 | `NoteJudge` | `static ETiming GetSlideJudgeTiming(ref float _fMsec, float optionJudgeTiming, EJudgeType type, float lastWaitTime)` |
| 判定 → 分数 | `NoteScore` ⚠️全局命名空间 | `static uint GetJudgeScore(ETiming judge, EScoreType scoreType)` |
| 判定窗口起止 | `NoteJudge` | `static float GetNoteCheckStart(EJudgeType type)` / `GetNoteCheckEnd(...)` |
| 判定显示 | `Monitor.JudgeGrade` | （见 `Monitor\JudgeGrade.cs`） |

### 4.2 AutoPlay / 模式开关

| 目的 | 类型 | 成员签名 |
|---|---|---|
| 是否 AutoPlay | `Manager.GameManager` | `public static bool IsAutoPlay()` |
| AutoPlay 的判定结果 | `Manager.GameManager` | `public static NoteJudge.ETiming AutoJudge()` |
| 隐藏 note（节奏测试曲） | `Manager.GameManager` | `public static bool ForceHideNote(int monIndex)` |
| 是否宴会场 | `Manager.GameManager` | `public static bool IsUtage { get; set; }` |
| 是否段位 | `Manager.GameManager` | `public static bool IsCourseMode { get; set; }` |
| 是否 Freedom | `Manager.GameManager` | `public static bool IsFreedomMode { get; set; }` |
| note 流速 | `Manager.GameManager` | `static float GetNoteSpeedForBeat(int index)` / `GetNoteSpeed(int index)` |

### 4.3 时间轴 / 谱面

| 目的 | 类型 | 成员签名 |
|---|---|---|
| 当前播放毫秒 | `Manager.NotesManager` | `public static float GetCurrentMsec()` |
| 开始 / 暂停 / 停止 | `Manager.NotesManager` | `static void StartPlay(float msecStartGap)` / `Pause(bool)` / `StopPlay()` |
| 加载谱面 | `Manager.NotesManager` | `public bool loadScore(SessionInfo sessionInfo)` |
| 加载谱面头 | `Manager.NotesManager` | `public bool loadScoreHeader(SessionInfo sessionInfo)` |
| 每帧更新 note | `Manager.NotesManager` | `public void updateNotes()` |
| 谱面解析器 | `Manager.NotesReader` | 46 KB，见 `Manager\NotesReader.cs` |
| 单曲实例 | `Manager.NotesManager` | `public static NotesManager Instance(int monitorId)` |

### 4.4 成绩 / 玩家

| 目的 | 类型 | 成员签名 |
|---|---|---|
| 取某个玩家某曲成绩 | `Manager.GamePlayManager` | `GameScoreList GetGameScore(int index, int trackNo)` |
| 是否重开 | `Manager.GamePlayManager` | `bool IsQuickRetry()` / `SetQuickRetryFrag(bool)` |
| 是否暂停 | `Manager.GamePlayManager` | `bool IsPauseGame()` / `SetPauseGame(bool)` |
| 玩家数 | `Manager.GamePlayManager` | `int GetPlayerNum(int trackNo)` |
| 玩家数据 | `Manager.UserData` | `IsGuest() IsNewUser() IsHuman() IsTrial() ...`（53 KB 的类） |
| 玩家容器 | `Manager.UserDataManager` | `Singleton<UserDataManager>.Instance.GetUserData(monitorIndex)` |

### 4.5 输入 / 音频 / 配置

| 目的 | 类型 | 成员签名 |
|---|---|---|
| 按键 / 触摸 | `Manager.InputManager` | `static bool GetButtonDown(int monitorId, ButtonSetting button)` 等 30+ 个 |
| 音频播放 | `Manager.SoundManager` | 见 `Manager\SoundManager.cs`（15 KB） |
| 音频触发 | `Manager.SoundCtrl` | 见 `Manager\SoundCtrl.cs`（12 KB） |
| ini 读写 | `MAI2System.IniFile` | 读 `mai2.ini` / `segatools.ini` |
| 游戏内选项 | `Manager.OptionDataManager` | 见 `Manager\OptionDataManager.cs` |

### 4.6 流程 / 界面（做 UI 类 Mod 用）

- 流程机：`Manager.ProcessManager` + `Process\*Process.cs`
  （`MusicSelectProcess` 227 KB、`ResultProcess` 57 KB、`GameProcess` 34 KB……）
- 界面：`Monitor\*Monitor.cs`（`MusicSelectMonitor` 124 KB、`MapResultMonitor` 194 KB……）
- 游戏开始时的一次性钩子：`Monitor.GameMonitor.Initialize(int monIndex, bool active)` ✅已在示例里用

---

## 五、编译工具链：两条路

### 方案 B：零安装（✅ 已验证可用）

用系统自带的 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`：
`build.ps1` 已经封装好了。

- ✅ 现在就能用，不用下载任何东西
- ⚠️ **语言版本只有 C# 5**：没有字符串插值 `$"..."`、没有 `?.`、没有 `nameof`、没有表达式体成员、没有自动属性初始化器
- ✅ 目标程序集是 `mscorlib 4.0.0.0`，和游戏的 `Assembly-CSharp.dll` 一致，**能正常加载**

### 方案 A：装 .NET SDK（推荐，做正经项目时）

1. 装 **.NET SDK 8**（`https://dotnet.microsoft.com/download`）
2. 用本仓库已写好的 `MaimaiGhostReplay.csproj`：`dotnet build`
3. 获得 C# 12、NuGet、IDE 智能提示、调试

> ⚠️ **不要**从 NuGet 装 `Lib.Harmony`——游戏自带的是 `0Harmony 2.10.2.0`，版本对不上会在加载时炸。`MaimaiGhostReplay.csproj` 里已经改成直接引用游戏自带的 dll。

### 反编译 / 阅读代码

`SDEZ1.70-Csharp\` 里已经有全套反编译源码（2502 个 `.cs`），直接用 VS Code 打开读就行。

想再压一份带语法高亮的**实时**视图，装 **dnSpy** 或 **ILSpy**，把 `Sinmai_Data\Managed\Assembly-CSharp.dll` 拖进去。

---

## 六、避坑清单（都是实际踩到的坑）

1. **PowerShell 5.1 读 UTF-8 无 BOM 的 .ps1 会按 GBK 解码**，中文字符会把后面的 ASCII 字符"吃掉"，导致路径串被破坏（实际撞到过 `fatal error CS2021: 文件名"씪ᄵő"太长或无效`）。
   → **凡是有中文注释的 .ps1，必须存成 UTF-8 with BOM。** 本仓库的 `build.ps1` / `FindMethod.ps1` 已经加好 BOM。

2. **不要引用 `Sinmai_Data\Managed\` 下的 `System.Core.dll` / `System.Xml.dll` / `mscorlib.dll`**，csc 会自动从框架目录引用同名程序集，重复引用报 `CS1703`。

3. **MelonLoader 报的 `Runtime Type: net35` 指的是加载器版本，不是你的 Mod 必须 net35。**
   实测：AquaMai 自己是 `net472`，游戏逻辑是 `mscorlib 4.0.0.0`，本仓库产物也是 `mscorlib 4.0.0.0` —— **都能跑**。Mono 会把 `mscorlib 2.0.0.0` 统一到 `4.0.0.0`。

4. **`NoteJudge` 和 `NoteScore` 在全局命名空间**（没有 namespace）。写补丁时别写成 `Monitor.NoteJudge`，会编译不过。

5. **`MelonLoader.MelonEnvironment` 在 0.6.4 的 net35 里不存在**（编译时撞到 `CS0234`）。要拿版本号用 `MelonLoader.MelonLoaderBase` 或干脆别拿。

6. **`MelonGame` 属性要写对**：`app.info` 里是 `sega-interactive` / `Sinmai`。
   ```csharp
   [assembly: MelonGame("sega-interactive", "Sinmai")]
   ```

7. **`[HarmonyPatch]` 打 protected/private 方法**：用字符串名，`__instance` 参数声明成声明类型即可。示例见 `examples\MinimalMod.cs`。

8. **改判定/成绩类 Mod 务必先备份 `UserData\`**。AquaMai 有 `[UX.DontRuinMyAccount]`，说明这是真实存在的翻车点。

---

## 七、常见 Mod 方向与落点

| 想做的 | 建议起点 |
|---|---|
| 自定义判定窗口 / 判定显示 | 补丁 `NoteJudge.GetJudgeTiming` |
| 强制某判定（全 Critical 等） | 补丁 `NoteJudge.GetJudgeTiming` 或 `Manager.GameManager.AutoJudge` |
| 游戏内显示额外信息（判定统计、Fast/Slow） | 补丁 `Monitor.NoteBase.SetPlayResult` + 自己画 GUI（`OnGUI` / Unity IMGUI） |
| 改流速 / 判定偏移的上限 | 补丁 `Manager.GameManager.GetNoteSpeed` / `NoteJudge.JudgeAdjustMs` |
| 记录游玩数据 / 导出成绩 | 补丁 `Manager.GamePlayManager.GetGameScore` + `Monitor.GameMonitor.Initialize` |
| 解锁 / 全开 | AquaMai 的 `[GameSystem.Unlock]` 已经有了，先看够不够 |
| 自制谱面 / 换歌 | 走 MaiChartManager，不是写 Mod |
| 换 UI 素材 / 字体 | 走路线 D（`LoadLocalImages` 等），零代码 |
| 加游戏内菜单 / 设置面板 | 参考 AquaMai 的 `AquaMai.Core.Helpers.GuiSizes` + `KeyListener`，或走路线 B fork |
