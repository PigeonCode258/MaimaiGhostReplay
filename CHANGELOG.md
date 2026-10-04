# 更新日志

> 每个版本记录的不只是「改了什么」，还有**为什么会出这个 bug**（根因分析）。
> 设计取舍与需求对照见 [docs/INTERNALS.md](docs/INTERNALS.md)。

| 版本 | 主题 |
|---|---|
| v0.6.0 | hold 回放时显示「被按住」的模样（Stage 5 完成） |
| v0.5.1 | 修复 hold 从来没被回放过 |
| v0.5.0 | slide 的星星真正会滑动（Stage 5 第一项） |
| v0.4.0 | 支持宴会场（Utage） |
| v0.3.0 | 修「第二首难度没还原」+「回放没有判定音」 |
| v0.2.0 | 修「note 秒判 miss 不显示」+「第二首未预选」 |
| v0.1.0 | 首个可运行版本（录制 / 回放 / 手势 / 曲数校验） |


## v0.2.0 —— 修复实测发现的 2 个 bug

#### Bug 1：第二首的 note 一出现就被判 miss、且不显示

两个根因，都记清楚避免以后再踩：

**根因 A（主因）：按实例做的绑定缓存被对象池击穿。**
`Monitor\Game\GameCtrl.cs:1177` 每种 note 只 `Instantiate` **64 个**放进对象池，整首歌几百个 note **轮流复用**这 64 个实例（`GameCtrl.cs:1479+` 的 `tapObject.Initialize(note)`）。
v0.1.0 用 note **实例**当字典 key，一个实例绑定到第一条记录后再也不更新 → 后续复用该实例的 note 一生成就命中「早已过去」的判定时刻 → 立刻走 `EndNote()` → `NoteObj.SetActive(false)` → **note 消失**。若那条过期记录本身是 miss，显示的就是 miss。
→ **修法**：删掉实例缓存，每次都直接读 `NoteIndex`。`NoteBase` 用 public 的 `GetNoteIndex()`（零反射零装箱）；`SlideRoot` 没有该方法，反射读字段。

**根因 B：录制混入了非实战判定。**
v0.1.0 挂在 `GameScoreList.SetResult` 上录制，但结算补判也会调它：
`FinishPlay()`（`:1788`，给未判 note 补 `TooLate`）、`SetGhostData()`（`:571`，幽灵成绩）、`SetForceAchivementLowAP()`（`:530`）/ `SetForceAchivement1Miss()`（`:545`）。
这些调用里的 `NotesManager.GetCurrentMsec()` 不是判定时刻，录下来就是错的。
（注：`SetResult` 开头有 `if (noteData.isJudged) return;`，所以这些批量填充只会碰到「从没判过」的 note —— 影响面比想象中小，但确实会污染。）
→ **修法**：录制改挂到 **6 个 `SetPlayResult()`**，它只被实战 `EndNote()` / slide 收尾调用。另外加了 `JudgeMsec <= 0` 的录制端丢弃 + 回放端跳过双重防御。

#### Bug 2：第一首结算后没有预选到同曲同难度

游戏自己在 `MusicSelectProcess.OnStart()` 的第 **1270-1311 行**也会尝试恢复光标，但那段是**按排序方式分支**的：

```csharp
if (IsMaiList()) SearchMusicIndex(...);          // 只有这个分支会按曲目定位
else if ((IsSortEnableCategory() && !IsGhostFolder(0)) | isForceChangeMusic) { ... }
else if (IsGhostFolder(0)) { ... }
else if (!SetGenreSortIndex(CategoryNameList[CurrentCategorySelect]))   // 默认「曲风」排序走这里
    SearchMusicIndex(...);                        // 只有 SetGenreSortIndex 返回 false 才定位
```

`SetGenreSortIndex` 只设分类并把 `CurrentMusicSelect` 归零，**不按曲目定位**。

