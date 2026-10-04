# 使用与故障排查

> 本文由项目原始文档拆分而来，涵盖安装、使用方法、运行日志对照、可调参数与故障排查。
> 实现原理与设计权衡见 [INTERNALS.md](INTERNALS.md)；怎么给这个游戏写 Mod 见 [DEVELOPMENT.md](DEVELOPMENT.md)。
> 想先看这个 Mod 是干什么的，回到 [项目首页](../README.md)。

## 安装


```powershell
cd D:\BaiduNetdiskDownload\maimai-mod-dev

# 编译并部署（默认就是本目录旁的 SDEZ170\Package）
.\build.ps1 -Deploy

# 如果游戏实际在别处，显式指定
.\build.ps1 -GameRoot "X:\你的路径\SDEZ170\Package" -Deploy
```

部署后 `Mods\` 里会有两个文件，互不冲突：

```
Mods\AquaMai.dll            (原有)
Mods\MaimaiGhostReplay.dll  (本 Mod，24 KB)
```

卸载 = 删掉 `MaimaiGhostReplay.dll`。

> **路径提醒**：`MelonLoader\Latest.log` 里记录的历史运行路径是 `D:\maimaiHDD\SDEZ170\Package`，
> 但**这台机器上 `D:\maimaiHDD` 并不存在**，当前唯一存在的安装是
> `D:\BaiduNetdiskDownload\SDEZ170\Package`。如果你平时是从别的地方启动游戏的，
> 请用 `-GameRoot` 指到那里。

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
[GhostReplay] MaimaiGhostReplay v0.1.0 已加载（手势：开始游戏页长按 1+2+7+8 3000ms）
[GhostReplay] 补丁应用完成: 成功 36 / 失败 0 （共 36）      <-- 必须是 36/0
[GhostReplay] 手势触发（长按 1/2/7/8 满 3000ms）
[GhostReplay] 记录模式开启: music=1234 diff=3 (来源 dsi=3 cd=3 levelTab=False) long=False 剩余=3/2
[GhostReplay] 第二首预选: music=1234 diff=3 定位成功
[GhostReplay] 强制难度: 3 (原 dsi=-1)                    <-- v0.3.0 新增
[GhostReplay] 回放判定音: 812 次（含 hold 头判 96 次）    <-- v0.3.0 新增
[GhostReplay] 回放 slide: 自动推进 143 条 / 交给超时路径 5 条   <-- v0.5.0 新增
[GhostReplay] 回放 hold: 注入头判 96 条 / 玩家自己按 3 条 / 录制无头判 12 条   <-- v0.5.1 新增
[GhostReplay] 回放 hold 保持态: 补出 93 条                              <-- v0.6.0 新增
[GhostReplay] 弹窗: 已开启记录模式
[GhostReplay] 开始录制 track=1 music=1234 diff=3
[GhostReplay] 录制完成: 812 个 note（music=1234 diff=3）
[GhostReplay]   含 hold/touchhold 头判: 96，含 slide 路径: 143
[GhostReplay]   判定时刻区间: 1234ms ~ 187654ms     <-- v0.2.0 新增自检，min 不应接近 0
[GhostReplay] 第二首预选: music=1234 diff=3 定位成功   <-- v0.2.0 新增
[GhostReplay]   含 hold/touchhold 头判: 96，含 slide 路径: 143
[GhostReplay]   [#0] kind=0 timing=1 @ 1234.5ms ...
[GhostReplay] 第二首匹配成功，进入回放: music=1234 diff=3
[GhostReplay] 开始回放 track=2 已录 note 数=812
[GhostReplay] 回放局结束，退出记录模式
```

**重点看这两行：**
- `补丁应用完成: 成功 36 / 失败 0` —— 若不是 36/0，会紧跟着打出哪个补丁失败及原因。
- `录制完成: N 个 note` —— 这个 N 应该等于该谱面的总 note 数（**这是录制成功的核心证据**）。
- `判定时刻区间: x ms ~ y ms` —— **min 不应接近 0**。若 min 是 0 或只有几百 ms，说明录制时刻又被打歪了，请把日志发我。
- `第二首预选: ... 定位成功` —— 第二首光标是否成功落在同曲同难度。

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
| 长按没反应 | 日志里有没有 `补丁应用完成: 成功 24 / 失败 0`；是不是停在正确的页面（GAME START 卡片页）；是不是 2P 在场 |
| 弹框没有文字 / 还是日文 | `P_WindowMessageIDEnum_GetName` 是否在失败列表里（Harmony 对扩展方法的参数绑定）。这是唯一一个我没法静态验证的补丁 |
| 第二首没还原 | 日志里 `录制完成: N 个 note` 的 N 是不是 0；`第二首匹配成功` 有没有出现 |
| 第二首弹「未选择同一首曲目」 | 你在第二首改了歌或难度（这是设计行为） |
| 游戏崩溃 | 把 `MelonLoader\Latest.log` 发我；`Enabled = false` 可以完全停用本 Mod 而不影响 AquaMai |

---
