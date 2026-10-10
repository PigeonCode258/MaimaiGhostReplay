// ============================================================================
//  ReplayInjector.cs —— 回放注入层
//
//  v0.7.1 修复：hold / touchhold 的「按住时持续显示的那个粒子」在回放里看不到。
//    两个独立问题叠加：
//
//    (1) 录制侧 —— hold 的按住区间缺右端点。
//      游戏只在尾判窗口之外才调 HoldOn（HoldNote.cs:169-172 的
//      IsNoteCheckTimeHoldTailIgnoreJudgeWait 让 :263 那个 if 不再成立，
//      于是 :312 的 HoldOn(LastHoldState) 也不再执行）。
//      玩家「按到 note 结束」时我们看不到 true->false 边沿 → HasHoldEnd 恒 false
//      → 区间 [HeadMsec, HoldEndMsec] 没有右端点 → 松手边沿永不触发。
//      修法见 Recorder.OnNoteJudged()。
//
//    (2) 回放侧 —— 持续粒子挂在会被抢占的**共享**实例上。
//      v0.6.0 用 F_JudgeEffect（= 游戏那根轨道的共享 TouchEffect）拉持续粒子，
//      而 TouchEffect 是单槽状态机（PlayParticles 播一个槽会关掉其它所有槽，
//      Monitor/TouchEffect.cs:101-104；Execute 在槽停时关掉整个 GameObject，:250-256），
//      这个实例同时被这些输入驱动路径写：
//        GameCtrl.cs:604-609   玩家每按一次键 → Initialize()（槽 0）
//        GameCtrl.cs:619-622   玩家每次碰 C 区 → Initialize()（槽 0）
//        *HoldNote.NoteCheck():299-306  按住/松手的边沿
//      更糟的是 tap 与 hold 共用同一个实例
//      （GameCtrl.cs:1480/1507/1534/1561/1588/1615 全是 _touchEffectObjectList[lane]），
//      而 TouchHoldC 更极端 —— GameCtrl.cs:1945 把所有 C 区 touchhold 都指向
//      _touchEffectCObjectList[0]，全场只有 1 个实例。
//      瞬时爆开只要有一帧就够了，持续粒子却要求整个按住期间一直不被抢走，
//      而 v0.6.0 只在进入区间时拉起过一次 → 被打断就再也回不来。
//      修法：改用**专属实例** _ghostHoldFx（与 v0.7.0 的瞬时爆开实例分开）+
//      引用计数（多条共存互不干扰）+ 窗口内自愈重放。
//
//  v0.7.0 新增：打击特效。
//    症状：回放判定命中的 note 看不到打击特效（判定文字是正常的）。
//    根因：判定文字与打击粒子是 EndNote() 里紧挨着的两行
//      Monitor/NoteBase.cs:348  JudgeGradeObject.Initialize(...);
//      Monitor/NoteBase.cs:355  JudgeEffectObject.Initialize(...);
//    文字出来了说明 EndNote() 确实跑了、粒子也确实被请求了 —— 但粒子被吃掉了。
//    因为每根轨道只有 **一个** TouchEffect 实例，被两方共用：
//      GameCtrl.cs:1480       RegistNote 把它交给 note（SetJudgeObject）
//      GameCtrl.cs:604-609    玩家每按一次键就调同一个实例的 Initialize()
//    而 TouchEffect 内部是单状态机：PlayParticles() 播一个槽会关掉其它所有槽
//    （Monitor/TouchEffect.cs:101-104），Execute() 在粒子停时把整个 GameObject
//    关掉（:250-256）。玩家在第二首里一直操作，按键那一发和回放判定那一发
//    互相抢占，判定粒子基本看不到。
//    做法：不再和游戏/玩家抢那个共享实例，而是每根轨道克隆一个「只有回放会用」
//    的 TouchEffect，挂在同一根轨道的 launcher 下（位置/朝向天然一致），
//    再从 InjectNoteBase() 里按 note 家族调用游戏自己的入口。
//
//  v0.6.0 新增：hold 的「保持态视觉」。
//    v0.5.1 修好了判定与分数，但回放时玩家并没真的按住，游戏每帧调 HoldOn(false)，
//    于是 hold 全程是「松手」贴图，只有最后蹦一个判定。
//    现在在 NoteCheck() 的 Postfix 里按录制的按住区间补 HoldOn(true)，
//    并同步拉起/停掉保持特效。
//
//  v0.5.1 修复：hold 从来没被回放过。
//    原来 InjectHold 等到 r.JudgeMsec（尾判时刻）才动手，但那时 HeadJudged 必然已为 true
//    —— 要么玩家按过，要么 HoldNote.NoteCheck() 里
//        if (JudgeToolate() && !HeadJudged) { HeadJudged = true; }
//    在头判窗口过后把它置真了。于是「玩家已判 → 玩家优先」的判断必然命中，
//    InjectHold 一条都没注入过，_injected 永远为空，OverrideHoldTotal 也从不生效，
//    所有 hold 都按游戏拿到的空身体重算 → 全变 miss。
//    现在改为在**录制到的头判时刻**（r.HeadMsec）注入，并区分
//    「玩家真按出的判定」与「超时路径写进去的 TooLate」。
//
//  v0.5.0 新增：slide 的滑动动画。
//    v0.4.0 及以前是直接往 JudgeResult 写值。这会让 SlideRoot.NoteCheck() 越过
//    那道早退闸门（:710 `if (GetJudgeResult() == End && !JudgeToolate()) return;`），
//    直接进收尾分支 —— EndFlag=true、星星 SetActive(false)、整个 slide 停摆，
//    而且 _hitIndex 始终是 0，箭头也不会随星星经过而熄灭。
//    注意 lastWaitTime 在 Initialize() 里就已按「末格推进距离占比 × 滑动时长」设好
//    并逐帧递减，早就 ≤ 0，全靠 :710 拦着才不误触发。
//
//    现在改为：借用游戏自带 autoplay 的那条「按时间推进」分支
//      （SlideRoot.NoteCheck() 里的 `else if (GameManager.AutoJudge() != TooLate)`），
//    它会按 currentMsec 在 StarLaunchMsec→StarArriveMsec 之间推进 _hitIndex、
//    逐个熄灭星星经过的箭头，推进到底后自然调用 Judge()。
//    做法：
//      1) Prefix GameManager.IsAutoPlay()，只在「回放中的 slide 的 NoteCheck」期间返回 true
//      2) Prefix/Postfix SlideRoot.NoteCheck() / SlideFan.NoteCheck() 维护这个作用域
//      3) Prefix SlideRoot.Judge() 把 AutoJudge() 的结果换成录制的判定
//
//  v0.3.0 新增：判定音。
//    NoteBase.EndNote() 不播判定音，判定音只在 Judge() / JudgeToolate() /
//    SetAutoPlayJudge() 三处发出。回放直接写 JudgeResult 会绕过它们，
//    所以注入完补调 note 自己的 PlayJudgeSe() / PlayJudgeHeadSe()。
//    （slide 的判定音由收尾分支 / 自动推进分支自己发，不重复补。）
//
//  v0.2.0 的重大 bug（已修）：曾经用「note 实例 -> 记录」的字典做缓存。
//  但 GameCtrl 每种 note 只实例化 64 个放进对象池（GameCtrl.cs:1177-1256），
//  整首歌几百个 note 轮流复用这 64 个实例，于是实例缓存一旦绑定就再也不更新，
//  后续复用该实例的 note 会拿到第一条记录的判定时刻（早已过去）→ 生成即判定
//  → EndNote() 里 SetActive(false) → note 一出现就消失。
//  现在改为每次都直接读 NoteIndex：NoteBase 用 public 的 GetNoteIndex()；
//  SlideRoot 没有该方法，反射读 NoteIndex 字段（同屏 slide 只有个位数，装箱可忽略）。
// ============================================================================
using System.Collections.Generic;
using System.Reflection;
using Manager;

