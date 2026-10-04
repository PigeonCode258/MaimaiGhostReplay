// ============================================================================
//  GhostFlow.cs —— 状态机：手势、开启/关闭、第二首配对、进出游戏
//
//  说明：跟游戏交互的部分一律用「强转成游戏自己的接口/类型」，而不是反射字符串。
//        这样签名由编译器检查，写错就直接编译不过，而不是运行时静默失效。
//        只有 SequenceBase 的 protected 字段和 ProcessDataContainer 必须走反射。
// ============================================================================
using System;
using System.Reflection;
using DB;
using MAI2.Util;
using Manager;
using Process;

namespace MaimaiGhostReplay
{
    public static class GhostFlow
    {
        // ---------------------------------------------------------------- 入口 1
        /// <summary>
        /// 补丁点：Process.SubSequence.MenuSelectSequence.Update()
        /// 也就是「选定难度后按 4 号键就会开始游戏」的那个页面。
        /// 该页只占用 Button03/04/05/06 与触摸区，1/2/7/8 是空闲的。
        /// </summary>
        public static void OnMenuUpdate(object seq)
        {
            if (seq == null) return;

            IMusicSelectProcessProcessing pp = GetProcessProcessing(seq);
            if (pp == null) return;
            GameRefs.CaptureMusicSelect(pp as MusicSelectProcess);

            int player = GetPlayerIndex(seq);
            if (player != 0) return;   // 本版只支持 1P

            // 只在 GameStart 卡片页生效
            MusicSelectProcess.MenuType[] menus = pp.CurrentSelectMenu;
            if (menus == null || player >= menus.Length ||
                menus[player] != MusicSelectProcess.MenuType.GameStart)
            {
                GhostState.ResetTimer(player);
                return;
            }

            bool all =
                InputManager.GetButtonPush(player, InputManager.ButtonSetting.Button01) &&
                InputManager.GetButtonPush(player, InputManager.ButtonSetting.Button02) &&
                InputManager.GetButtonPush(player, InputManager.ButtonSetting.Button07) &&
                InputManager.GetButtonPush(player, InputManager.ButtonSetting.Button08);

            if (!all) { GhostState.ResetTimer(player); return; }

            GhostState.PushTimer[player] += GameManager.GetGameMSecAddD();
            if (GhostState.PushTimer[player] < GhostReplayMod.LongPressMs) return;

            // 触发，先归零避免连发
            GhostState.ResetTimer(player);
            GhostReplayMod.Log("手势触发（长按 1/2/7/8 满 " + (int)GhostReplayMod.LongPressMs + "ms）");

            if (GhostState.Phase != GhostPhase.Idle)
            {
                GhostState.StopMode();
                GhostReplayMod.Log("记录模式关闭");
                Notice.Show(player, "已关闭记录模式");
                return;
            }
            TryEnable(player, pp);
        }

        // ---------------------------------------------------------------- 开启
        private static void TryEnable(int player, IMusicSelectProcessProcessing pp)
        {
            // 1) 只支持单人
            if (Singleton<UserDataManager>.Instance.GetUserData(1L).IsEntry)
            {
                Notice.Show(player, "记录模式仅支持单人游玩");
                return;
            }
            // 2) 排除 track 语义不同的模式
            if (GameManager.IsCourseMode) { Notice.Show(player, "段位模式无法使用记录模式"); return; }
            if (GameManager.IsFreedomMode) { Notice.Show(player, "Freedom 模式无法使用记录模式"); return; }
            if (GameManager.IsEventMode) { Notice.Show(player, "活动模式无法使用记录模式"); return; }

            // 3) 读当前高亮的曲目
            MusicSelectProcess.MusicSelectData msd = pp.GetMusic(0);
            if (msd == null || msd.MusicData == null)
            {
                Notice.Show(player, "无法读取当前曲目");
                return;
            }
            // v0.4.0：宴会场已支持。但双人宴会谱（DoublePlayerScore）在单人游玩时
            // 游戏会自动把曲目换成别的 utage 曲，那样第二首就配不上对了，提前拒绝。
            if (pp.IsUtageMusicFolder() &&
                msd.MusicData.utagePlayStyle == Manager.MaiStudio.UtagePlayStyle.DoublePlayerScore)
            {
                Notice.Show(player, "双人宴会谱无法使用记录模式");
                return;
            }

            int musicId = msd.MusicData.GetID();
            bool isLong = Singleton<DataManager>.Instance.IsLong(msd.MusicData.longMusic);
            int difficulty = ReadDifficulty(pp, player, msd);

            // 4) 剩余曲数校验（决策 B：只校验，不预扣）
            int remain = TrackBudget.RemainingIncludingCurrent();
            int need = TrackBudget.Required(isLong);
            if (remain < need)
            {
                Notice.Show(player, "剩余曲数不足（需要 " + need + " 首，剩余 " + remain + " 首）");
                return;
            }

            GhostState.Phase = GhostPhase.Armed;
            GhostState.BaseMusicId = musicId;
            GhostState.BaseDifficulty = difficulty;
            GhostState.BaseIsLong = isLong;
            GhostState.Recording = null;
            GhostState.Record = null;

            GhostReplayMod.Log("记录模式开启: music=" + musicId + " diff=" + difficulty +
                " (来源 " + DifficultySourceText(pp, player) + ") long=" + isLong +
                " 剩余=" + remain + "/" + need);
            Notice.Show(player, "已开启记录模式");
        }

