// ============================================================================
//  Patches.cs —— 全部 Harmony 补丁  (v0.2.0)
//
//  用 [HarmonyPatch] 特性写法（补丁方法本身由编译器检查），
//  但不用 PatchAll（它遇到一个失败就整体抛出），改成逐个 Patch 并记录成败，
//  这样某个目标方法不存在时其余补丁仍能生效，日志里也能一眼看出是哪个。
//
//  v0.2.0 变更：
//    - 删除 P_GameScoreList_SetResult（会被结算补判污染）
//    - 新增 6 个 P_*_SetPlayResult（只从实战路径调用）
//    - 新增 P_MusicSelectProcess_OnStart（第二首强制预选同曲同难度）
// ============================================================================
using System;
using System.Reflection;
using DB;
using HarmonyLib;
using Manager;
using Process;
using Process.SubSequence;

namespace MaimaiGhostReplay
{
    public static class Patches
    {
        public static int Applied;
        public static int Failed;

        public static readonly System.Type[] All = new System.Type[]
        {
            // ---- 手势 & 弹窗文案 ----
            typeof(P_MenuSelectSequence_Update),
            typeof(P_WindowMessageIDEnum_GetName),

            // ---- 录制：判定（只从实战 SetPlayResult 走）----
            typeof(P_NoteBase_SetPlayResult),
            typeof(P_BreakNote_SetPlayResult),
            typeof(P_HoldNote_SetPlayResult),
            typeof(P_BreakHoldNote_SetPlayResult),
            typeof(P_TouchHoldC_SetPlayResult),
            typeof(P_SlideRoot_SetPlayResult),

            // ---- 录制：hold/touchhold 头判与松手、slide 划动路径 ----
            typeof(P_HoldNote_JudgeHoldHead),
            typeof(P_BreakHoldNote_JudgeHoldHead),
            typeof(P_TouchHoldC_JudgeHoldHead),
            typeof(P_HoldNote_HoldOn),
            typeof(P_BreakHoldNote_HoldOn),
            typeof(P_TouchHoldC_HoldOn),
            typeof(P_SlideRoot_CheckSlideTouch),

            // ---- 回放注入 ----
            typeof(P_NoteBase_NoteCheck),
            typeof(P_HoldNote_NoteCheck),
            typeof(P_BreakNote_NoteCheck),
            typeof(P_BreakHoldNote_NoteCheck),
            typeof(P_TouchNoteB_NoteCheck),
            typeof(P_TouchHoldC_NoteCheck),
            typeof(P_SlideRoot_NoteCheck),
            typeof(P_SlideFan_NoteCheck),
            typeof(P_HoldNote_JudgeTotalResult),
            typeof(P_BreakHoldNote_JudgeTotalResult),
            typeof(P_TouchHoldC_JudgeTotalResult),

            // ---- 流程 ----
            typeof(P_GameProcess_OnStart),
            typeof(P_GameProcess_OnRelease),
            typeof(P_MusicSelectProcess_OnGameStart),
            typeof(P_MusicSelectProcess_OnStart),
            typeof(P_MusicSelectSequence_OnStartSequence),
            typeof(P_DifficultySelectSequence_OnStartSequence),
            typeof(P_MusicSelectMonitor_OnStartDifficultySelect),
            typeof(P_UtageDifficultySelectSequence_OnStartSequence),
            typeof(P_MusicSelectMonitor_OnStartUtageDifficultySelect),

            // ---- slide 滑动动画（v0.5.0）----
            typeof(P_GameManager_IsAutoPlay),
            typeof(P_SlideRoot_Judge),
        };

        public static void ApplyAll(HarmonyLib.Harmony harmony)
        {
            Applied = 0;
            Failed = 0;
            for (int i = 0; i < All.Length; i++)
            {
                System.Type t = All[i];
                try
                {
                    harmony.CreateClassProcessor(t).Patch();
                    Applied++;
                }
                catch (Exception e)
                {
                    Failed++;
                    GhostReplayMod.LogWarn("补丁失败 " + t.Name + " : " + e.Message);
                }
            }
            GhostReplayMod.Log("补丁应用完成: 成功 " + Applied + " / 失败 " + Failed + " （共 " + All.Length + "）");
        }