namespace MaimaiGhostReplay
{
    public static class ReplayInjector
    {
        // ------------------------------------------------------------ 字段句柄
        public static readonly FieldInfo F_NoteIndex = Reflect.FindField(typeof(Monitor.NoteBase), "NoteIndex");
        public static readonly FieldInfo F_NoteJudge = Reflect.FindField(typeof(Monitor.NoteBase), "JudgeResult");
        public static readonly FieldInfo F_NoteDiff = Reflect.FindField(typeof(Monitor.NoteBase), "JudgeTimingDiffMsec");

        public static readonly FieldInfo F_SlideIndex = Reflect.FindField(typeof(Monitor.SlideRoot), "NoteIndex");
        public static readonly FieldInfo F_SlideJudge = Reflect.FindField(typeof(Monitor.SlideRoot), "JudgeResult");
        public static readonly FieldInfo F_SlideDiff = Reflect.FindField(typeof(Monitor.SlideRoot), "JudgeTimingDiffMsec");

        public static readonly FieldInfo F_HoldHeadJudged =
            Reflect.FindField(typeof(Monitor.HoldNote), "HeadJudged");
        public static readonly FieldInfo F_BreakHoldHeadJudged =
            Reflect.FindField(typeof(Monitor.BreakHoldNote), "HeadJudged");
        public static readonly FieldInfo F_TouchHoldHeadJudged =
            Reflect.FindField(typeof(Monitor.TouchHoldC), "HeadJudged");

        // v0.3.0：头判结果也要还原，否则头判音会用错音色
        public static readonly FieldInfo F_HoldHeadResult =
            Reflect.FindField(typeof(Monitor.HoldNote), "JudgeHeadResult");
        public static readonly FieldInfo F_BreakHoldHeadResult =
            Reflect.FindField(typeof(Monitor.BreakHoldNote), "JudgeHeadResult");
        public static readonly FieldInfo F_TouchHoldHeadResult =
            Reflect.FindField(typeof(Monitor.TouchHoldC), "JudgeHeadResult");