        private static int ReadDifficulty(IMusicSelectProcessProcessing pp, int player,
            MusicSelectProcess.MusicSelectData msd)
        {
            if (pp.IsLevelTab()) return msd.Difficulty;

            int[] dsi = pp.DifficultySelectIndex;
            if (dsi != null && player < dsi.Length && dsi[player] != -1) return dsi[player];

            MusicDifficultyID[] cd = pp.CurrentDifficulty;
            if (cd != null && player < cd.Length) return (int)cd[player];

            return -1;
        }

        // ---------------------------------------------------------------- 入口 2
        /// <summary>
        /// 补丁点：Process.MusicSelectProcess.OnGameStart()
        /// 玩家「确认开始这首歌」的瞬间 —— 需求 4 说的「以第二首歌开始游戏为准」。
        /// 此处 SelectMusicID / SelectDifficultyID / IsLongMusic 已全部就绪。
        /// </summary>
        public static void OnMusicSelectGameStart(MusicSelectProcess msp)
        {
            GameRefs.CaptureMusicSelect(msp);
            if (GhostState.Phase != GhostPhase.AwaitingSecond) return;

            int musicId = GameManager.SelectMusicID[0];
            int difficulty = GameManager.SelectDifficultyID[0];

            if (musicId != GhostState.BaseMusicId || difficulty != GhostState.BaseDifficulty)
            {
                GhostReplayMod.Log("第二首不匹配: 期望 music=" + GhostState.BaseMusicId +
                    " diff=" + GhostState.BaseDifficulty +
                    "，实际 music=" + musicId + " diff=" + difficulty);
                // 决策 B 下没有预扣，所以「归还 track」是空操作，只需退出模式
                GhostState.StopMode();
                Notice.Show(0, "未选择同一首曲目，已退出记录模式");
                return;
            }

            GhostState.Phase = GhostPhase.Replaying;
            GhostReplayMod.Log("第二首匹配成功，进入回放: music=" + musicId + " diff=" + difficulty);
        }

        // ---------------------------------------------------------------- 入口 3
        /// <summary>补丁点：Process.GameProcess.OnStart() —— 每局进歌的必经点。</summary>
        public static void OnGameProcessStart(GameProcess gp)
        {
            GameRefs.CaptureFromProcess(gp);

            if (GhostState.Phase == GhostPhase.Armed)
            {
                SongRecord rec = new SongRecord();
                rec.MusicId = GameManager.SelectMusicID[0];
                rec.Difficulty = GameManager.SelectDifficultyID[0];
                rec.IsLong = GameManager.IsLongMusic;
                rec.TrackNumber = (int)GameManager.MusicTrackNumber;
                GhostState.Recording = rec;
                GhostState.Phase = GhostPhase.Recording;

                Recorder.BeginSong();
                ReplayInjector.BeginSong();
                GhostReplayMod.Log("开始录制 track=" + rec.TrackNumber +
                    " music=" + rec.MusicId + " diff=" + rec.Difficulty);
                return;
            }

            if (GhostState.Phase == GhostPhase.Replaying)
            {
                Recorder.BeginSong();
                ReplayInjector.BeginSong();
                GhostReplayMod.Log("开始回放 track=" + (int)GameManager.MusicTrackNumber +
                    " 已录 note 数=" + (GhostState.Record == null ? 0 : GhostState.Record.NoteCount));
            }
        }

