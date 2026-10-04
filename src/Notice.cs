// ============================================================================
//  Notice.cs —— 警告弹窗 + 反射小工具
//
//  弹窗完全复用游戏自带的 WindowMessage 管线：
//      EnqueueMessage(monitorId, WindowMessageID.TrackSkip3Second, WindowPositionID.Middle)
//  TrackSkip3Second(137) 的记录是 Kind=1 / Position=1 / Size=3 / Title="ATTENTION!"，
//  与游戏内 track skip 警告框是同一个 prefab，样式 100% 一致。
//  文案通过 Prefix 覆盖 WindowMessageIDEnum.GetName() 来替换（见 Patches.cs）。
// ============================================================================
using System;
using System.Reflection;
using DB;
using Manager;

namespace MaimaiGhostReplay
{
    /// <summary>反射工具：字段/属性通吃，向基类查找，结果缓存。</summary>
    public static class Reflect
    {
        // 菜单位于每帧调用路径上，Type.GetField 每次都要重新搜索并分配，
        // 所以把 (类型, 成员名) -> MemberInfo 缓存起来。
        private static readonly System.Collections.Generic.Dictionary<string, MemberInfo> _cache =
            new System.Collections.Generic.Dictionary<string, MemberInfo>();

        // 方法查找单独一个缓存（键空间与字段/属性分开，避免同名冲突）
        private static readonly System.Collections.Generic.Dictionary<string, MethodInfo> _methodCache =
            new System.Collections.Generic.Dictionary<string, MethodInfo>();

        private static MemberInfo Lookup(System.Type type, string name)
        {
            string key = type.FullName + "|" + name;
            MemberInfo mi;
            if (_cache.TryGetValue(key, out mi)) return mi;

            System.Type t = type;
            while (t != null)
            {
                FieldInfo f = t.GetField(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) { _cache[key] = f; return f; }
                t = t.BaseType;
            }
            PropertyInfo p = type.GetProperty(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanRead) { _cache[key] = p; return p; }

            _cache[key] = null;   // 记下「确实没有」，避免每帧重复搜索
            return null;
        }

        public static object GetMember(object obj, string name)
        {
            if (obj == null) return null;
            MemberInfo mi = Lookup(obj.GetType(), name);
            if (mi == null) return null;

            FieldInfo f = mi as FieldInfo;
            if (f != null) return f.GetValue(obj);

            PropertyInfo p = mi as PropertyInfo;
            if (p != null && p.CanRead) return p.GetValue(obj, null);
            return null;
        }

        public static FieldInfo FindField(System.Type type, string name)
        {
            MemberInfo mi = Lookup(type, name);
            return mi as FieldInfo;
        }

        /// <summary>
        /// 按名字找无参方法（含 protected / private），向基类查找并缓存。
        /// 用于调用 note 自己的 PlayJudgeSe / PlayJudgeHeadSe —— 它们是 protected virtual，
        /// 各子类重写不同，只能靠反射拿到具体类型上的那一个。
        /// </summary>
        public static MethodInfo FindMethod(System.Type type, string name)
        {
            return FindMethod(type, name, System.Type.EmptyTypes);
        }

        /// <summary>带参数版本（例如 HoldOn(bool)）。缓存键含参数个数。</summary>
        public static MethodInfo FindMethod(System.Type type, string name, System.Type[] argTypes)
        {
            if (type == null) return null;
            if (argTypes == null) argTypes = System.Type.EmptyTypes;

            string key = type.FullName + "|" + name + "|" + argTypes.Length;
            MethodInfo cached;
            if (_methodCache.TryGetValue(key, out cached)) return cached;

            MethodInfo mi = null;
            System.Type t = type;
            while (t != null && mi == null)
            {
                mi = t.GetMethod(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, argTypes, null);
                t = t.BaseType;
            }
            _methodCache[key] = mi;
            return mi;
        }

        public static System.Type FindType(string fullName)
        {
            Assembly asm = typeof(Reflect).Assembly;
            System.Type t = asm.GetType(fullName, false);
            if (t != null) return t;
            Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < all.Length; i++)
            {
                t = all[i].GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }
    }