        // 保持态视觉要用的：NoteBase 上的 TouchEffect 引用
        public static readonly FieldInfo F_JudgeEffect =
            Reflect.FindField(typeof(Monitor.NoteBase), "JudgeEffectObject");

        // ------------------------------------------------------------ 统计（日志用）
        public static int JudgeSeCount;
        public static int HoldHeadSeCount;
        public static int SlideAutoCount;     // 走自动推进（有滑动动画）的 slide 数
        public static int SlideManualCount;   // 录制里是 miss、交给游戏超时路径的 slide 数
        public static int HoldInjected;       // 注入头判的 hold 数
        public static int HoldPlayerTook;     // 玩家自己按出真实判定、让给玩家的 hold 数
        public static int HoldNoHead;         // 录制里就没有头判（原本也没按上）的 hold 数
        public static int HoldBodyFaked;      // 补出「按住」视觉的 hold 数

        // v0.7.0：补出的打击特效次数（按 note 家族分列，日志用）
        public static int HitFxCount;
        public static int HitFxTap;           // TapNote / StarNote / TouchNoteC 系
        public static int HitFxEx;            // Ex note
        public static int HitFxBreak;         // BreakNote / BreakStarNote
        public static int HitFxTouch;         // TouchNoteB 及其子类

        // v0.7.1：hold 的「按住持续粒子」统计（日志用）
        public static int HoldFxStart;        // 拉起持续粒子的次数
        public static int HoldFxStop;         // 真正停掉的次数（引用计数归零）
        public static int HoldFxReplay;       // 窗口内自愈重放的次数
        public static int HoldFxTail;         // 结算时补的松手 / 破防爆开次数

        // ------------------------------------------------------------ 内部状态
        private static readonly HashSet<int> _handled = new HashSet<int>();
        private static readonly HashSet<int> _injected = new HashSet<int>();
        private static readonly HashSet<int> _slideCounted = new HashSet<int>();
        // 保持态视觉：当前正被我们补成「按住」的 hold
        private static readonly HashSet<int> _holdGhostOn = new HashSet<int>();
        // v0.7.1：已经走到结算、处理过收尾特效的 hold
        private static readonly HashSet<int> _holdFinished = new HashSet<int>();
        // v0.7.0 打击特效：每根轨道一个「幽灵专用」实例，key = 游戏那个共享 TouchEffect。
        // 刻意不随 BeginSong() 清理 —— 游戏的 TouchEffect 是开局建一次、跨局复用的，
        // 我们的兄弟节点跟着复用，避免每局重建对象、也避免引用失效。
        private static readonly Dictionary<Monitor.TouchEffect, Monitor.TouchEffect> _ghostFx =
            new Dictionary<Monitor.TouchEffect, Monitor.TouchEffect>();
        // v0.7.1：hold 的「持续粒子」单独一套实例。
        // 持续粒子生命周期远长于瞬时爆开，共用会被顶掉，所以必须分开。
        private static readonly Dictionary<Monitor.TouchEffect, Monitor.TouchEffect> _ghostHoldFx =
            new Dictionary<Monitor.TouchEffect, Monitor.TouchEffect>();
        // 同一个实例上可能同时有多条 hold 按住（TouchHoldC 全场共用 1 个实例），
        // 引用计数保证「最后一条松手才停」。每局清空。
        private static readonly Dictionary<Monitor.TouchEffect, int> _holdRefCount =
            new Dictionary<Monitor.TouchEffect, int>();
        // 自愈重放的节流（避免粒子资源异常时逐帧重启）
        private static float _lastHoldAssertMsec;
        // TouchEffect 当前正在播哪个粒子槽（0=Touch 1=Tap 2=HoldOn 3=HoldOff
        // 4=ExTap 5=Break 6=Center）。private，取不到就退回 public 的 activeSelf 判断。
        private static readonly FieldInfo F_FxPlayingIndex =
            Reflect.FindField(typeof(Monitor.TouchEffect), "_playingIndex");
        private static readonly System.Type[] _argBool = new System.Type[] { typeof(bool) };
        private static readonly object[] _invokeArgs = new object[1];

        // slide 回放上下文：只在「回放中的 slide 的 NoteCheck()」期间非空
        private static Monitor.SlideRoot _ctxSlide;
        private static NoteRecord _ctxRecord;