        // ================================================================
        //  共用辅助
        // ================================================================
        private static readonly FieldInfo F_Head_Hold =
            Reflect.FindField(typeof(Monitor.HoldNote), "JudgeHeadResult");
        private static readonly FieldInfo F_Head_BreakHold =
            Reflect.FindField(typeof(Monitor.BreakHoldNote), "JudgeHeadResult");
        private static readonly FieldInfo F_Head_TouchHold =
            Reflect.FindField(typeof(Monitor.TouchHoldC), "JudgeHeadResult");

        /// <summary>NoteBase 家族：直接用 public 的 GetNoteIndex()，零反射。</summary>
        private static void RecNote(Monitor.NoteBase nb, NoteScore.EScoreType kind)
        {
            if (GhostState.Phase != GhostPhase.Recording) return;
            try
            {
                float diff = 0f;
                if (ReplayInjector.F_NoteDiff != null)
                    diff = (float)ReplayInjector.F_NoteDiff.GetValue(nb);
                Recorder.OnNoteJudged(nb.GetNoteIndex(), (int)kind, nb.GetJudgeResult(), diff);
            }
            catch (Exception e) { GhostReplayMod.LogWarn("RecNote 异常: " + e.Message); }
        }

        private static void RecSlide(Monitor.SlideRoot sr)
        {
            if (GhostState.Phase != GhostPhase.Recording) return;
            try
            {
                float diff = 0f;
                if (ReplayInjector.F_SlideDiff != null)
                    diff = (float)ReplayInjector.F_SlideDiff.GetValue(sr);
                Recorder.OnNoteJudged(ReplayInjector.SlideNoteIndex(sr),
                    (int)NoteScore.EScoreType.Slide, sr.GetJudgeResult(), diff);
            }
            catch (Exception e) { GhostReplayMod.LogWarn("RecSlide 异常: " + e.Message); }
        }

        private static void RecHead(Monitor.NoteBase nb, FieldInfo headField)
        {
            if (GhostState.Phase != GhostPhase.Recording) return;
            if (headField == null) return;
            try
            {
                NoteJudge.ETiming t = (NoteJudge.ETiming)headField.GetValue(nb);
                Recorder.OnJudgeHoldHead(nb.GetNoteIndex(), t);
            }
            catch (Exception) { }
        }

        private static void RecHoldOn(Monitor.NoteBase nb, bool on)
        {
            try { Recorder.OnHoldOn(nb.GetNoteIndex(), on); }
            catch (Exception) { }
        }

        // ================================================================
        //  1. 手势：MenuSelectSequence.Update()
        //     难度选择后按 4 号键会进入的这个「开始游戏」页面
        // ================================================================
        [HarmonyPatch(typeof(MenuSelectSequence), "Update")]
        internal static class P_MenuSelectSequence_Update
        {
            private static void Postfix(MenuSelectSequence __instance)
            {
                try { GhostFlow.OnMenuUpdate(__instance); }
                catch (Exception e) { GhostReplayMod.LogWarn("OnMenuUpdate 异常: " + e); }
            }
        }

        // ================================================================
        //  2. 弹窗文案：WindowMessageIDEnum.GetName(this WindowMessageID)
        //     只在我们置位期间替换 TrackSkip3Second 的正文，
        //     Title 保持 "ATTENTION!"，所以样式与 track skip 完全一致。
        // ================================================================
        [HarmonyPatch(typeof(WindowMessageIDEnum), "GetName")]
        internal static class P_WindowMessageIDEnum_GetName
        {
            private static bool Prefix(WindowMessageID self, ref string __result)
            {
                if (Notice.Showing && self == WindowMessageID.TrackSkip3Second)
                {
                    __result = Notice.Text;
                    return false;
                }
                return true;
            }
        }