→ **修法**：Postfix `Process.MusicSelectProcess.OnStart()`，仅在等待第二首时：
1. 反射调游戏自己的 private `SetSortIndexToMaiList(录制曲目ID, 录制难度, true, true)` 强制定位（它会在**全部分类**里搜索）
2. 把 `DifficultySelectIndex[i]` / `CurrentDifficulty[i]` 设成录制难度
3. 调 `MonitorArray[i].SetDeployList(false, false)` 把光标推到画面上 —— 这一步是照搬游戏自己的 `SetGhostJumpIndex()`（`:4193`）的做法，有据可依

#### 附带变更

- 补丁数 **24 → 30**（删 1 个 `SetResult`，加 6 个 `SetPlayResult` + 1 个 `MusicSelectProcess.OnStart`）
- 新增两项日志自检：`判定时刻区间` 和 `第二首预选`
- `AccessTools.FieldRefAccess` 不能用：它是带 `ref` 返回的委托类型，旧版 csc（C# 5）直接报 `CS0648 该语言不支持的类型`。改用普通反射读 slide 索引（同屏 slide 只有个位数，装箱可忽略）

#### 本次仍未修

hold / touchhold 的「保持态」回放、slide 箭头滚动动画回放（Stage 5）。

## v0.3.0 —— 修「第二首难度没还原」+「回放没有判定音」

#### 问题 1：难度没有还原（v0.2.0 没修好）

v0.2.0 把难度写在 `MusicSelectProcess.OnStart()` 的 Postfix 里。这条路径**结构上就是脆的**：从 `OnStart()` 执行完到玩家真正进难度页，中间隔着一整套选曲操作，而期间有**多处**会把 `DifficultySelectIndex` 冲成 `-1`：

| 写入点 | 值 | 触发时机 |
|---|---|---|
| `MusicSelectProcess.cs:1171` | `-1` | `OnStart()` 内部（在 Postfix **之前**） |
| `OptionSelectSequence.cs:164` | `-1` | 从选项页返回选曲 |
| `MenuSelectSequence.cs:130` | `-1` | 强制返回选曲 |
| `DifficultySelectSequence.cs:218` | `-1` | 难度页取消 |
| `MusicSelectProcess.cs:2363 / 2374` | `CurrentDifficulty[j]` | 可玩性判定分支 |

而难度页是这么判断的（`DifficultySelectSequence.cs:48-52`）：

```csharp
MusicDifficultyID musicDifficultyID = MusicDifficultyID.Basic;
if (ProcessProcessing.DifficultySelectIndex[PlayerIndex] != -1)
    musicDifficultyID = (MusicDifficultyID)ProcessProcessing.DifficultySelectIndex[PlayerIndex];
// else 分支最终退回 Basic
```

**只要中间任何一步把它写成 -1，Postfix 里设的值就白设了。**

→ **修法：改到「消费点」上强制，两层：**
1. Prefix `DifficultySelectSequence.OnStartSequence()` —— 在游戏读 `DifficultySelectIndex` **之前**把它和 `CurrentDifficulty` 纠正过来
2. Prefix `MusicSelectMonitor.OnStartDifficultySelect(MusicDifficultyID, Action)` —— 用 **`ref` 改写参数**，直接保证难度页高亮正确，即使前面数据被冲掉也兜得住

> 顺带修正 v0.2.0 的一个误判：`SetPlayableMusic(..., int[] difficultyID, ...)` 对那个数组**只读不写**（`:4093/4128`），逐行核实过，它不是元凶。

#### 问题 2：回放时不播判定音

`NoteBase.EndNote()` 会打判定特效、显示判定等级、写入成绩，但**不播判定音**。判定音只在三处发出：

| 位置 | 触发条件 |
|---|---|
| `NoteBase.Judge()` | 玩家实际打到 |
| `NoteBase.JudgeToolate()` | note 超时未打到 |
| `NoteBase.SetAutoPlayJudge()` | 游戏自带 autoplay |

回放是**直接往 `JudgeResult` 字段写值**，这三个入口一个都不经过 → 下一帧 `NoteCheck()` 直接走 `EndNote()` → **特效/等级/分数都有，唯独没声音**。听到的全是自己实际打到时经 `Judge()` 发出的。