        /// <summary>每局开始清空标记与统计。</summary>
        public static void BeginSong()
        {
            _handled.Clear();
            _injected.Clear();
            _slideCounted.Clear();
            _ctxSlide = null;
            _ctxRecord = null;
            JudgeSeCount = 0;
            HoldHeadSeCount = 0;
            SlideAutoCount = 0;
            SlideManualCount = 0;
            HoldInjected = 0;
            HoldPlayerTook = 0;
            HoldNoHead = 0;
            HoldBodyFaked = 0;
            _holdGhostOn.Clear();
            HitFxCount = 0;
            HitFxTap = 0;
            HitFxEx = 0;
            HitFxBreak = 0;
            HitFxTouch = 0;
            HoldFxStart = 0;
            HoldFxStop = 0;
            HoldFxReplay = 0;
            HoldFxTail = 0;
            _holdFinished.Clear();
            _lastHoldAssertMsec = 0f;

            // v0.7.1：把上一局可能残留的 hold 持续粒子收干净，避免卡住不放
            foreach (Monitor.TouchEffect fx in _ghostHoldFx.Values)
            {
                if (fx == null) continue;
                try { fx.StopAll(); }
                catch (System.Exception) { }
            }
            _holdRefCount.Clear();
        }

        /// <summary>读取 slide 的 NoteIndex。</summary>
        public static int SlideNoteIndex(Monitor.SlideRoot sr)
        {
            if (sr == null || F_SlideIndex == null) return -1;
            try { return (int)F_SlideIndex.GetValue(sr); }
            catch (System.Exception) { return -1; }
        }

        /// <summary>取录制记录；同时做 JudgeMsec 合法性防御。</summary>
        private static NoteRecord Lookup(int noteIndex)
        {
            if (noteIndex < 0) return null;
            SongRecord song = GhostState.Record;
            if (song == null) return null;
            NoteRecord r = song.Get(noteIndex);
            if (r == null) return null;
            // 防御：判定时刻非法的记录一律不注入（v0.1.0 就是被结算补判污染过）
            if (r.JudgeMsec <= 0f) return null;
            return r;
        }

        /// <summary>
        /// 只查记录是否存在，不做 JudgeMsec 防御 —— hold 依赖的是 HeadMsec。
        /// </summary>
        private static NoteRecord LookupRaw(int noteIndex)
        {
            if (noteIndex < 0) return null;
            SongRecord song = GhostState.Record;
            if (song == null) return null;
            return song.Get(noteIndex);
        }

        // ================================================================
        //  判定音（v0.3.0）
        //  PlayJudgeSe / PlayJudgeHeadSe 都是 protected，且各子类重写不同，
        //  所以按具体类型反射取 MethodInfo（Reflect.FindMethod 内部有缓存），
        //  拿到的是最派生类上的那一个。
        // ================================================================
        private static void PlaySe(object note, string methodName)
        {
            if (note == null) return;
            try
            {
                MethodInfo mi = Reflect.FindMethod(note.GetType(), methodName);
                if (mi == null) return;
                mi.Invoke(note, null);
            }
            catch (System.Exception e)
            {
                GhostReplayMod.LogWarn("播放判定音失败(" + methodName + "): " + e.Message);
            }
        }

        // ================================================================
        //  打击特效（v0.7.0）
        //
        //  为什么不复用游戏那个 JudgeEffectObject：每根轨道只有 1 个 TouchEffect
        //  实例，note 判定和玩家按键共用它，而它内部是单状态机（同时只有一个粒子槽
        //  在播，播新的会关掉旧的）。玩家在第二首里一直操作，两个来源互相抢占，
        //  回放判定那一发就被吃掉了。自带一个实例彻底绕开这个争用。
        //
        //  位置与朝向天然正确：父节点就是那根轨道 launcher 的 "NoteEnd"，
        //  和游戏自己的特效同级同位（GameCtrl.cs:996-999 就是这么建的）。
        // ================================================================

        /// <summary>
        /// 克隆一个「只有回放会用」的 TouchEffect，挂在同一根轨道的 launcher 下。
        /// 父节点取游戏那个实例的 transform.parent，所以位置与朝向天然正确
        /// （GameCtrl.cs:996-999 建游戏自己的特效时用的就是这个父节点）。
        /// </summary>
        private static Monitor.TouchEffect CreateGhostFx(Monitor.TouchEffect gameFx, int monitorId)
        {
            try
            {
                // 资源容器还没初始化时直接放弃 —— 只影响特效，不影响判定与分数
                if (GameNotePrefabContainer.TouchEffect == null) return null;

                UnityEngine.Transform parent = gameFx.transform.parent;
                Monitor.TouchEffect fx = UnityEngine.Object.Instantiate<Monitor.TouchEffect>(
                    GameNotePrefabContainer.TouchEffect, parent);
                if (fx == null) return null;

                // 与游戏同一套粒子资源、同一 TapDesign / HoldDesign
                fx.SetUpParticle(monitorId);
                // 关键：SetUpParticle 会把 7 个粒子槽全播一遍，必须收掉
                //（GameCtrl.cs:412 建完 launcher 后也是这么收尾的）
                fx.StopAll();
                return fx;
            }
            catch (System.Exception e)
            {
                GhostReplayMod.LogWarn("创建回放特效实例失败: " + e.Message);
                return null;
            }
        }

