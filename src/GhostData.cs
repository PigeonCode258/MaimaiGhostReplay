// ============================================================================
//  GhostData.cs —— 数据结构与全局状态
//  maimai DX (SDEZ) 记录模式 Mod
// ============================================================================
using System.Collections.Generic;

namespace MaimaiGhostReplay
{
    /// <summary>记录模式状态机的阶段。</summary>
    public enum GhostPhase
    {
        Idle = 0,           // 未开启
        Armed = 1,          // 已开启，等第一首进歌
        Recording = 2,      // 正在录制第一首
        AwaitingSecond = 3, // 第一首已录完，等第二首
        Replaying = 4       // 第二首回放中
    }

    /// <summary>slide 的一次划动事件：什么时候划到了第几个箭头（进入/离开）。</summary>
    public struct SlideTouchRecord
    {
        public int Index;   // CheckSlideTouch 的 index，即箭头序号
        public bool Enter;  // true=进入(In) false=离开
        public float Msec;
    }

    /// <summary>单个 note 的录制结果。主键 = NoteIndex。</summary>
    public class NoteRecord
    {
        public int NoteIndex;

        /// <summary>NoteScore.EScoreType 的原始值（Tap/Hold/Slide/Touch/Break）。</summary>
        public int ScoreKind;

        /// <summary>NoteJudge.ETiming 的原始值 —— 权威终态，回放时注入这个。</summary>
        public int Timing;

        /// <summary>判定发生时刻（NotesManager.GetCurrentMsec()）。</summary>
        public float JudgeMsec;

        /// <summary>JudgeTimingDiffMsec，用于判定显示。</summary>
        public float DiffMsec;

        // ---- hold / touchhold 专属：头判与松手（需求 3 要求记录）----
        public bool HasHead;
        public int HeadTiming;      // JudgeHeadResult
        public float HeadMsec;

        public bool HasHoldEnd;
        public float HoldEndMsec;

        // ---- slide 专属：划动路径（需求 3 要求记录）----
        public List<SlideTouchRecord> SlidePath;
    }

    /// <summary>一首曲子的完整录制。</summary>
    public class SongRecord
    {
        public int MusicId = -1;
        public int Difficulty = -1;
        public bool IsLong;
        public int TrackNumber = -1;

        private readonly Dictionary<int, NoteRecord> _notes = new Dictionary<int, NoteRecord>();

        public int NoteCount { get { return _notes.Count; } }

        public NoteRecord Get(int noteIndex)
        {
            NoteRecord r;
            if (_notes.TryGetValue(noteIndex, out r)) return r;
            return null;
        }

        public NoteRecord GetOrCreate(int noteIndex)
        {
            NoteRecord r;
            if (_notes.TryGetValue(noteIndex, out r)) return r;
            r = new NoteRecord();
            r.NoteIndex = noteIndex;
            _notes[noteIndex] = r;
            return r;
        }

        /// <summary>供调试输出使用，返回前 n 个 note 的摘要。</summary>
        public List<NoteRecord> Snapshot()
        {
            List<NoteRecord> list = new List<NoteRecord>(_notes.Values);
            list.Sort(delegate (NoteRecord a, NoteRecord b) { return a.NoteIndex.CompareTo(b.NoteIndex); });
            return list;
        }
    }

    /// <summary>全局状态（单例式静态类，只在主线程访问）。</summary>
    public static class GhostState
    {
        public static GhostPhase Phase = GhostPhase.Idle;

        /// <summary>正在录制的这一首。</summary>
        public static SongRecord Recording;

        /// <summary>已录好、用于回放的那一首。</summary>
        public static SongRecord Record;

        // 手势计时（按下 1/2/7/8 的累计毫秒）
        public static readonly double[] PushTimer = new double[2];
        public static readonly int[] PushPhase = new int[2];

        // 录制基准（用于第二首配对校验）
        public static int BaseMusicId = -1;
        public static int BaseDifficulty = -1;
        public static bool BaseIsLong;
        public static int BaseTrackNumber = -1;

        /// <summary>回放是否生效。</summary>
        public static bool Replaying
        {
            get { return Phase == GhostPhase.Replaying && Record != null; }
        }

        public static void ResetTimer(int player)
        {
            if (player < 0 || player > 1) return;
            PushTimer[player] = 0.0;
            PushPhase[player] = -1;
        }

        public static void ResetAll()
        {
            Phase = GhostPhase.Idle;
            Recording = null;
            Record = null;
            BaseMusicId = -1;
            BaseDifficulty = -1;
            BaseIsLong = false;
            BaseTrackNumber = -1;
            ResetTimer(0);
            ResetTimer(1);
        }

        /// <summary>从回放/录制态回落到空闲。</summary>
        public static void StopMode()
        {
            Phase = GhostPhase.Idle;
            Recording = null;
            Record = null;
            ResetTimer(0);
            ResetTimer(1);
        }
    }

    /// <summary>剩余曲数校验（决策 B：只校验，不预扣）。</summary>
    public static class TrackBudget
    {
        /// <summary>
        /// 剩余可玩曲数（含当前这首）。
        /// GetMaxTrackCount() 已把先前长曲的额外消耗算进去了，
        /// MusicTrackNumber 是「即将游玩的这一首」的序号。
        /// </summary>
        public static int RemainingIncludingCurrent()
        {
            int max = (int)Manager.GameManager.GetMaxTrackCount();
            int cur = (int)Manager.GameManager.MusicTrackNumber;
            int r = max - cur + 1;
            return r < 0 ? 0 : r;
        }

        /// <summary>记录模式需要的曲数：普通曲 2，长曲 4。</summary>
        public static int Required(bool isLong)
        {
            return isLong ? 4 : 2;
        }

        /// <summary>当前这首歌是否放得下记录模式。</summary>
        public static bool CanAfford(bool isLong)
        {
            return RemainingIncludingCurrent() >= Required(isLong);
        }
    }
}