        // ================================================================
        //  3. 录制：6 个 SetPlayResult（只被实战 EndNote / slide 收尾调用）
        //     NoteBase 覆盖 TapNote / StarNote / TouchNoteB / TouchNoteC
        //     BreakNote 覆盖 BreakNote / BreakStarNote
        // ================================================================
        [HarmonyPatch(typeof(Monitor.NoteBase), "SetPlayResult")]
        internal static class P_NoteBase_SetPlayResult
        {
            private static void Postfix(Monitor.NoteBase __instance)
            {
                RecNote(__instance, NoteScore.EScoreType.Tap);
            }
        }

        [HarmonyPatch(typeof(Monitor.BreakNote), "SetPlayResult")]
        internal static class P_BreakNote_SetPlayResult
        {
            private static void Postfix(Monitor.BreakNote __instance)
            {
                RecNote(__instance, NoteScore.EScoreType.Break);
            }
        }

        [HarmonyPatch(typeof(Monitor.HoldNote), "SetPlayResult")]
        internal static class P_HoldNote_SetPlayResult
        {
            private static void Postfix(Monitor.HoldNote __instance)
            {
                RecNote(__instance, NoteScore.EScoreType.Hold);
            }
        }

        [HarmonyPatch(typeof(Monitor.BreakHoldNote), "SetPlayResult")]
        internal static class P_BreakHoldNote_SetPlayResult
        {
            private static void Postfix(Monitor.BreakHoldNote __instance)
            {
                RecNote(__instance, NoteScore.EScoreType.Break);
            }
        }

        [HarmonyPatch(typeof(Monitor.TouchHoldC), "SetPlayResult")]
        internal static class P_TouchHoldC_SetPlayResult
        {
            private static void Postfix(Monitor.TouchHoldC __instance)
            {
                RecNote(__instance, NoteScore.EScoreType.Hold);
            }
        }

        [HarmonyPatch(typeof(Monitor.SlideRoot), "SetPlayResult")]
        internal static class P_SlideRoot_SetPlayResult
        {
            private static void Postfix(Monitor.SlideRoot __instance) { RecSlide(__instance); }
        }

        // ---- hold / touchhold 头判 ----
        [HarmonyPatch(typeof(Monitor.HoldNote), "JudgeHoldHead")]
        internal static class P_HoldNote_JudgeHoldHead
        {
            private static void Postfix(Monitor.HoldNote __instance, bool __result)
            {
                if (__result) RecHead(__instance, F_Head_Hold);
            }
        }

        [HarmonyPatch(typeof(Monitor.BreakHoldNote), "JudgeHoldHead")]
        internal static class P_BreakHoldNote_JudgeHoldHead
        {
            private static void Postfix(Monitor.BreakHoldNote __instance, bool __result)
            {
                if (__result) RecHead(__instance, F_Head_BreakHold);
            }
        }

        [HarmonyPatch(typeof(Monitor.TouchHoldC), "JudgeHoldHead")]
        internal static class P_TouchHoldC_JudgeHoldHead
        {
            private static void Postfix(Monitor.TouchHoldC __instance, bool __result)
            {
                if (__result) RecHead(__instance, F_Head_TouchHold);
            }
        }

        // ---- hold / touchhold 按住与松手 ----
        [HarmonyPatch(typeof(Monitor.HoldNote), "HoldOn")]
        internal static class P_HoldNote_HoldOn
        {
            private static void Postfix(Monitor.HoldNote __instance, bool on) { RecHoldOn(__instance, on); }
        }

        [HarmonyPatch(typeof(Monitor.BreakHoldNote), "HoldOn")]
        internal static class P_BreakHoldNote_HoldOn
        {
            private static void Postfix(Monitor.BreakHoldNote __instance, bool on) { RecHoldOn(__instance, on); }
        }

        [HarmonyPatch(typeof(Monitor.TouchHoldC), "HoldOn")]
        internal static class P_TouchHoldC_HoldOn
        {
            private static void Postfix(Monitor.TouchHoldC __instance, bool on) { RecHoldOn(__instance, on); }
        }