    /// <summary>缓存游戏内部引用，供各处补丁取用。</summary>
    public static class GameRefs
    {
        /// <summary>ProcessManager —— 弹窗/关窗用。</summary>
        public static ProcessManager ProcessManager;

        /// <summary>MusicSelectProcess —— 选曲流程里用它弹窗最省事。</summary>
        public static Process.MusicSelectProcess MusicSelect;

        /// <summary>从任意 ProcessBase 实例（如 GameProcess）捞出 ProcessManager。</summary>
        public static void CaptureFromProcess(object processBase)
        {
            if (ProcessManager != null) return;
            try
            {
                object container = Reflect.GetMember(processBase, "container");
                if (container == null) return;
                object pm = Reflect.GetMember(container, "processManager");
                if (pm is ProcessManager) ProcessManager = (ProcessManager)pm;
            }
            catch (Exception e)
            {
                GhostReplayMod.LogWarn("CaptureFromProcess 失败: " + e.Message);
            }
        }

        public static void CaptureMusicSelect(Process.MusicSelectProcess musicSelectProcess)
        {
            if (musicSelectProcess != null) MusicSelect = musicSelectProcess;
            CaptureFromProcess(musicSelectProcess);
        }
    }

    /// <summary>警告弹窗。</summary>
    public static class Notice
    {
        /// <summary>供 WindowMessageIDEnum.GetName 的 Prefix 判断是否要替换文案。</summary>
        public static bool Showing;
        public static string Text = "";

        private const float AutoHideSeconds = 2.5f;
        private static float _hideAt = -1f;
        private static int _shownOn = -1;

        /// <summary>弹出与 track skip 同样式的警告框，文案替换为 text。</summary>
        public static void Show(int monitorId, string text)
        {
            Text = text;
            Showing = true;
            _shownOn = monitorId;
            _hideAt = UnityEngine.Time.realtimeSinceStartup + AutoHideSeconds;

            bool ok = false;

            // 路线 1：选曲流程的公开 API（编译期检查签名，不用反射）
            if (GameRefs.MusicSelect != null)
            {
                try
                {
                    GameRefs.MusicSelect.CallMessage(monitorId, WindowMessageID.TrackSkip3Second);
                    ok = true;
                }
                catch (Exception e)
                {
                    GhostReplayMod.LogWarn("CallMessage 弹窗失败: " + e.Message);
                }
            }

            // 路线 2：直接用 ProcessManager
            if (!ok && GameRefs.ProcessManager != null)
            {
                try
                {
                    GameRefs.ProcessManager.EnqueueMessage(
                        monitorId, WindowMessageID.TrackSkip3Second, WindowPositionID.Middle, null);
                    ok = true;
                }
                catch (Exception e)
                {
                    GhostReplayMod.LogWarn("EnqueueMessage 弹窗失败: " + e.Message);
                }
            }

            if (!ok) GhostReplayMod.LogWarn("弹窗失败：拿不到 ProcessManager / MusicSelectProcess");
            GhostReplayMod.Log("弹窗: " + text);
        }

        /// <summary>每帧调用，到点自动关窗。</summary>
        public static void Tick()
        {
            if (!Showing) return;
            if (_hideAt < 0f) return;
            if (UnityEngine.Time.realtimeSinceStartup < _hideAt) return;

            Showing = false;
            _hideAt = -1f;

            if (GameRefs.ProcessManager != null && _shownOn >= 0)
            {
                try { GameRefs.ProcessManager.CloseWindow(_shownOn); }
                catch (Exception) { /* 关窗失败无所谓，别影响游戏 */ }
            }
            _shownOn = -1;
        }

        /// <summary>立刻撤下（状态切换时用）。</summary>
        public static void ForceHide()
        {
            Showing = false;
            _hideAt = -1f;
            if (GameRefs.ProcessManager != null && _shownOn >= 0)
            {
                try { GameRefs.ProcessManager.CloseWindow(_shownOn); }
                catch (Exception) { }
            }
            _shownOn = -1;
        }
    }
}