→ **修法**：注入完判定后补调 note **自己的**发声方法（不用自己拼 cue，这样能自动处理 Break / Ex 音符的音色差异）：

| 注入点 | 补调 |
|---|---|
| Tap / Star / Break / BreakStar / Touch / TouchNoteC | 写完 `JudgeResult` 后调 `PlayJudgeSe()` |
| Hold / BreakHold / TouchHold | 还原 `JudgeHeadResult` 后调 `PlayJudgeHeadSe()`（尾判音本来就由 `EndNote()` 负责） |
| Slide | 不动 —— `SlideRoot.NoteCheck()` 收尾分支里本来就有 `PlayJudgeSe()` |

`PlayJudgeSe()` 内部有 `if (!ShotJudgeSound)` 保护，不会重复发声。

`sound` 相关的可达性已核对：`NoteBase.PlayJudgeSe`[protected virtual]、`HoldNote`/`BreakHoldNote` 的 `PlayJudgeSe` + `PlayJudgeHeadSe`、`TouchHoldC.PlayJudgeHeadSe`、`SlideRoot.PlayJudgeSe`。

#### 附带变更

- 补丁数 **30 → 32**
- 新增 4 项日志：`记录模式开启` 加难度来源、`强制难度`、`回放判定音`、`第二首预选` 读回实际值
- 校验脚本 `VerifyPatches.ps1` 的方法查找改为**向上走基类**（与运行时 `Reflect.FindMethod` 行为一致），否则 `BreakNote` 这类"继承而来"的方法会被误报为缺失
- ⚠️ **踩坑记录**：我曾往这个 **ASCII-only** 的校验脚本里插了一行中文注释，PowerShell 5.1 按 GBK 解码后注释行尾吞掉了换行符，把下一行检查条目整行注释掉，导致校验结果少一条。该脚本必须保持纯 ASCII —— 这也是它文件头写明的原因。

#### 本次仍未修

hold / touchhold 的「保持态」回放、slide 箭头滚动动画回放（Stage 5）。

## v0.4.0 —— 支持宴会场（Utage）

宴会场不是「普通模式的变体」，它有自己的难度页和曲目分类，所以普通难度页那两层拦不住它。

| 差异 | 普通曲 | 宴会场 |
|---|---|---|
| 难度页序列 | `DifficultySelectSequence` | **`UtageDifficultySelectSequence`** |
| 难度取值来源 | `DifficultySelectIndex`（`!= -1` 才用） | **`CurrentDifficulty`** |
| 难度页入口 | `OnStartDifficultySelect` | **`OnStartUtageDifficultySelect`** |
| 曲目定位 | `SetSortIndexToMaiList`（只搜 `IsMaiList` 分类） | **`SetSortIndexToExtraGenre`**（utage 属 extra 分类，genre id = 107） |

**改动：**

1. **解除限制** —— 去掉 `IsUtageMusicFolder()` 的一刀切拒绝；只保留「双人宴会谱」拒绝（单人时游戏会自动换曲，会导致第二首配对失败）
2. **难度还原加两层**（对应普通曲的两层）：
   - Prefix `UtageDifficultySelectSequence.OnStartSequence()` → 强制 `CurrentDifficulty[player]`
   - Prefix `MusicSelectMonitor.OnStartUtageDifficultySelect(MusicDifficultyID, Action)` → `ref` 改写参数
3. **曲目定位兜底** —— `SetSortIndexToMaiList` 返回 false 且当前是宴会场时，改用游戏自己的 `SetSortIndexToExtraGenre(musicId, GetMusicGenre(107).genreName)`

**判定与录制/回放核心无需改动**：`NoteCheck()` / `SetPlayResult()` / `JudgeHoldHead()` 等在所有模式下共用，`IsUtage` 只影响玩家输入路径的 `IsJudgeNote()`，回放注入不经过它。

**补丁数 32 → 34**；新增日志 `强制难度(宴会场): N (原 cd=M)`。

