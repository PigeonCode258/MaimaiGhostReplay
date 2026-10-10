// ============================================================================
//  GhostReplayMod.cs —— MelonLoader 入口
//
//  maimai DX「记录模式」：第一局录制玩家判定，第二局按录制回放，
//  同时允许玩家真实操作覆盖（取并集）。
//
//  目标框架：net35 / net40 均可（游戏是 Mono，非 IL2CPP）
//  语言版本：C# 5（兼容零安装的 csc.exe）
// ============================================================================
using System;
using HarmonyLib;
using MelonLoader;

[assembly: MelonInfo(typeof(MaimaiGhostReplay.GhostReplayMod), "MaimaiGhostReplay", "0.7.2.1", "Sayori")]
[assembly: MelonGame("sega-interactive", "Sinmai")]

namespace MaimaiGhostReplay
{
    public class GhostReplayMod : MelonMod
    {
        /// <summary>手势长按时长（毫秒）。</summary>
        public static double LongPressMs = 3000.0;

        /// <summary>是否把录制结果打到日志（Stage 验收用）。</summary>
        public static bool DumpRecordToLog = true;

        private static MelonLogger.Instance _log;
        private static bool _enabled = true;

        public override void OnInitializeMelon()
        {
            _log = LoggerInstance;

            try
            {
                MelonPreferences_Category cat = MelonPreferences.CreateCategory("GhostReplay");
                MelonPreferences_Entry<bool> eEnabled = cat.CreateEntry<bool>("Enabled", true);
                MelonPreferences_Entry<double> ePress = cat.CreateEntry<double>("LongPressMs", 3000.0);
                MelonPreferences_Entry<bool> eDump = cat.CreateEntry<bool>("DumpRecordToLog", true);

                _enabled = eEnabled.Value;
                LongPressMs = ePress.Value;
                DumpRecordToLog = eDump.Value;
            }
            catch (Exception e)
            {
                _log.Warning("[GhostReplay] 读取配置失败，用默认值继续: " + e.Message);
            }

            if (LongPressMs < 500.0) LongPressMs = 500.0;

            if (!_enabled)
            {
                _log.Msg("[GhostReplay] 配置里 Enabled=false，不加载任何补丁");
                return;
            }

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("maimai.ghostreplay");
            Patches.ApplyAll(harmony);

            _log.Msg("[GhostReplay] MaimaiGhostReplay v0.7.2.1 已加载"
                + "（手势：开始游戏页长按 1+2+7+8 " + (int)LongPressMs + "ms）");
        }

        public override void OnUpdate()
        {
            try { Notice.Tick(); }
            catch (Exception) { }
        }

        public override void OnDeinitializeMelon()
        {
            Log("已卸载");
        }

        public static void Log(string msg)
        {
            if (_log != null) _log.Msg("[GhostReplay] " + msg);
        }

        public static void LogWarn(string msg)
        {
            if (_log != null) _log.Warning("[GhostReplay] " + msg);
        }
    }
}
