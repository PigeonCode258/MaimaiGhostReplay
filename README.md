# MaimaiGhostReplay

> 给 **maimai DX（SDEZ / Sinmai）** 做的「记录模式」Mod。
> 第一局把你每一次判定都录下来，第二局原样复现给你看 —— 同时你依然可以上手抢打。

![license](https://img.shields.io/badge/license-GPL--3.0-blue)
![platform](https://img.shields.io/badge/platform-Windows%20x64-lightgrey)
![game](https://img.shields.io/badge/game-SDEZ%201.70-orange)

---

## 这是什么

一个 MelonLoader Mod。开启「记录模式」后：

1. **第一首** —— 正常游玩，后台悄悄记录你的每一个判定（含判定时刻、hold 的按下与松手、slide 的划动路径）。
2. **第二首** —— 同一首歌同一难度，Mod 把你第一首的判定**逐 note 原样复现**出来：
   - 判定结果、达成率、combo、判定统计都会忠实还原
   - 该响的判定音会响
   - slide 的星星会沿轨道滑动，箭头随星星经过逐个熄灭
   - hold 会显示成「被按住」的样子，松手时机也按你原来那样
3. **你随时可以上手抢打** —— 谁先判到就按谁的算。

全程**不修改任何游戏文件**，卸载 = 删掉一个 dll。

---

## 环境要求

| 项目 | 要求 |
|---|---|
| 游戏 | maimai DX **SDEZ 1.70**（内部代号 Sinmai） |
| Mod 加载器 | **MelonLoader 0.6.4**（Open-Beta） |
| 系统 | Windows x64 |
| 运行环境 | 你的 segatools + 私服环境需先能正常游玩 |

> 只在 SDEZ 1.70 上验证过。别的版本游戏内部方法签名可能不同，Mod 会拒绝加载有问题的补丁并在日志里报出来。

---

## 安装

1. 到 Releases 页下载 `MaimaiGhostReplay.dll`
2. 放进游戏的 `Mods\` 目录：

   ```
   <游戏目录>\Package\Mods\MaimaiGhostReplay.dll
   ```

3. 正常启动游戏

**卸载**：删掉那个 dll 即可，不会残留任何东西。

和 AquaMai、MuNet 等常见 Mod **可以共存**。

---

## 使用方法

### 开启记录模式

1. 正常选曲、**选好难度**，停在那个「再按一次 4 号键就进游戏」的页面（显示 GAME START 卡片的那页）
2. **同时长按 1、2、7、8 号键满 3 秒**
   （这页只占用 3/4/5/6 号键，1、2、7、8 是空的，不会和游戏操作打架）
3. 弹出 `ATTENTION! / 已开启记录模式` 的警告框 —— 样式与游戏内 track skip 的警告框完全一致

### 走完两首

4. 按 4 开始第一首，**正常打**（后台在录制）
5. 第一首结算后回到选曲页，光标会自动落在**同一首歌同一难度**上，再按 4→4→4 进第二首
6. 第二首会自动复现你第一首的每一个判定；**你仍然可以正常操作**，你打到的 note 以你的为准
7. 第二首结算后，记录模式自动退出

### 取消

在同一个页面**再长按一次** 1+2+7+8，会弹「已关闭记录模式」。

---

## 配置

配置文件由 MelonLoader 生成在 `<游戏目录>\Package\UserData\MelonPreferences.cfg`，段名 `[GhostReplay]`：

| 键 | 默认 | 说明 |
|---|---|---|
| `Enabled` | `true` | 总开关。设为 `false` 则完全不加载任何补丁 |
| `LongPressMs` | `3000` | 手势长按时长（毫秒） |
| `DumpRecordToLog` | `true` | 是否把录制明细打进日志（验收完可以关掉） |

---

## 常见问题

| 现象 | 先查什么 |
|---|---|
| 长按没反应 | 日志里有没有 `补丁应用完成: 成功 36 / 失败 0`；是不是停在正确的页面（GAME START 卡片页）；是不是 2P 在场 |
| 弹框没有文字 / 还是日文 | 日志里的失败列表有没有 `P_WindowMessageIDEnum_GetName` |
| 第二首没还原 | 日志里 `录制完成: N 个 note` 的 N 是不是 0；`第二首匹配成功` 有没有出现 |
| 第二首弹「未选择同一首曲目」 | 你在第二首改了歌或难度 —— 这是**设计行为**（记录模式会退出） |
| note 一出现就消失 | 看日志的 `判定时刻区间`，`min` 是否接近 0 |
| 游戏崩溃 | 把 `MelonLoader\Latest.log` 发到 Issues；`Enabled = false` 可以完全停用而不影响其他 Mod |

日志里本 Mod 的行都以 `[GhostReplay]` 开头。详细的日志对照表见 **[docs/USAGE.md](docs/USAGE.md)**。

---

## 已知限制

- **slide 回放时你的抢划不会被采纳** —— 为了保证星星滑动动画与箭头逐个熄灭的效果，回放中的 slide 由游戏自身的逻辑推进。tap / hold / touchhold 的抢打**不受影响**。
- **「取并集」= 玩家先判则玩家优先** —— 你抢在录制时刻之前打到，就按你自己的算（可好可坏）。详见 [docs/INTERNALS.md](docs/INTERNALS.md)。
- **只支持单人 1P** —— 2P 在场时手势不生效。
- **段位 / Freedom / 活动模式不可用** —— 它们的 track 语义与普通模式不同。
- **宴会场可用**，但「双人宴会谱」会被拒绝（单人游玩时游戏会自动换曲，会导致第二首配不上对）。
- 录制数据**只存内存**，第二首结算即丢弃，不落盘、不碰 `UserData`。

---

## 从源码构建

本机**不需要安装任何 SDK 也能编译** —— 直接用 Windows 自带的 .NET Framework 编译器。

```powershell
git clone https://github.com/PigeonCode258/MaimaiGhostReplay.git
cd MaimaiGhostReplay

# 编译 + 部署到游戏（把路径换成你自己的）
.\build.ps1 -GameRoot "D:\你的游戏目录\Package" -Deploy

# 只编译不部署
.\build.ps1 -GameRoot "D:\你的游戏目录\Package"
```

装过 .NET SDK 的话也可以用 `dotnet build MaimaiGhostReplay.csproj -p:GameRoot="..."`（这条路径未在本机验证过）。

### 上线前的静态自检

改完补丁后强烈建议跑一次：

```powershell
.\tools\VerifyPatches.ps1 -GameRoot "D:\你的游戏目录\Package"
```

它会用 Mono.Cecil 把编译产物里的每一个 Harmony 补丁目标反解出来，逐个到游戏的 `Assembly-CSharp.dll` 里核对类型与方法签名是否存在，并把运行时按名字反射查找的字段/方法一并查掉。**写错名字的补丁编译期不会报错，靠这个工具才能提前发现。**

预期输出：

```
checked=36  ok=36  bad=0
reflection lookups: ok=33  bad=0
```

---

## 文档

| 文档 | 内容 |
|---|---|
| [docs/USAGE.md](docs/USAGE.md) | 详细安装、使用方法、运行日志对照、可调参数、故障排查 |
| [docs/INTERNALS.md](docs/INTERNALS.md) | 需求实现对照、设计权衡、Stage 5 完成情况 |
| [CHANGELOG.md](CHANGELOG.md) | 每个版本改了什么，以及**为什么会出那个 bug**（根因分析） |
| [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) | 给 maimai DX (SDEZ) 写 Mod 的通用方案：环境结论、可行路线、补丁点地图、编译工具链、避坑清单 |

---

## 免责声明

- 本项目是**非官方**的第三方 Mod，与 **SEGA** 及任何游戏发行商**没有任何关系**。
- 本仓库**不包含**任何游戏资源、谱面、音频、图片或游戏本体文件，也**不包含**游戏程序集的反编译源码。使用者需自行合法拥有游戏。
- 本项目仅供**学习与自用**。请勿用于商业用途，也不要用于任何破坏游戏公平性的场合。
- 使用本 Mod 造成的一切后果由使用者自行承担。

---

## 许可证

[GPL-3.0](LICENSE)
