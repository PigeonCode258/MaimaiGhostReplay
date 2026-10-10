// ============================================================================
//  Recorder.cs —— 录制层  (v0.2.0)
//
//  v0.7.1：OnNoteJudged() 里补记 hold 的松手时刻。
//    玩家按到 note 结束时游戏就不再调 HoldOn 了（见 ReplayInjector 头部的说明），
//    于是录不到 true->false 边沿、HasHoldEnd 一直是 false，
//    回放的「按住区间」缺少右端点 —— 松手边沿永不触发。
//
//  v0.1.0 的重大 bug（已修）：曾经挂在 GameScoreList.SetResult 上录制。
//  但 SetResult 不止实战判定会调，结算补判也会调：
//    FinishPlay()              GameScoreList.cs:1788  给未判 note 补 TooLate
//    SetGhostData()            :571                    写入幽灵成绩
//    SetForceAchivementLowAP() :530 / 1Miss() :545     调试用批量填充
//  这些调用的 NotesManager.GetCurrentMsec() 不是判定时刻，录下来就是错的。
//
//  现在改为挂在 6 个 SetPlayResult() 上 —— 它只被实战路径调用：
//    NoteBase.EndNote() / BreakNote.EndNote() / HoldNote.EndNote() /
//    BreakHoldNote.EndNote() / TouchHoldC.EndNote() / SlideRoot.NoteCheck() 收尾
// ============================================================================
using System.Collections.Generic;
using Manager;

namespace MaimaiGhostReplay
{
    public static class Recorder
    {
        /// <summary>noteIndex -> 上一次看到的「保持中」状态，用于识别松手边沿。</summary>
        private static readonly Dictionary<int, bool> _holding = new Dictionary<int, bool>();

        public static void BeginSong()
        {
            _holding.Clear();
        }

        // --------------------------------------------------------------------
        /// <summary>
        /// 补丁点：6 个 *NoteBase/*SlideRoot 的 SetPlayResult()
        /// 只从实战 EndNote() / slide 收尾调用，因此这里拿到的就是真实判定时刻。
        /// </summary>
        public static void OnNoteJudged(int noteIndex, int scoreKind,
            NoteJudge.ETiming timing, float diffMsec)
        {
            if (GhostState.Phase != GhostPhase.Recording) return;
            if (noteIndex < 0) return;

            SongRecord song = GhostState.Recording;
            if (song == null) return;

            float msec = NotesManager.GetCurrentMsec();
            // 防御：判定时刻非法的丢弃（0 = 播放未开始 / 已停止）
            if (msec <= 0f) return;

            NoteRecord r = song.GetOrCreate(noteIndex);
            r.ScoreKind = scoreKind;
            r.Timing = (int)timing;
            r.JudgeMsec = msec;
            r.DiffMsec = diffMsec;

            // v0.7.1：hold 的松手时刻补齐。
            // 玩家「按到 note 结束」（hold 的正确打法）时，游戏进入尾判窗口后
            // 就不再调 HoldOn 了（HoldNote.cs:169-172 的
            // IsNoteCheckTimeHoldTailIgnoreJudgeWait 让 :263 那个 if 不再成立，
            // 于是 :312 的 HoldOn(LastHoldState) 也不再执行），
            // 我们因此看不到 true->false 边沿，HasHoldEnd 永远为 false ——
            // 回放的「按住区间」[HeadMsec, HoldEndMsec] 就没有右端点。
            // 这里在 hold 结算时补记：_holding 只由 hold 的 HoldOn 填充，
            // 所以这个判断天然只对 hold 生效，不需要额外分辨 note 类型。
            bool stillHolding;
            if (_holding.TryGetValue(noteIndex, out stillHolding) && stillHolding)
            {
                _holding[noteIndex] = false;
                r.HasHoldEnd = true;
                r.HoldEndMsec = msec;
            }
        }

        // --------------------------------------------------------------------
        /// <summary>补丁点：*HoldNote.JudgeHoldHead() —— 头判时刻与头判结果。</summary>
        public static void OnJudgeHoldHead(int noteIndex, NoteJudge.ETiming headResult)
        {
            if (GhostState.Phase != GhostPhase.Recording) return;
            if (noteIndex < 0) return;

            SongRecord song = GhostState.Recording;
            if (song == null) return;

            float msec = NotesManager.GetCurrentMsec();
            if (msec <= 0f) return;

            NoteRecord r = song.GetOrCreate(noteIndex);
            r.HasHead = true;
            r.HeadTiming = (int)headResult;
            r.HeadMsec = msec;
        }

        // --------------------------------------------------------------------
        /// <summary>补丁点：*HoldNote.HoldOn(bool on) —— 识别「按住 → 松手」的边沿。</summary>
        public static void OnHoldOn(int noteIndex, bool on)
        {
            if (GhostState.Phase != GhostPhase.Recording) return;
            if (noteIndex < 0) return;

            bool was;
            bool known = _holding.TryGetValue(noteIndex, out was);
            _holding[noteIndex] = on;

            // 只在 true -> false 的边沿记录松手时刻
            if (known && was && !on)
            {
                SongRecord song = GhostState.Recording;
                if (song == null) return;
                float msec = NotesManager.GetCurrentMsec();
                if (msec <= 0f) return;

                NoteRecord r = song.GetOrCreate(noteIndex);
                r.HasHoldEnd = true;
                r.HoldEndMsec = msec;
            }
        }

        // --------------------------------------------------------------------
        /// <summary>补丁点：Monitor.SlideRoot.CheckSlideTouch(int index, bool In)</summary>
        public static void OnSlideTouch(int noteIndex, int index, bool enter)
        {
            if (GhostState.Phase != GhostPhase.Recording) return;
            if (noteIndex < 0) return;

            SongRecord song = GhostState.Recording;
            if (song == null) return;

            float msec = NotesManager.GetCurrentMsec();
            if (msec <= 0f) return;

            NoteRecord r = song.GetOrCreate(noteIndex);
            if (r.SlidePath == null) r.SlidePath = new List<SlideTouchRecord>();

            SlideTouchRecord t;
            t.Index = index;
            t.Enter = enter;
            t.Msec = msec;
            r.SlidePath.Add(t);
        }
    }
}