        /// <summary>取（必要时创建）某根轨道的「瞬时爆开」幽灵实例。失败返回 null。</summary>
        private static Monitor.TouchEffect GhostFx(Monitor.TouchEffect gameFx, int monitorId)
        {
            Monitor.TouchEffect fx;
            if (_ghostFx.TryGetValue(gameFx, out fx) && fx != null) return fx;

            fx = CreateGhostFx(gameFx, monitorId);
            if (fx == null) return null;
            _ghostFx[gameFx] = fx;
            return fx;
        }

        /// <summary>
        /// 按 note 家族补一次打击特效，入口与游戏 EndNote() 里的调用一一对应。
        /// 游戏对 miss 不出特效，这里也照做（TooFast / TooLate 经 ConvertJudge 都归 Miss）。
        /// </summary>
        private static void PlayHitEffect(Monitor.NoteBase nb)
        {
            if (nb == null || F_JudgeEffect == null) return;

            NoteJudge.ETiming timing = nb.GetJudgeResult();
            if (NoteJudge.ConvertJudge(timing) == NoteJudge.JudgeBox.Miss) return;

            Monitor.TouchEffect gameFx;
            try { gameFx = F_JudgeEffect.GetValue(nb) as Monitor.TouchEffect; }
            catch (System.Exception) { return; }
            if (gameFx == null) return;

            Monitor.TouchEffect fx = GhostFx(gameFx, nb.MonitorId);
            if (fx == null) return;

            // 顺序敏感：BreakNote.EndNote() 只调 InitializeBreak、TouchNoteB.EndNote()
            // 只调 InitializeCenter，两者都不看 ExNote，所以必须先判它们。
            if (nb is Monitor.BreakNote) { fx.InitializeBreak(timing); HitFxBreak++; }
            else if (nb is Monitor.TouchNoteB) { fx.InitializeCenter(timing); HitFxTouch++; }
            else if (nb.ExNote) { fx.InitializeEx(timing); HitFxEx++; }
            else { fx.Initialize(timing); HitFxTap++; }
            HitFxCount++;
        }

        // ================================================================
        //  NoteBase 家族：Tap / Star / Break / BreakStar / Touch / TouchNoteC
        // ================================================================
        public static void InjectNoteBase(Monitor.NoteBase nb)
        {
            if (nb == null) return;
            if (!GhostState.Replaying) return;

            NoteRecord r = Lookup(nb.GetNoteIndex());
            if (r == null) return;
            if (NotesManager.GetCurrentMsec() < r.JudgeMsec) return;

            // 玩家已经打过了 → 玩家优先（并集语义），判定音也已经由 Judge() 播过
            if (nb.GetJudgeResult() != NoteJudge.ETiming.End) return;

            F_NoteJudge.SetValue(nb, (NoteJudge.ETiming)r.Timing);
            F_NoteDiff.SetValue(nb, r.DiffMsec);

            // 补判定音（EndNote 不会播）
            PlaySe(nb, "PlayJudgeSe");
            JudgeSeCount++;

            // 补打击特效（游戏那一发会和玩家的按键抢同一个共享实例，见上方说明）
            PlayHitEffect(nb);
        }

        // ================================================================
        //  Hold / BreakHold / TouchHold
        //  hold 的最终 JudgeResult 是 EndNote 里由 JudgeTotalResult() 算出来的，
        //  直接写 JudgeResult 会被覆盖，所以：
        //    1) 到时刻把 JudgeHeadResult / HeadJudged 补上，让 note 能走到尾判 EndNote
        //    2) 在 JudgeTotalResult() 的 Prefix 里用录制终态覆盖并跳过原逻辑
        // ================================================================
        public static void InjectHold(Monitor.NoteBase nb, FieldInfo headJudgedField, FieldInfo headResultField)
        {
            if (nb == null || headJudgedField == null) return;
            if (!GhostState.Replaying) return;

            int idx = nb.GetNoteIndex();
            if (_handled.Contains(idx)) return;

            NoteRecord r = LookupRaw(idx);
            if (r == null) return;

            // 录制里没有头判（玩家原本就没按上）→ 不注入，让游戏自己的超时路径生效
            if (!r.HasHead || r.HeadMsec <= 0f)
            {
                if (_handled.Add(idx)) HoldNoHead++;
                return;
            }

            // 关键：在**录制到的头判时刻**注入头判，而不是像原来那样等到尾判时刻。
            // 否则 HoldNote.NoteCheck() 里的
            //     if (JudgeToolate() && !HeadJudged) { HeadJudged = true; }
            // 会在头判窗口过去后先把 JudgeHeadResult 写成 TooLate，
            // 于是 1) 原来的 already 判断必然为真、直接跳过注入；
            //      2) 就算注入了，JudgeHoldTotal 也会按 TooLate 算成 miss。
            if (NotesManager.GetCurrentMsec() < r.HeadMsec) return;

            NoteJudge.ETiming headNow;
            try { headNow = (NoteJudge.ETiming)headResultField.GetValue(nb); }
            catch (System.Exception) { return; }

            // 玩家在录制时刻之前已经自己按出**真实**判定 → 玩家优先。
            // TooLate 不算：那多半是超时路径写进去的，原局本来也是 miss。
            if (headNow != NoteJudge.ETiming.End && headNow != NoteJudge.ETiming.TooLate)
            {
                if (_handled.Add(idx)) HoldPlayerTook++;
                return;
            }

            // 注入头判 + 补头判音。
            // 尾判由 EndNote() -> JudgeTotalResult() 的 Prefix 覆盖成录制终态。
            try { headResultField.SetValue(nb, (NoteJudge.ETiming)r.HeadTiming); }
            catch (System.Exception) { }
            headJudgedField.SetValue(nb, true);
            _injected.Add(idx);
            if (_handled.Add(idx)) HoldInjected++;

            PlaySe(nb, "PlayJudgeHeadSe");
            HoldHeadSeCount++;
        }