        // ---- slide 划动路径：什么时候划到了第几个箭头 ----
        [HarmonyPatch(typeof(Monitor.SlideRoot), "CheckSlideTouch")]
        internal static class P_SlideRoot_CheckSlideTouch
        {
            private static void Postfix(Monitor.SlideRoot __instance, int index, bool In, bool __result)
            {
                if (!__result) return;
                if (GhostState.Phase != GhostPhase.Recording) return;
                try
                {
                    Recorder.OnSlideTouch(ReplayInjector.SlideNoteIndex(__instance), index, In);
                }
                catch (Exception) { }
            }
        }

        // ================================================================
        //  4. 回放注入：逐 note 的 NoteCheck
        //     NoteCheck 被 5 个子类重写，且 SlideRoot 根本不属于 NoteBase，
        //     所以这里必须逐个声明类型打补丁（Harmony 补丁基类方法拦不住重写）。
        // ================================================================
        [HarmonyPatch(typeof(Monitor.NoteBase), "NoteCheck")]
        internal static class P_NoteBase_NoteCheck
        {
            private static void Postfix(Monitor.NoteBase __instance) { ReplayInjector.InjectNoteBase(__instance); }
        }

        [HarmonyPatch(typeof(Monitor.BreakNote), "NoteCheck")]
        internal static class P_BreakNote_NoteCheck
        {
            private static void Postfix(Monitor.BreakNote __instance) { ReplayInjector.InjectNoteBase(__instance); }
        }

        [HarmonyPatch(typeof(Monitor.TouchNoteB), "NoteCheck")]
        internal static class P_TouchNoteB_NoteCheck
        {
            private static void Postfix(Monitor.TouchNoteB __instance) { ReplayInjector.InjectNoteBase(__instance); }
        }

        [HarmonyPatch(typeof(Monitor.HoldNote), "NoteCheck")]
        internal static class P_HoldNote_NoteCheck
        {
            private static void Postfix(Monitor.HoldNote __instance)
            {
                ReplayInjector.InjectHold(__instance, ReplayInjector.F_HoldHeadJudged, ReplayInjector.F_HoldHeadResult);
                ReplayInjector.ForceHoldBody(__instance, __instance.GetJudgeHeadResult());
            }
        }

        [HarmonyPatch(typeof(Monitor.BreakHoldNote), "NoteCheck")]
        internal static class P_BreakHoldNote_NoteCheck
        {
            private static void Postfix(Monitor.BreakHoldNote __instance)
            {
                ReplayInjector.InjectHold(__instance, ReplayInjector.F_BreakHoldHeadJudged, ReplayInjector.F_BreakHoldHeadResult);
                ReplayInjector.ForceHoldBody(__instance, __instance.GetJudgeHeadResult());
            }
        }

        [HarmonyPatch(typeof(Monitor.TouchHoldC), "NoteCheck")]
        internal static class P_TouchHoldC_NoteCheck
        {
            private static void Postfix(Monitor.TouchHoldC __instance)
            {
                ReplayInjector.InjectHold(__instance, ReplayInjector.F_TouchHoldHeadJudged, ReplayInjector.F_TouchHoldHeadResult);
                ReplayInjector.ForceHoldBody(__instance, __instance.GetJudgeHeadResult());
            }
        }

        [HarmonyPatch(typeof(Monitor.SlideRoot), "NoteCheck")]
        internal static class P_SlideRoot_NoteCheck
        {
            private static void Prefix(Monitor.SlideRoot __instance) { ReplayInjector.EnterSlide(__instance); }
            private static void Postfix(Monitor.SlideRoot __instance) { ReplayInjector.ExitSlide(); }
        }

        [HarmonyPatch(typeof(Monitor.SlideFan), "NoteCheck")]
        internal static class P_SlideFan_NoteCheck
        {
            private static void Prefix(Monitor.SlideFan __instance) { ReplayInjector.EnterSlide(__instance); }
            private static void Postfix(Monitor.SlideFan __instance) { ReplayInjector.ExitSlide(); }
        }

