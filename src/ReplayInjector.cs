// ============================================================================
//  ReplayInjector.cs —— 回放注入层
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

        // ------------------------------------------------------------ 内部状态
        private static readonly HashSet<int> _handled = new HashSet<int>();
        private static readonly HashSet<int> _injected = new HashSet<int>();
        private static readonly HashSet<int> _slideCounted = new HashSet<int>();
        // 保持态视觉：当前正被我们补成「按住」的 hold
        private static readonly HashSet<int> _holdGhostOn = new HashSet<int>();
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
        //  Hold 保持态视觉（v0.6.0）
        //
        //  判定与分数本来是对的，但回放时玩家并没有真的按住，于是
        //  HoldNote.NoteCheck() 里那个 flag 一直是 false → flag2 一直 false →
        //  游戏每帧都调 HoldOn(false)，hold 从头到尾都是「松手」贴图，
        //  只有最后蹦一个判定。
        //
        //  做法：挂 NoteCheck() 的 **Postfix**（在游戏自己那次 HoldOn 之后），
        //  按录制的按住区间 [HeadMsec, HoldEndMsec] 补一次 HoldOn(true)，
        //  并把保持特效一起拉起 / 在松手边沿停掉。
        //  HoldOn(bool) 对三种 hold 都是「换贴图」的那个函数：
        //    HoldNote / BreakHoldNote  → NoteObj 贴图 + HoldBodyOnFlg（发光）
        //    TouchHoldC                → HoldGaugeObject 贴图
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

        private static void SetHoldEffect(Monitor.NoteBase nb, bool start, NoteJudge.ETiming headResult)
        {
            try
            {
                if (F_JudgeEffect == null) return;
                Monitor.TouchEffect fx = F_JudgeEffect.GetValue(nb) as Monitor.TouchEffect;
                if (fx == null) return;
                if (start) fx.InitializeHold(headResult);
                else fx.StopHoldPlay();
            }
            catch (System.Exception) { }
        }

        /// <summary>
        /// 补丁点：HoldNote / BreakHoldNote / TouchHoldC 的 NoteCheck() Postfix。
        /// 必须在 Postfix —— 游戏自己在 NoteCheck 里每帧都会调 HoldOn(LastHoldState)，
        /// 我们在它之后再覆盖一次才有效。
        /// </summary>
        public static void ForceHoldBody(Monitor.NoteBase nb, NoteJudge.ETiming headResult)
        {
            if (nb == null) return;
            if (!GhostState.Replaying) return;

            int idx = nb.GetNoteIndex();
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
                // 松手边沿：停掉保持特效（贴图交回游戏自己的 HoldOn(false)）
                if (_holdGhostOn.Remove(idx)) SetHoldEffect(nb, false, headResult);
                return;
            }

            // 保持中：每一帧都补一次，压过游戏自己的 HoldOn(false)
            CallHoldOn(nb, true);
            if (_holdGhostOn.Add(idx))
            {
                SetHoldEffect(nb, true, headResult);
                HoldBodyFaked++;
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