        /// <summary>
        /// 补丁点：*HoldNote.JudgeTotalResult() 的 Prefix。
        /// 返回 false = 跳过原方法（我们自己写了 JudgeResult）。
        /// </summary>
        public static bool OverrideHoldTotal(object instance, int noteIndex, FieldInfo judgeResultField)
        {
            if (!GhostState.Replaying) return true;
            if (judgeResultField == null) return true;
            if (!_injected.Contains(noteIndex)) return true;   // 玩家自己打的 → 让游戏算

            SongRecord song = GhostState.Record;
            if (song == null) return true;
            NoteRecord r = song.Get(noteIndex);
            if (r == null || r.JudgeMsec <= 0f) return true;

            judgeResultField.SetValue(instance, (NoteJudge.ETiming)r.Timing);
            return false;
        }

        // ================================================================
        //  Hold 保持态视觉（v0.6.0）+ 按住持续粒子（v0.7.1）
        //
        //  v0.6.0：判定与分数本来是对的，但回放时玩家并没有真的按住，于是
        //  HoldNote.NoteCheck() 里那个 flag 一直是 false → flag2 一直 false →
        //  游戏每帧都调 HoldOn(false)，hold 从头到尾都是「松手」贴图，
        //  只有最后蹦一个判定。
        //  做法：挂 NoteCheck() 的 **Postfix**（在游戏自己那次 HoldOn 之后），
        //  按录制的按住区间 [HeadMsec, HoldEndMsec] 补一次 HoldOn(true)。
        //  HoldOn(bool) 对三种 hold 都是「换贴图」的那个函数：
        //    HoldNote / BreakHoldNote  → NoteObj 贴图 + HoldBodyOnFlg（发光）
        //    TouchHoldC                → HoldGaugeObject 贴图
        //
        //  v0.7.1：同一个 Postfix 里再把「按住的持续粒子」也管起来，
        //  但改写到专属实例上（原因见下方那一段和文件头部）。
        //  另外 Recorder 侧补了 HoldEndMsec —— 否则这里的区间没有右端点。
        // ================================================================
        private static void CallHoldOn(Monitor.NoteBase nb, bool on)
        {
            try
            {
                MethodInfo mi = Reflect.FindMethod(nb.GetType(), "HoldOn", _argBool);
                if (mi == null) return;
                _invokeArgs[0] = on;
                mi.Invoke(nb, _invokeArgs);
            }
            catch (System.Exception e)
            {
                GhostReplayMod.LogWarn("HoldOn 调用失败: " + e.Message);
            }
        }

        // ----------------------------------------------------------------
        //  v0.7.1：hold 的「按住持续粒子」改由专属实例驱动
        //
        //  游戏对应代码（三种 hold 同构）：
        //    接到头判   HoldNote.cs:227-228   JudgeHoldHead(); InitializeHold(GetJudgeHeadResult());
        //    按住边沿   HoldNote.cs:299-302   flag2 && !LastHoldState → InitializeHold(...)
        //    松手边沿   HoldNote.cs:303-306   !flag2 && LastHoldState → StopHoldPlay()
        //    结算       HoldNote.cs:493       FinishHold(GetJudgeResult())
        //               BreakHoldNote.cs:478-480  FinishHold(...) 之后再来一次 InitializeBreak(...)
        //               TouchHoldC.cs:479         FinishHold(GetJudgeResult())
        //
        //  回放里这些时刻我们都自己算（录制区间 + note 结算），但**必须写到自己的实例上** ——
        //  写游戏那个共享实例会被玩家的按键/C 区触摸抢掉（见文件头部的说明）。
        // ----------------------------------------------------------------