        // ================================================================
        //  5. hold 总判覆盖
        //     hold 的最终 JudgeResult 是 EndNote() 里 JudgeTotalResult() 算出来的，
        //     直接写会被覆盖，所以在这里拦截。
        // ================================================================
        [HarmonyPatch(typeof(Monitor.HoldNote), "JudgeTotalResult")]
        internal static class P_HoldNote_JudgeTotalResult
        {
            private static bool Prefix(Monitor.HoldNote __instance)
            {
                return JudgeTotal(__instance, ReplayInjector.F_HoldHeadJudged);
            }
        }

        [HarmonyPatch(typeof(Monitor.BreakHoldNote), "JudgeTotalResult")]
        internal static class P_BreakHoldNote_JudgeTotalResult
        {
            private static bool Prefix(Monitor.BreakHoldNote __instance)
            {
                return JudgeTotal(__instance, ReplayInjector.F_BreakHoldHeadJudged);
            }
        }

        [HarmonyPatch(typeof(Monitor.TouchHoldC), "JudgeTotalResult")]
        internal static class P_TouchHoldC_JudgeTotalResult
        {
            private static bool Prefix(Monitor.TouchHoldC __instance)
            {
                return JudgeTotal(__instance, ReplayInjector.F_TouchHoldHeadJudged);
            }
        }

        /// <summary>返回 false = 跳过原方法（我们已经写好 JudgeResult）。</summary>
        private static bool JudgeTotal(Monitor.NoteBase nb, FieldInfo headJudgedField)
        {
            try
            {
                if (headJudgedField == null) return true;
                return ReplayInjector.OverrideHoldTotal(
                    nb, nb.GetNoteIndex(), ReplayInjector.F_NoteJudge);
            }
            catch (Exception e)
            {
                GhostReplayMod.LogWarn("JudgeTotalResult 异常: " + e.Message);
                return true;
            }
        }

        // ================================================================
        //  6. 流程推进
        // ================================================================
        [HarmonyPatch(typeof(GameProcess), "OnStart")]
        internal static class P_GameProcess_OnStart
        {
            private static void Postfix(GameProcess __instance)
            {
                try { GhostFlow.OnGameProcessStart(__instance); }
                catch (Exception e) { GhostReplayMod.LogWarn("OnGameProcessStart 异常: " + e); }
            }
        }

        [HarmonyPatch(typeof(GameProcess), "OnRelease")]
        internal static class P_GameProcess_OnRelease
        {
            private static void Postfix(GameProcess __instance)
            {
                try { GhostFlow.OnGameProcessRelease(__instance); }
                catch (Exception e) { GhostReplayMod.LogWarn("OnGameProcessRelease 异常: " + e); }
            }
        }

        [HarmonyPatch(typeof(MusicSelectProcess), "OnGameStart")]
        internal static class P_MusicSelectProcess_OnGameStart
        {
            private static void Postfix(MusicSelectProcess __instance)
            {
                try { GhostFlow.OnMusicSelectGameStart(__instance); }
                catch (Exception e) { GhostReplayMod.LogWarn("OnMusicSelectGameStart 异常: " + e); }
            }
        }

        // 第二首强制预选同曲同难度（v0.2.0 修复）
        [HarmonyPatch(typeof(MusicSelectProcess), "OnStart")]
        internal static class P_MusicSelectProcess_OnStart
        {
            private static void Postfix(MusicSelectProcess __instance)
            {
                try { GhostFlow.OnMusicSelectStart(__instance); }
                catch (Exception e) { GhostReplayMod.LogWarn("OnMusicSelectStart 异常: " + e); }
            }
        }