        // ---------------------------------------------------------------- 入口 4
        /// <summary>补丁点：Process.GameProcess.OnRelease() —— 一局结束（进结算）。</summary>
        public static void OnGameProcessRelease(GameProcess gp)
        {
            GameRefs.CaptureFromProcess(gp);

            if (GhostState.Phase == GhostPhase.Recording)
            {
                SongRecord rec = GhostState.Recording;
                GhostState.Recording = null;

                if (rec == null || rec.NoteCount == 0)
                {
                    GhostReplayMod.LogWarn("录制为空，退出记录模式");
                    GhostState.StopMode();
                    return;
                }

                GhostState.Record = rec;
                GhostState.Phase = GhostPhase.AwaitingSecond;
                GhostReplayMod.Log("录制完成: " + rec.NoteCount + " 个 note（music=" + rec.MusicId +
                    " diff=" + rec.Difficulty + "）");
                DumpRecord(rec);
                return;
            }

            if (GhostState.Phase == GhostPhase.Replaying)
            {
                GhostReplayMod.Log("回放局结束，退出记录模式");
                GhostReplayMod.Log("  回放判定音: " + ReplayInjector.JudgeSeCount +
                    " 次（含 hold 头判 " + ReplayInjector.HoldHeadSeCount + " 次）");
                GhostReplayMod.Log("  回放 slide: 自动推进 " + ReplayInjector.SlideAutoCount +
                    " 条 / 交给超时路径 " + ReplayInjector.SlideManualCount + " 条");
                GhostReplayMod.Log("  回放 hold: 注入头判 " + ReplayInjector.HoldInjected +
                    " 条 / 玩家自己按 " + ReplayInjector.HoldPlayerTook +
                    " 条 / 录制无头判 " + ReplayInjector.HoldNoHead + " 条");
                GhostReplayMod.Log("  回放 hold 保持态: 补出 " + ReplayInjector.HoldBodyFaked + " 条");
                GhostState.StopMode();
            }
        }

        // ---------------------------------------------------------------- 入口 5
        /// <summary>
        /// 补丁点：Process.MusicSelectProcess.OnStart()
        /// v0.2.0 新增：第二首时强制把光标预选到「同一首歌 + 同一难度」。
        ///
        /// 为什么需要自己做：游戏自己在 OnStart 里也会尝试恢复（MusicSelectProcess.cs:1270-1311），
        /// 但那段是按排序方式分支的，默认「曲风」排序走 SetGenreSortIndex，
        /// 只设分类、不按曲目定位。这里直接调游戏自己的 SetSortIndexToMaiList 强制定位，
        /// 再照搬 SetGhostJumpIndex()(:4193) 的做法调 SetDeployList 把光标推到画面上。
        /// </summary>
        public static void OnMusicSelectStart(MusicSelectProcess msp)
        {
            GameRefs.CaptureMusicSelect(msp);
            if (GhostState.Phase != GhostPhase.AwaitingSecond) return;
            if (GhostState.BaseMusicId < 0) return;

            int musicId = GhostState.BaseMusicId;
            int difficulty = GhostState.BaseDifficulty;

            bool located = false;
            try
            {
                MethodInfo mi = typeof(MusicSelectProcess).GetMethod(
                    "SetSortIndexToMaiList",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new System.Type[] { typeof(int), typeof(int), typeof(bool), typeof(bool) },
                    null);
                if (mi != null)
                {
                    object r = mi.Invoke(msp, new object[] { musicId, difficulty, true, true });
                    if (r is bool) located = (bool)r;
                }
                else
                {
                    GhostReplayMod.LogWarn("找不到 SetSortIndexToMaiList，无法强制预选");
                }
            }
            catch (Exception e)
            {
                GhostReplayMod.LogWarn("SetSortIndexToMaiList 调用失败: " + e.Message);
            }

            // v0.4.0：宴会场曲目属于 extra 分类，而 SetSortIndexToMaiList 只搜索
            // IsMaiList 的分类，找不到 utage；这时改用游戏自己的 SetSortIndexToExtraGenre。
            if (!located && msp.IsUtageMusicFolder())
            {
                located = CallSetSortIndexToExtraGenre(msp, musicId);
            }

            // 难度：两个都是 public 属性，游戏自己也是就地改数组元素
            try
            {
                int[] dsi = msp.DifficultySelectIndex;
                if (dsi != null)
                {
                    for (int i = 0; i < dsi.Length; i++) dsi[i] = difficulty;
                }
                MusicDifficultyID[] cd = msp.CurrentDifficulty;
                if (cd != null)
                {
                    for (int i = 0; i < cd.Length; i++) cd[i] = (MusicDifficultyID)difficulty;
                }
            }
            catch (Exception e)
            {
                GhostReplayMod.LogWarn("设置难度失败: " + e.Message);
            }

            // 把光标推到画面上 —— 与游戏自己的 SetGhostJumpIndex() 做法一致
            try
            {
                Monitor.MusicSelectMonitor[] ma = msp.MonitorArray;
                if (ma != null)
                {
                    for (int i = 0; i < ma.Length; i++)
                    {
                        if (ma[i] == null) continue;
                        ma[i].SetDeployList(false, false);
                    }
                }
            }
            catch (Exception e)
            {
                GhostReplayMod.LogWarn("SetDeployList 失败: " + e.Message);
            }

            GhostReplayMod.Log("第二首预选: music=" + musicId + " diff=" + difficulty +
                (located ? " 定位成功" : " 定位失败（保持游戏默认）"));
        }
        // ---------------------------------------------------------------- 入口 6/7（v0.3.0）
        /// <summary>
        /// 补丁点：Process.SubSequence.DifficultySelectSequence.OnStartSequence() 的 Prefix。
        ///
        /// v0.3.0 修复：难度页读取 DifficultySelectIndex 之前把它纠正过来。
        /// 为什么不在 MusicSelectProcess.OnStart() 里设：从那里到玩家真正进难度页，
        /// 中间隔着整套选曲操作，而 OptionSelectSequence:164 / MenuSelectSequence:130 /
        /// DifficultySelectSequence:218 都会把 DifficultySelectIndex 写成 -1；
        /// 一旦被冲掉，难度页的 `if (... != -1)` 就会走 else 分支退回 Basic。
        /// 这里是「消费点」，写完之后游戏紧接着就读，中间再没有东西能覆盖。
        /// </summary>
        public static void OnDifficultySelectStart(object seq)
        {
            if (GhostState.Phase != GhostPhase.AwaitingSecond) return;

            MusicSelectProcess msp = GameRefs.MusicSelect;
            if (msp == null) return;

            int d = GhostState.BaseDifficulty;
            if (d < 0) return;

            int player = GetPlayerIndex(seq);
            int oldDsi = -99;
            try
            {
                int[] dsi = msp.DifficultySelectIndex;
                if (dsi != null && player < dsi.Length)
                {
                    oldDsi = dsi[player];
                    dsi[player] = d;
                }
                MusicDifficultyID[] cd = msp.CurrentDifficulty;
                if (cd != null && player < cd.Length) cd[player] = (MusicDifficultyID)d;
            }
            catch (Exception e)
            {
                GhostReplayMod.LogWarn("难度页纠正难度失败: " + e.Message);
                return;
            }
            GhostReplayMod.Log("强制难度: " + d + " (原 dsi=" + oldDsi + ")");
        }

