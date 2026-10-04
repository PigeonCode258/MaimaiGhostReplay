# MaimaiGhostReplay

> 给 **maimai DX（SDEZ / Sinmai）** 做的「记录模式」Mod。
> 第一局把你每一次判定都录下来，第二局原样复现给你看 —— 同时你依然可以进行操作（谁说单人打不了[协]Love You（bushi））

![license](https://img.shields.io/badge/license-GPL--3.0-blue)
![platform](https://img.shields.io/badge/platform-Windows%20x64-lightgrey)
![game](https://img.shields.io/badge/game-SDEZ%201.70-orange)

---

## 这是什么

一个召唤滚木陪你打maimai的mod（

---

## 环境要求

| 项目 | 要求 |
|---|---|
| 游戏 | **SDEZ 1.70** |
| Mod 加载器（测试环境） | **MelonLoader 0.6.4**（Open-Beta） |

> 只对 SDEZ 1.70 进行测试，其余版本不做可用性保证

---

## 安装

1. 到 Releases 页下载 `MaimaiGhostReplay.dll`
2. 放进游戏的 `Mods\` 目录：

   ```
   <游戏目录>\Package\Mods\MaimaiGhostReplay.dll
   ```

3. 正常启动游戏

**卸载**：删掉 dll 即可

---

## 使用方法

### 开启记录模式

1. 正常选曲、**选好难度**，停在显示 GAME START 卡片的页面
2. **同时长按 1、2、7、8 号键满 3 秒**
   （这页只占用 3/4/5/6 号键，1、2、7、8 是空的，不会和游戏操作打架）
3. 弹出 `ATTENTION! / 已开启记录模式` 的警告框

4. 第二首会自动复现你第一首的每一个判定；**你仍然可以正常操作**，以你打到的 note 为准
5. 第二首结算后，记录模式自动退出

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
| 游戏崩溃 | 检查一下自己的游戏版本？ |

日志里本 Mod 的行都以 `[GhostReplay]` 开头。详细的日志对照表见 **[docs/USAGE.md](docs/USAGE.md)**。

---

## 已知限制

- **slide 回放时你的抢划不会被采纳** —— 为了保证星星滑动动画与箭头逐个熄灭的效果，回放中的 slide 由游戏自身的逻辑推进。tap / hold / touchhold 的抢打**不受影响**。
- **「取并集」= 玩家先判则玩家优先** —— 详见 [docs/INTERNALS.md](docs/INTERNALS.md)。
- **只支持 1P 游玩** 
- **段位 / Freedom / 活动模式不可用** 
- **单人宴会场可用**
- 录制数据只存内存，第二首结算即丢弃

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

## 致谢

- #### **万分感谢7YunLuo的 Token 以及 ZCM-X 提供的技术支持！**

## 许可证

[GPL-3.0](LICENSE)
