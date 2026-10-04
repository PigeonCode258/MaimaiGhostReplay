// ============================================================================
//  maimai DX (SDEZ 1.70 / Sinmai) MelonLoader 最小示例 Mod
//  注意：本文件**不参与** build.ps1 / csproj 的编译（只编译 src\ 下的文件），
//        它是给新人的一个「三种补丁写法」参考。
//  目标框架: .NET Framework 3.5 / 4.0 均可 (游戏是 Mono, 非 IL2CPP)
//  语言版本: 为兼容零安装的 csc.exe (C# 5), 本文件刻意不使用 C# 6+ 语法
// ============================================================================
using System;
using HarmonyLib;
using MelonLoader;

[assembly: MelonInfo(typeof(MaimaiModDev.ExampleMod), "MaimaiExampleMod", "1.0.0", "yourname")]
// 绑定到游戏: app.info 里的 (company, product) = (sega-interactive, Sinmai)
[assembly: MelonGame("sega-interactive", "Sinmai")]

namespace MaimaiModDev
{
    /// <summary>
    /// MelonLoader 入口。AquaMai 与本 Mod 可以共存。
    /// </summary>
    public class ExampleMod : MelonMod
    {
        public static MelonPreferences_Category Cfg;
        public static MelonPreferences_Entry<bool> LogJudgements;
        public static MelonPreferences_Entry<bool> ForceAutoPlay;

        private static MelonLogger.Instance _log;

        public override void OnInitializeMelon()
        {
            _log = LoggerInstance;

            Cfg = MelonPreferences.CreateCategory("ExampleMod", "Example Mod");
            LogJudgements = Cfg.CreateEntry<bool>("LogJudgements", true, "记录每个 note 的判定结果");
            ForceAutoPlay = Cfg.CreateEntry<bool>("ForceAutoPlay", false, "强制 AutoPlay");

            // 自己持有 Harmony 实例, 避免依赖 MelonLoader 版本差异
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("maimai.exampledev.examplemod");
            harmony.PatchAll(typeof(ExampleMod).Assembly);

            _log.Msg("ExampleMod v1.0.0 已加载");
        }

        public override void OnDeinitializeMelon()
        {
            _log.Msg("ExampleMod 已卸载");
        }

        public static void Log(string msg)
        {
            if (_log != null) _log.Msg(msg);
        }
    }

    // ------------------------------------------------------------------------
    // 示例 1: Prefix —— 直接改写游戏逻辑 (返回值)
    //   目标: Manager.GameManager.IsAutoPlay() 是 public static bool
    //   Prefix 返回 false = 跳过原方法, 用 __result 作为返回值
    // ------------------------------------------------------------------------
    [HarmonyPatch(typeof(Manager.GameManager), "IsAutoPlay")]
    internal static class Patch_GameManager_IsAutoPlay
    {
        private static bool Prefix(ref bool __result)
        {
            if (ExampleMod.ForceAutoPlay != null && ExampleMod.ForceAutoPlay.Value)
            {
                __result = true;
                return false; // 不执行原方法
            }
            return true; // 执行原方法
        }
    }

    // ------------------------------------------------------------------------
    // 示例 2: Postfix —— 只观察, 不改逻辑
    //   目标: Monitor.NoteBase.SetPlayResult() 是 protected virtual void
    //   protected/private 方法用字符串名打补丁; __instance 声明为声明类型即可
    // ------------------------------------------------------------------------
    [HarmonyPatch(typeof(Monitor.NoteBase), "SetPlayResult")]
    internal static class Patch_NoteBase_SetPlayResult
    {
        private static void Postfix(Monitor.NoteBase __instance)
        {
            if (ExampleMod.LogJudgements == null || !ExampleMod.LogJudgements.Value) return;
            ExampleMod.Log(string.Format("[note] index={0} judge={1}",
                __instance.GetNoteIndex(), __instance.GetJudgeResult()));
        }
    }

    // ------------------------------------------------------------------------
    // 示例 3: 静态构造 / 初始化钩子 —— 常用于在歌曲开始时做一次性准备
    //   目标: Monitor.GameMonitor.Initialize(int monIndex, bool active)
    //   注意: 参数名必须与游戏里一致才能用 __0/__1 或按名注入
    // ------------------------------------------------------------------------
    [HarmonyPatch(typeof(Monitor.GameMonitor), "Initialize")]
    internal static class Patch_GameMonitor_Initialize
    {
        private static void Postfix(int monIndex)
        {
            ExampleMod.Log(string.Format("[game] GameMonitor.Initialize monitor={0} music={1}",
                monIndex, Manager.GameManager.SelectMusicID[monIndex]));
        }
    }
}