        /// <summary>取本 note 所在轨道（或 C 区）的「hold 持续粒子」专属实例。失败返回 null。</summary>
        private static Monitor.TouchEffect HoldFxOf(Monitor.NoteBase nb)
        {
            if (nb == null || F_JudgeEffect == null) return null;

            Monitor.TouchEffect gameFx;
            try { gameFx = F_JudgeEffect.GetValue(nb) as Monitor.TouchEffect; }
            catch (System.Exception) { return null; }
            if (gameFx == null) return null;

            Monitor.TouchEffect fx;
            if (_ghostHoldFx.TryGetValue(gameFx, out fx) && fx != null) return fx;

            fx = CreateGhostFx(gameFx, nb.MonitorId);
            if (fx == null) return null;
            _ghostHoldFx[gameFx] = fx;
            return fx;
        }

        /// <summary>专属实例当前是否正在播「保持」槽（2）。取不到私有槽号时退回 activeSelf。</summary>
        private static bool IsHoldSlotPlaying(Monitor.TouchEffect fx)
        {
            if (fx == null) return false;
            if (F_FxPlayingIndex != null)
            {
                try { return (int)F_FxPlayingIndex.GetValue(fx) == 2; }
                catch (System.Exception) { }
            }
            return fx.gameObject.activeSelf;
        }

        /// <summary>拉起持续粒子（引用计数 +1）。多条 hold 共用同一实例时互不干扰。</summary>
        private static void BeginHoldFx(Monitor.TouchEffect fx, NoteJudge.ETiming headResult)
        {
            if (fx == null) return;
            int n;
            _holdRefCount.TryGetValue(fx, out n);
            _holdRefCount[fx] = n + 1;
            try { fx.InitializeHold(headResult); }
            catch (System.Exception) { }
            HoldFxStart++;
        }

        /// <summary>松手（引用计数 -1）。只有减到 0 才真的停 —— 否则会误停其它还在按住的 hold。</summary>
        private static void EndHoldFx(Monitor.TouchEffect fx)
        {
            if (fx == null) return;
            int n;
            if (!_holdRefCount.TryGetValue(fx, out n)) return;
            n--;
            if (n > 0) { _holdRefCount[fx] = n; return; }
            _holdRefCount.Remove(fx);
            try { fx.StopHoldPlay(); }
            catch (System.Exception) { }
            HoldFxStop++;
        }

        /// <summary>窗口内自愈重放（不动引用计数）。</summary>
        private static void ReassertHoldFx(Monitor.TouchEffect fx, NoteJudge.ETiming headResult)
        {
            if (fx == null) return;
            try { fx.InitializeHold(headResult); }
            catch (System.Exception) { }
            HoldFxReplay++;
        }

        /// <summary>
        /// 补丁点：HoldNote / BreakHoldNote / TouchHoldC 的 NoteCheck() Postfix。
        /// 必须在 Postfix —— 游戏自己在 NoteCheck 里每帧都会调 HoldOn(LastHoldState)，
        /// 我们在它之后再覆盖一次才有效。
        ///
        /// 三件事：①按住期间每帧补 HoldOn(true)（v0.6.0 的贴图/发光）
        ///        ②按住期间让持续粒子一直在（v0.7.1）
        ///        ③结算时把游戏 EndNote() 里那两行特效镜像到专属实例上（v0.7.1）
        /// </summary>
        public static void ForceHoldBody(Monitor.NoteBase nb, NoteJudge.ETiming headResult)
        {
            if (nb == null) return;
            if (!GhostState.Replaying) return;

            int idx = nb.GetNoteIndex();
            Monitor.TouchEffect hfx = HoldFxOf(nb);

            // ---- ③ 结算：EndNote() 已在本帧跑完 ----
            // 三种 hold 的 Execute() 都无条件调 NoteCheck()（HoldNote.cs:213 /
            // BreakHoldNote.cs:198 / TouchHoldC.cs:148），而 EndNote() 是在 NoteCheck()
            // 内部被调的（HoldNote.cs:316 等），所以这里必定跑到一次，
            // 且此时 EndFlag 已置位、GetJudgeResult() 已是终态。
            // 这条同时兜住「旧录制数据 HasHoldEnd 仍然缺失」的情况，保证粒子不会卡住。
            if (nb.IsEnd())
            {
                if (_holdFinished.Add(idx))
                {
                    if (_injected.Contains(idx))
                    {
                        NoteJudge.ETiming final = nb.GetJudgeResult();
                        if (hfx != null)
                        {
                            try
                            {
                                hfx.FinishHold(final);
                                // 破防 hold 的 EndNote 里 FinishHold 之后还有一次 InitializeBreak
                                if (nb is Monitor.BreakHoldNote) hfx.InitializeBreak(final);
                            }
                            catch (System.Exception) { }
                        }
                        HoldFxTail++;
                    }
                    // 顺序要紧：先 FinishHold（可能是松手/爆开），再减引用计数。
                    // 若录制尾判是 miss，FinishHold 内部就是 StopHoldPlay，此时计数已是空操作。
                    if (_holdGhostOn.Remove(idx)) EndHoldFx(hfx);
                }
                return;
            }

            bool inside = false;

            // 只有「我们注入过头判」的 hold 才补保持态
            if (_injected.Contains(idx))
            {
                NoteRecord r = LookupRaw(idx);
                if (r != null && r.HasHead && r.HeadMsec > 0f)
                {
                    float now = NotesManager.GetCurrentMsec();
                    inside = now >= r.HeadMsec && (!r.HasHoldEnd || now <= r.HoldEndMsec);
                }
            }

            if (!inside)
            {
                // 松手边沿：停掉持续粒子（贴图交回游戏自己的 HoldOn(false)）
                if (_holdGhostOn.Remove(idx)) EndHoldFx(hfx);
                return;
            }

            // 保持中：每一帧都补一次，压过游戏自己的 HoldOn(false)
            CallHoldOn(nb, true);

            if (_holdGhostOn.Add(idx))
            {
                // ① 进入按住区间：拉起持续粒子
                BeginHoldFx(hfx, headResult);
                HoldBodyFaked++;
            }
            else if (!IsHoldSlotPlaying(hfx))
            {
                // ② 自愈：专属实例上只有我们写，所以「槽 2 不在了」就说明它被我们自己的
                //    其它调用顶掉、或粒子本身已经播完。补一次，保证整个按住期间都有显示。
                //    加节流，避免粒子资源异常时逐帧重启造成闪烁。
                float now2 = NotesManager.GetCurrentMsec();
                if (now2 - _lastHoldAssertMsec >= 100f)
                {
                    ReassertHoldFx(hfx, headResult);
                    _lastHoldAssertMsec = now2;
                }
            }
        }
        // ================================================================
        //  Slide（SlideRoot / SlideFan）—— v0.5.0
        //  不再直接写 JudgeResult，而是让游戏自己的「按时间自动推进」分支跑起来，
        //  这样星星会滑、箭头会随星星经过逐个熄灭、滑到底才判定。
        // ================================================================