        /// <summary>
        /// 补丁点：Monitor.MusicSelectMonitor.OnStartDifficultySelect(MusicDifficultyID, Action) 的 Prefix。
        /// 第二层兜底 —— 直接改写真正喂给难度页的那个值，前面数据即使被冲掉也拦得住。
        /// </summary>
        public static void ForceDifficultyArg(ref MusicDifficultyID selectDifficulty)
        {
            if (GhostState.Phase != GhostPhase.AwaitingSecond) return;
            if (GhostState.BaseDifficulty < 0) return;

            MusicDifficultyID want = (MusicDifficultyID)GhostState.BaseDifficulty;
            if (selectDifficulty == want) return;

            GhostReplayMod.Log("强制难度(显示层): " + (int)selectDifficulty + " -> " + GhostState.BaseDifficulty);
            selectDifficulty = want;
        }

        /// <summary>把难度的原始来源打出来，便于定位 BaseDifficulty 是否取错。</summary>
        private static string DifficultySourceText(IMusicSelectProcessProcessing pp, int player)
        {
            try
            {
                int[] dsi = pp.DifficultySelectIndex;
                MusicDifficultyID[] cd = pp.CurrentDifficulty;
                int a = (dsi != null && player < dsi.Length) ? dsi[player] : -99;
                int b = (cd != null && player < cd.Length) ? (int)cd[player] : -99;
                return "dsi=" + a + " cd=" + b + " levelTab=" + pp.IsLevelTab();
            }
            catch (Exception) { return "?"; }
        }
        /// <summary>
        /// 反射调用 MusicSelectProcess.SetSortIndexToExtraGenre(int musicID, string genreName)。
        /// 宴会场（genre 107）走这个，SetSortIndexToMaiList 搜不到 extra 分类。
        /// </summary>
        private static bool CallSetSortIndexToExtraGenre(MusicSelectProcess msp, int musicId)
        {
            try
            {
                string genreName = Singleton<DataManager>.Instance.GetMusicGenre(107).genreName;
                MethodInfo mi = typeof(MusicSelectProcess).GetMethod(
                    "SetSortIndexToExtraGenre",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new System.Type[] { typeof(int), typeof(string) },
                    null);
                if (mi == null)
                {
                    GhostReplayMod.LogWarn("找不到 SetSortIndexToExtraGenre");
                    return false;
                }
                object r = mi.Invoke(msp, new object[] { musicId, genreName });
                return (r is bool) && (bool)r;
            }
            catch (Exception e)
            {
                GhostReplayMod.LogWarn("SetSortIndexToExtraGenre 调用失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 补丁点：Process.SubSequence.UtageDifficultySelectSequence.OnStartSequence() 的 Prefix。
        /// v0.4.0：宴会场的难度页读的是 CurrentDifficulty（不是 DifficultySelectIndex），
        /// 而且调的是 OnStartUtageDifficultySelect —— 所以普通难度页的那两层拦不住它。
        /// </summary>
        public static void OnUtageDifficultySelectStart(object seq)
        {
            if (GhostState.Phase != GhostPhase.AwaitingSecond) return;

            MusicSelectProcess msp = GameRefs.MusicSelect;
            if (msp == null) return;

            int d = GhostState.BaseDifficulty;
            if (d < 0) return;

            int player = GetPlayerIndex(seq);
            int oldCd = -99;
            try
            {
                MusicDifficultyID[] cd = msp.CurrentDifficulty;
                if (cd != null && player < cd.Length)
                {
                    oldCd = (int)cd[player];
                    cd[player] = (MusicDifficultyID)d;
                }
                int[] dsi = msp.DifficultySelectIndex;
                if (dsi != null && player < dsi.Length) dsi[player] = d;
            }
            catch (Exception e)
            {
                GhostReplayMod.LogWarn("宴会场纠正难度失败: " + e.Message);
                return;
            }
            GhostReplayMod.Log("强制难度(宴会场): " + d + " (原 cd=" + oldCd + ")");
        }
        // ---------------------------------------------------------------- 反射工具
        private static IMusicSelectProcessProcessing GetProcessProcessing(object seq)
        {
            try
            {
                return Reflect.GetMember(seq, "ProcessProcessing") as IMusicSelectProcessProcessing;
            }
            catch (Exception e)
            {
                GhostReplayMod.LogWarn("取 ProcessProcessing 失败: " + e.Message);
                return null;
            }
        }

        private static int GetPlayerIndex(object seq)
        {
            try
            {
                object v = Reflect.GetMember(seq, "PlayerIndex");
                if (v is int) return (int)v;
            }
            catch (Exception) { }
            return 0;
        }

        /// <summary>把录制结果打到 MelonLoader 日志，Stage 验收时用。</summary>
        private static void DumpRecord(SongRecord rec)
        {
            if (!GhostReplayMod.DumpRecordToLog) return;

            System.Collections.Generic.List<NoteRecord> list = rec.Snapshot();
            int withHead = 0, withSlide = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].HasHead) withHead++;
                if (list[i].SlidePath != null && list[i].SlidePath.Count > 0) withSlide++;
            }
            GhostReplayMod.Log("  含 hold/touchhold 头判: " + withHead + "，含 slide 路径: " + withSlide);

            // v0.2.0 自检：判定时刻区间。min 接近 0 说明录制又被结算补判污染了。
            float minMs = float.MaxValue;
            float maxMs = float.MinValue;
            int badTime = 0;
            for (int i = 0; i < list.Count; i++)
            {
                float t = list[i].JudgeMsec;
                if (t <= 0f) { badTime++; continue; }
                if (t < minMs) minMs = t;
                if (t > maxMs) maxMs = t;
            }
            string range = (list.Count == 0 || minMs > maxMs)
                ? "无有效记录"
                : (minMs.ToString("F0") + "ms ~ " + maxMs.ToString("F0") + "ms");
            GhostReplayMod.Log("  判定时刻区间: " + range +
                (badTime > 0 ? ("，非法时刻 " + badTime + " 条") : ""));

            int show = list.Count < 24 ? list.Count : 24;
            for (int i = 0; i < show; i++)
            {
                NoteRecord n = list[i];
                string line = "  [#" + n.NoteIndex + "] kind=" + n.ScoreKind +
                    " timing=" + n.Timing + " @ " + n.JudgeMsec.ToString("F1") + "ms";
                if (n.HasHead) line += " head=" + n.HeadTiming + "@" + n.HeadMsec.ToString("F1");
                if (n.HasHoldEnd) line += " release@" + n.HoldEndMsec.ToString("F1");
                if (n.SlidePath != null && n.SlidePath.Count > 0) line += " slidePath=" + n.SlidePath.Count;
                GhostReplayMod.Log(line);
            }
        }
    }
}