**注意**：`utagePlayStyle` 是 `Manager.MaiStudio.UtagePlayStyle`，只有 `SinglePlayerScore = 0` / `DoublePlayerScore = 1` 两个值。

## v0.5.0 —— slide 的星星现在真的会滑动（Stage 5 第一项）

#### 根因

星星的位移动画本身是**纯时间驱动**的，跟判定无关：

```csharp
// SlideRoot.Execute() —— GameCtrl.cs:696 每帧调用
if (base.gameObject.activeSelf) {
    if (!EndFlag) { UpdateAlpha(); MoveStarLane(); UpdateBreakEffect(); }
    NoteCheck();
}
```

`MoveStarLane()` 只按 `currentMsec` 在 `StarLaunchMsec → StarArriveMsec` 之间插值算星星位置。

**问题出在 `InjectSlide` 直接往 `JudgeResult` 写值**，这让 `SlideRoot.NoteCheck()` 越过那道早退闸门：

```csharp
if (GetJudgeResult() == NoteJudge.ETiming.End && !JudgeToolate()) return;   // :710  ← 被跳过
if (lastWaitTime <= 0f || GetJudgeResult() == NoteJudge.ETiming.TooLate)   // :714
{
    ...PlayJudgeSe(); EndFlag = true;
    _baseStarNote.SetActive(false);     // 星星直接消失
    ...隐藏所有箭头...
    JudgeObj.Initialize(...);           // 判定特效
    base.gameObject.SetActive(false);   // 整个 slide 停摆
    SetPlayResult();
}
```

关键在于 **`lastWaitTime` 早在 `Initialize()` 里就按「末格推进距离占比 × 滑动时长」设好并逐帧递减**，也就是说它早就 ≤ 0 —— 全靠 :710 拦着才不误触发。我们一写 `JudgeResult`，闸门失效，**下一帧立即收尾**。

**第二个后果**：`_hitIndex` 始终是 0，所以即使星星在动，**箭头也不会随星星经过而逐个熄灭** —— 那才是肉眼看到的「滑动效果」。

#### 修法：借用游戏自带 autoplay 的「按时间推进」分支

`SlideRoot.NoteCheck()` 里本来就有这么一条（游戏自带 autoplay 用的）：

```csharp
if (!GameManager.IsAutoPlay()) { ...玩家输入路径... }
else if (GameManager.AutoJudge() != NoteJudge.ETiming.TooLate)
{
    float num5 = (currentMsec - StarLaunchMsec) / (StarArriveMsec - StarLaunchMsec - lastWaitTime);
    _hitIndex = (int)(_hitAreaList.Count * num5);     // 按时间推进
    ...把星星已经过的箭头逐个 SetActive(false)...      // ← 滑动效果
    if (num5 >= 1f) _hitIndex = _hitAreaList.Count;   // 推进到底
}
if (_hitIndex >= _hitAreaList.Count) { Judge(); }     // 自然进入判定
```

于是加三个补丁：

| 补丁 | 作用 |
|---|---|
| Prefix `GameManager.IsAutoPlay()` | 仅在「回放中的 slide 的 `NoteCheck()`」期间返回 `true`，让游戏走自动推进分支 |
| Prefix/Postfix `SlideRoot.NoteCheck()` / `SlideFan.NoteCheck()` | 维护上面那个作用域（进入时记下当前 slide + 它的录制记录，退出时清除） |
| Prefix `SlideRoot.Judge()` | 自动推进到底后 `Judge()` 原本取 `GameManager.AutoJudge()`（`AutoPlay==None` 时是 `TooFast`→被转成 `FastGood`），改成写入**录制的判定** |

`SlideRoot.Judge()` 是 `protected bool`，但唯一调用点 `if (_hitIndex >= _hitAreaList.Count) { Judge(); }` **不使用返回值**，所以 Prefix 返回 `false` 跳过原方法是安全的。`SlideFan` 没有重写 `Judge()`，一个补丁覆盖两种 slide。

#### 「录制里是 miss 的 slide」特殊处理