        // 第二首曲目位置的「消费点复检」（v0.7.2）
        // OnStart 摆完之后，游戏自己那套以 Extend / CategoryIndex 为准的恢复逻辑
        // 还可能把位置带回旧处；游客模式下分类列表又与登录玩家不同，容易错位。
        // 所以在玩家真正看到音乐列表的那一刻（MusicSelectSequence 开始）再确认一次。
        // GhostFlow 侧做了「一次性」保护，玩家主动改选时不会被拽回。
        [HarmonyPatch(typeof(MusicSelectSequence), "OnStartSequence")]
        internal static class P_MusicSelectSequence_OnStartSequence
        {
            private static void Postfix()
            {
                try { GhostFlow.OnMusicSelectSequenceStart(); }
                catch (Exception e) { GhostReplayMod.LogWarn("OnMusicSelectSequenceStart 异常: " + e); }
            }
        }

        // ================================================================
        //  7. 第二首难度还原（v0.3.0 修复）
        //     两层都打在「消费点」上，不再依赖值能一路活着穿过去。
        // ================================================================

        // 第 1 层：难度页读取 DifficultySelectIndex 之前把它纠正过来
        [HarmonyPatch(typeof(DifficultySelectSequence), "OnStartSequence")]
        internal static class P_DifficultySelectSequence_OnStartSequence
        {
            private static void Prefix(DifficultySelectSequence __instance)
            {
                try { GhostFlow.OnDifficultySelectStart(__instance); }
                catch (Exception e) { GhostReplayMod.LogWarn("OnDifficultySelectStart 异常: " + e); }
            }
        }

        // 第 2 层：直接改写真正喂给难度页的那个值（即使前面数据被冲掉也拦得住）
        [HarmonyPatch(typeof(Monitor.MusicSelectMonitor), "OnStartDifficultySelect")]
        internal static class P_MusicSelectMonitor_OnStartDifficultySelect
        {
            private static void Prefix(ref MusicDifficultyID selectDifficulty)
            {
                try { GhostFlow.ForceDifficultyArg(ref selectDifficulty); }
                catch (Exception e) { GhostReplayMod.LogWarn("ForceDifficultyArg 异常: " + e); }
            }
        }

        // ================================================================
        //  8. 宴会场（v0.4.0）
        //     宴会场走的是 UtageDifficultySelectSequence，且难度取自 CurrentDifficulty，
        //     普通难度页的那两层拦不住它，所以单独再来两层。
        // ================================================================

        [HarmonyPatch(typeof(UtageDifficultySelectSequence), "OnStartSequence")]
        internal static class P_UtageDifficultySelectSequence_OnStartSequence
        {
            private static void Prefix(UtageDifficultySelectSequence __instance)
            {
                try { GhostFlow.OnUtageDifficultySelectStart(__instance); }
                catch (Exception e) { GhostReplayMod.LogWarn("OnUtageDifficultySelectStart 异常: " + e); }
            }
        }

        [HarmonyPatch(typeof(Monitor.MusicSelectMonitor), "OnStartUtageDifficultySelect")]
        internal static class P_MusicSelectMonitor_OnStartUtageDifficultySelect
        {
            private static void Prefix(ref MusicDifficultyID selectDifficulty)
            {
                try { GhostFlow.ForceDifficultyArg(ref selectDifficulty); }
                catch (Exception e) { GhostReplayMod.LogWarn("ForceDifficultyArg 异常: " + e); }
            }
        }

        // ================================================================
        //  9. slide 滑动动画（v0.5.0）
        // ================================================================

        // 让游戏自己那条「按时间推进」的分支跑起来（只在回放中的 slide 的 NoteCheck 期间）
        [HarmonyPatch(typeof(GameManager), "IsAutoPlay")]
        internal static class P_GameManager_IsAutoPlay
        {
            private static bool Prefix(ref bool __result)
            {
                if (!ReplayInjector.InAutomaticSlide) return true;
                __result = true;
                return false;
            }
        }

        // 自动推进到底后的判定，换成录制的判定
        [HarmonyPatch(typeof(Monitor.SlideRoot), "Judge")]
        internal static class P_SlideRoot_Judge
        {
            private static bool Prefix(Monitor.SlideRoot __instance)
            {
                return !ReplayInjector.ForceSlideJudge(__instance);
            }
        }
    }
}