        /// <summary>
        /// 是否正处于「回放中的 slide 的 NoteCheck()」。
        /// 供 GameManager.IsAutoPlay() 的 Prefix 使用 —— 作用域严格限定在这一帧的这个 slide。
        /// </summary>
        public static bool InAutomaticSlide
        {
            get { return _ctxSlide != null && _ctxRecord != null; }
        }

        /// <summary>
        /// 补丁点：SlideRoot.NoteCheck() / SlideFan.NoteCheck() 的 Prefix。
        /// 决定这条 slide 是走「自动推进回放」还是「交给游戏的超时路径」。
        /// </summary>
        public static void EnterSlide(Monitor.SlideRoot sr)
        {
            _ctxSlide = null;
            _ctxRecord = null;
            if (sr == null) return;
            if (!GhostState.Replaying) return;

            int idx = SlideNoteIndex(sr);
            NoteRecord r = Lookup(idx);
            if (r == null) return;

            // 录制里是 miss 的 slide 不启用自动推进：
            // 收尾分支会把 TooLate 强行抬成 LateGood
            //   if (GetJudgeResult() == TooLate && _hitIndex >= _hitAreaList.Count - 1)
            //       JudgeResult = LateGood;
            // 自动推进会正好把 _hitIndex 顶到末尾踩中它。让游戏自然超时即可，
            // 星星照样会滑完全程（MoveStarLane 是时间驱动的）。
            if (r.Timing == (int)NoteJudge.ETiming.TooLate)
            {
                if (_slideCounted.Add(idx)) SlideManualCount++;
                return;
            }

            _ctxSlide = sr;
            _ctxRecord = r;
        }

        /// <summary>补丁点：SlideRoot.NoteCheck() / SlideFan.NoteCheck() 的 Postfix。</summary>
        public static void ExitSlide()
        {
            _ctxSlide = null;
            _ctxRecord = null;
        }

        /// <summary>
        /// 补丁点：SlideRoot.Judge() 的 Prefix。
        /// 自动推进分支里 Judge() 会取 GameManager.AutoJudge()（AutoPlay==None 时是
        /// TooFast，再被转成 FastGood），所以要换成录制的判定。
        /// 唯一调用点 `if (_hitIndex >= _hitAreaList.Count) { Judge(); }` 不使用返回值，
        /// 因此返回 true（跳过原方法）是安全的。
        /// </summary>
        public static bool ForceSlideJudge(Monitor.SlideRoot sr)
        {
            if (_ctxSlide == null || _ctxRecord == null) return false;
            if (!object.ReferenceEquals(_ctxSlide, sr)) return false;

            F_SlideJudge.SetValue(sr, (NoteJudge.ETiming)_ctxRecord.Timing);
            F_SlideDiff.SetValue(sr, _ctxRecord.DiffMsec);

            int idx = SlideNoteIndex(sr);
            if (_slideCounted.Add(idx)) SlideAutoCount++;
            return true;
        }
    }
}