不启用自动推进。因为收尾分支里有：

```csharp
if (GetJudgeResult() == NoteJudge.ETiming.TooLate && _hitIndex >= _hitAreaList.Count - 1)
{
    JudgeResult = NoteJudge.ETiming.LateGood;   // 会把 TooLate 强行抬成 LateGood
}
```

自动推进正好把 `_hitIndex` 顶到末尾踩中它。所以录制的 miss 走游戏自然的超时路径（`JudgeToolate()`）—— 星星照样滑完全程，最后判 TooLate，与原局一致。

#### 权衡（重要）

走自动推进意味着**这条 slide 的玩家输入会被忽略**（`if (!IsAutoPlay()) ... else if (...)`,是二选一）：

- ✅ 星星滑动、箭头逐个熄灭、滑到底才判定
- ⚠️ 不能再自己抢划这条 slide 去「打得更好」（需求 6 的 `slide提前划完` 对 slide 不再成立）
- ✅ tap / hold / touchhold 的玩家覆盖**完全不受影响**

若希望「玩家真的去划了就按玩家的算」，可加 latch（slide 开始瞬间检测玩家是否按着触摸板）。但那会引入「中途切换导致 `_hitIndex` 回退」的视觉抖动，所以本版默认不做。

#### 附带变更

- 补丁数 **34 → 36**；新增日志 `回放 slide: 自动推进 N 条 / 交给超时路径 M 条`
- `InjectSlide` 不再写 `JudgeResult`，改为只做记录查询（`EnterSlide` / `ExitSlide` / `InAutomaticSlide` / `ForceSlideJudge`）
- 判定音不再需要为 slide 补发 —— 自动推进分支与收尾分支自己会调 `PlayJudgeSe()` / `ReserveSlideTouchSe()`

#### 本次仍未做

hold / touchhold 的「保持态」回放（Stage 5 剩余项）。

## v0.5.1 —— 修复 hold 从来没被回放过

#### 根因（必然触发，不是偶发）

`InjectHold` 原来是这样判断「玩家有没有自己按过」的：

```csharp
if (NotesManager.GetCurrentMsec() < r.JudgeMsec) return;   // 等到「尾判时刻」才动手
bool already = (bool)headJudgedField.GetValue(nb);          // 读 HeadJudged
if (already) { _handled.Add(idx); return; }                 // 玩家优先 → 直接跳过
```

问题在于 `r.JudgeMsec` 是**尾判时刻**（`SetPlayResult()` 在 `EndNote()` 里被调用），而到那个时刻 `HeadJudged` **一定**已经是 `true`：

- 要么玩家真按过（`JudgeHoldHead()` 里 `HeadJudged = true`）
- 要么 `HoldNote.NoteCheck():233` 的 `if (JudgeToolate() && !HeadJudged) { HeadJudged = true; }` 在头判窗口过去后把它置真了

**所以 `already` 永远为真，`InjectHold` 一条 hold 都没注入过。** 连带 `_injected` 永远为空，`OverrideHoldTotal()` 也从不生效 —— 所有 hold 都按游戏拿到的**空身体**重算（`JudgeHoldTotal(TailMsec - AppearMsec, HoldReleaseTime, TooLate, BodyOn, false)`），**全部变成 miss**。

#### 修法

1. **改在「录制到的头判时刻」注入**（`r.HeadMsec`），而不是等尾判时刻 —— 那时头判窗口还没过，`JudgeToolate()` 还没来得及污染 `JudgeHeadResult`
2. **区分「玩家真按出的判定」与「超时写进去的 TooLate」**：`headNow != End && headNow != TooLate` 才算玩家优先
3. 用 `LookupRaw()` 查记录（只判存在性），不再用带 `JudgeMsec > 0` 防御的 `Lookup()` —— hold 依赖的是 `HeadMsec`
4. 录制里没有头判（玩家原本就没按上）的记录，直接标记处理并不注入，让游戏的超时路径给出与原来一致的 miss

