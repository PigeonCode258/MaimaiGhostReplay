# 使用与故障排查

> 本文由项目原始文档拆分而来，涵盖安装、使用方法、运行日志对照、可调参数与故障排查。
> 实现原理与设计权衡见 [INTERNALS.md](INTERNALS.md)；怎么给这个游戏写 Mod 见 [DEVELOPMENT.md](DEVELOPMENT.md)。
> 想先看这个 Mod 是干什么的，回到 [项目首页](../README.md)。

## 安装


```powershell
cd <仓库所在目录>\MaimaiGhostReplay

# 编译并部署。注意：部署前必须完全退出游戏
# （Sinmai / amdaemon / inject 都关掉），否则 DLL 被占用会报
# `user-mapped section open`。
#
# -GameRoot 指游戏的 Package 目录。它的默认值是「本仓库上一级 \SDEZ170\Package」，
# 所以仓库没有和游戏放在一起时，必须显式指定：
.\build.ps1 -GameRoot "<游戏目录>\Package" -Deploy
```

部署后 `Mods\` 里多出本 Mod，与其它 Mod 互不冲突：

```
Mods\MaimaiGhostReplay.dll  (本 Mod，约 40 KB)
```

卸载 = 删掉 `MaimaiGhostReplay.dll`。

> **路径提醒**：`MelonLoader\Latest.log` 里可能残留别的机器 / 别的游戏副本留下的旧路径。
> **以当前日志里 `Core::BasePath = ...` 那一行为准**，不要照抄文档里的示例路径。

---


## 使用方法


1. 正常开局、选曲、**选好难度**，停在那个「再按一次 4 号键就进游戏」的页面（显示 GAME START 卡片的那页）。
2. **同时长按 1、2、7、8 号键满 3 秒**（这页只占用 3/4/5/6 号键，1/2/7/8 是空的）。
3. 弹出 `ATTENTION! / 已开启记录模式` 的警告框 —— 样式与游戏内 track skip 的警告框完全一致（复用的是同一个 prefab）。
4. 按 4 开始第一首 → 全程正常打，后台在录制。
5. 第一首结算后回到选曲页，**光标会自动落在同一首歌同一难度上**（这是游戏自带的行为），再按 4→4→4 进第二首。
6. 第二首会自动把你第一首的判定逐 note 还原出来；**你仍然可以正常操作**，你打到的 note 以你的为准。
7. 第二首结算后自动退出记录模式。

**中途想取消**：在同一个页面再长按一次 1+2+7+8，会弹「已关闭记录模式」。

---


## 运行日志对照


`MelonLoader\Latest.log`，本 Mod 的行都以 `[GhostReplay]` 开头。

**正常开启时你会看到：**

```
[GhostReplay] MaimaiGhostReplay v0.7.2.1 已加载（手势：开始游戏页长按 1+2+7+8 3000ms）
[GhostReplay] 补丁应用完成: 成功 37 / 失败 0 （共 37）      <-- 必须是 37/0
[GhostReplay] 手势触发（长按 1/2/7/8 满 3000ms）
[GhostReplay] 记录模式开启: music=1234 diff=3 (来源 dsi=3 cd=3 levelTab=False) long=False 剩余=3/2
[GhostReplay] 第二首预选: music=1234 diff=3 定位成功
[GhostReplay] 强制难度: 3 (原 dsi=-1)                    <-- v0.3.0 新增
[GhostReplay] 回放判定音: 812 次（含 hold 头判 96 次）    <-- v0.3.0 新增
[GhostReplay] 回放 slide: 自动推进 143 条 / 交给超时路径 5 条   <-- v0.5.0 新增
[GhostReplay] 回放 hold: 注入头判 96 条 / 玩家自己按 3 条 / 录制无头判 12 条   <-- v0.5.1 新增
[GhostReplay] 回放 hold 保持态: 补出 93 条                              <-- v0.6.0 新增
[GhostReplay] 回放 hold 粒子: 起 93 条 / 止 93 条 / 重放 2 次 / 结算 93 条   <-- v0.7.1 新增
[GhostReplay] 回放打击特效: 809 次（tap=700 / ex=0 / break=13 / touch=96）  <-- v0.7.0 新增
[GhostReplay] 弹窗: 已开启记录模式
[GhostReplay] 开始录制 track=1 music=1234 diff=3
[GhostReplay] 录制完成: 812 个 note（music=1234 diff=3）
[GhostReplay]   含 hold/touchhold 头判: 96，含 slide 路径: 143
[GhostReplay]   判定时刻区间: 1234ms ~ 187654ms     <-- v0.2.0 新增自检，min 不应接近 0
[GhostReplay]   含 hold/touchhold 头判: 96，含 slide 路径: 143
[GhostReplay]   [#0] kind=0 timing=1 @ 1234.5ms ...
[GhostReplay] 第二首匹配成功，进入回放: music=1234 diff=3
[GhostReplay] 开始回放 track=2 已录 note 数=812
[GhostReplay] 回放局结束，退出记录模式
```

**重点看这几行：**
- `补丁应用完成: 成功 37 / 失败 0` —— 若不是 37/0，会紧跟着打出哪个补丁失败及原因。
- `录制完成: N 个 note` —— 这个 N 应该等于该谱面的总 note 数（**这是录制成功的核心证据**）。
- `判定时刻区间: x ms ~ y ms` —— **min 不应接近 0**。若 min 是 0 或只有几百 ms，说明录制时刻又被打歪了，请把日志发我。
- `第二首预选: ...` —— 第二首光标是否成功落在同曲同难度。
- `回放打击特效: N 次（tap=a / ex=b / break=c / touch=d）` —— **v0.7.0 新增**。四项之和应等于 N，
  且 N 应大致等于 `回放判定音` 的次数（两者是同一批注入判定）。这个计数只证明「特效被请求了」，
  **画面是否真的爆开要用眼睛确认**。
- `回放 hold 粒子: 起 a 条 / 止 b 条 / 重放 c 次 / 结算 d 条` —— **v0.7.1 新增**。
  `起` 应等于上一行 `回放 hold 保持态: 补出 N 条` 的 N；`止` 应接近 `起`（差太多说明还有 hold 没走到收尾）。
  `重放` 是按住期间的自愈次数，通常是个位数 —— 只要不是每帧都在涨就正常。
  同样，**画面是否持续显示要用眼睛确认**。

---


## 可调参数


配置文件由 MelonLoader 生成：`UserData\MelonPreferences.cfg`（段名 `[GhostReplay]`）

```ini
[GhostReplay]
Enabled = true            # 总开关，false 则完全不加载补丁
LongPressMs = 3000        # 手势长按时长
DumpRecordToLog = true    # 是否把录制明细打进日志（验收完可以关掉）
```

---


## 故障排查


| 现象 | 先查什么 |
|---|---|
| 长按没反应 | 日志里有没有 `补丁应用完成: 成功 37 / 失败 0`；是不是停在正确的页面（GAME START 卡片页）；是不是 2P 在场 |
| 弹框没有文字 / 还是日文 | `P_WindowMessageIDEnum_GetName` 是否在失败列表里（Harmony 对扩展方法的参数绑定）。这是唯一一个我没法静态验证的补丁 |
| 第二首没还原 | 日志里 `录制完成: N 个 note` 的 N 是不是 0；`第二首匹配成功` 有没有出现 |
| 第二首弹「未选择同一首曲目」 | 你在第二首改了歌或难度（这是设计行为） |
| 回放命中时看不到打击特效 | `回放打击特效: N 次` 是否为 0。若是 0，把日志发我；若 N 正常但画面没有，也把日志发我（说明抢特效的还有别的东西） |
| 回放里按住 hold 时看不到持续粒子 | 看 `回放 hold 粒子: 起 a 条`。`a = 0` 说明没拉起（把日志发我）；`a` 正常但画面没有也发我。该日志自 Mod v0.7.1 起才有 |
| 游戏崩溃 | 把 `MelonLoader\Latest.log` 发我；`Enabled = false` 可以完全停用本 Mod 而不影响 AquaMai |

---
