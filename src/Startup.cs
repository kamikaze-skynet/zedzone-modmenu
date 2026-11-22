using System;
using System.Linq;
using UnityEngine;

namespace ModMenu
{
    // A few seconds after launch (when every mod has loaded): write each mod's keys to
    // ModMenu.log and, if two of them share a key, show a short notice in the corner.
    internal static class Startup
    {
        static string _notice;
        static float _until;

        internal static void Check()
        {
            Registry.Refresh(true);
            var keys = Registry.AllKeys.ToList();
            MLog.Info($"{keys.Count} hotkeys: " + string.Join(", ", keys.Select(k => $"{k.Mod.Name}.{k.Name}={k.Value}")));
            var clashing = keys.Where(k => Registry.ClashesWith(k).Count > 0).ToList();
            foreach (var k in clashing)
                MLog.Warn($"Key clash: {k.Mod.Name} {k.Name} = {k.Value} is also {string.Join(", ", Registry.ClashesWith(k))}");
            if (clashing.Count > 0 && Plugin.WarnKeyClashes.Value)
            {
                _notice = $"Mod Menu: {clashing.Count} mod hotkeys share a key ({string.Join(", ", clashing.Select(k => k.Value).Distinct())}). Press {In.KeyName(Plugin.MenuKey.Value)} > All keys to fix.";
                _until = Time.unscaledTime + 15f;
            }
        }

        internal static void DrawNotice()
        {
            if (_notice == null || Time.unscaledTime > _until) return;
            float s = Mathf.Clamp(Screen.height / 1080f, 0.6f, 3f);
            var skin = GUI.skin;
            int old = skin.label.fontSize;
            try
            {
                skin.label.fontSize = Mathf.RoundToInt(14f * s);
                var r = new Rect(10f * s, Screen.height - 50f * s, Mathf.Min(900f * s, Screen.width - 20f * s), 36f * s);
                GUI.Box(r, ""); GUI.Box(r, ""); GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 8f * s, r.y + 4f * s, r.width - 16f * s, r.height - 8f * s), $"<color=#ffd27f>{_notice}</color>");
            }
            finally { skin.label.fontSize = old; }
        }
    }
}