注入头判后会置 `HeadJudged = true` 并记入 `_injected`，于是尾判 `EndNote() -> JudgeTotalResult()` 的 Prefix 会把结果覆盖成**录制的终态**。

#### 附带变更

- 新增日志 `回放 hold: 注入头判 N 条 / 玩家自己按 M 条 / 录制无头判 K 条`
- 版本 `0.5.0` → `0.5.1`

#### 踩坑记录

`VerifyPatches.ps1` 用 Mono.Cecil **内存映射**了产物 DLL。若在**同一个 PowerShell 进程**里紧接着跑 `build.ps1`，csc 会报：

```
error CS0016: 未能写入输出文件 ... 另一个程序正在使用此文件
```

跟游戏无关，是校验脚本自己持有的映射没释放。两者要分开调用（已写进脚本头部注释）。

#### 本次仍未做

hold / touchhold 的「保持态视觉」回放 —— 即回放时 hold 不会显示成「被按住」的发光状态。**判定与分数是正确的**，只是那个持续发光的视觉没还原。

## v0.6.0 —— hold 回放时显示「被按住」的模样（Stage 5 完成）

#### 根因

v0.5.1 已经让 hold 的**判定与分数**正确了，但视觉还是不对：回放时玩家并没有真的按住，于是

```csharp
// HoldNote.NoteCheck()
bool flag = (!GameManager.IsAutoPlay() && InputManager.GetButtonPush(MonitorId, ButtonId))
            || InputManager.GetTouchPanelAreaPush(MonitorId, ButtonId);   // → false
...
bool flag2 = false;
if (HeadJudged) { if (flag) flag2 = true; else { ... } }                  // → 一直 false
...
LastHoldState = flag2;   // 一直 false
HoldOn(LastHoldState);   // 于是每帧都调 HoldOn(false)
```

`HoldOn(bool)` 正是「换贴图」的那个函数：

| 类型 | `HoldOn(true)` 做了什么 |
|---|---|
| `HoldNote` / `BreakHoldNote` | `SpriteRender.sprite = NormalHoldOn/EachHoldOn`（否则 `HoldOff`）+ `HoldBodyOnFlg = true` |
| `TouchHoldC` | `HoldGaugeObject.sprite = TouchHoldGuide`（否则 `TouchHoldGuideOff`） |

`HoldBodyOnFlg` 又驱动 `GetNoteYPosition()` 里的 `EffectSprite` 呼吸发光。所以全程是「松手」贴图，只有最后蹦一个判定。

#### 修法

挂在 `HoldNote` / `BreakHoldNote` / `TouchHoldC` 的 **`NoteCheck()` Postfix** —— 必须是 Postfix，因为游戏自己在 `NoteCheck()` 里每帧都会调 `HoldOn(LastHoldState)`，我们得在它之后再覆盖一次。

Postfix 里按录制的按住区间 `[HeadMsec, HoldEndMsec]`：

- **区间内**：调 `HoldOn(true)`；进入区间的那一帧额外调 `TouchEffect.InitializeHold(GetJudgeHeadResult())` 把保持特效也拉起来
- **离开区间**（松手边沿）：调 `TouchEffect.StopHoldPlay()`，贴图交回游戏自己的 `HoldOn(false)`

只对「我们注入过头判」的 hold 生效（`_injected`），所以录制里本来就 miss 掉的 hold 不会凭空显示成按住。

调用方式：`HoldOn(bool)` 是 `protected`，用扩展后的 `Reflect.FindMethod(type, name, argTypes)` 反射调用（结果按类型缓存，参数数组复用，避免每帧装箱分配）。

#### 附带变更

- `Reflect.FindMethod` 增加带参数重载（缓存键含参数个数）
- 新增日志 `回放 hold 保持态: 补出 N 条`
- 版本 `0.5.1` → `0.6.0`；补丁数不变（36），反射查找 27 → 33

#### Stage 5 剩余项

- 玩家在回放时**抢划 slide** 覆盖录制判定的能力（见 v0.5.0 的权衡一节）
- 「取并集」仍是「玩家先判则玩家优先」

---
