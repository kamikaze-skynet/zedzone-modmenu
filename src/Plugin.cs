using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace ModMenu
{
    // One in-game menu for every mod's BepInEx settings. Other mods need no code for it:
    // the menu reads each loaded plugin's ConfigFile, and writing a value there saves the
    // .cfg file at once (mods that read .Value each time pick the change up straight away).
    [BepInPlugin(Guid, "Mod Menu", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "girthy_mods.zedzone.modmenu";
        public const string Version = "1.0.0";

        // Mods made by us. They are listed first; other people's mods come after them.
        internal const string OwnPrefix = "girthy_mods.";

        internal static ConfigEntry<KeyCode> MenuKey;
        internal static ConfigEntry<float> UiScale;
        internal static ConfigEntry<bool> ShowOtherMods;
        internal static ConfigEntry<bool> WarnKeyClashes;
        internal static ConfigEntry<bool> VerboseLog;

        public override void Load()
        {
            MenuKey = Config.Bind("Keys", "OpenMenuKey", KeyCode.F11,
                "Opens and closes the Mod Menu.");
            UiScale = Config.Bind("Display", "UiScale", 1f,
                new ConfigDescription("Size of the menu. 1 = sized for the screen height, 1.25 = a quarter bigger.",
                    new AcceptableValueRange<float>(0.5f, 2.5f)));
            ShowOtherMods = Config.Bind("Display", "ShowOtherMods", true,
                "Also list mods by other authors (anything that has a BepInEx config). Off = only our girthy_mods mods.");
            WarnKeyClashes = Config.Bind("Keys", "WarnKeyClashes", true,
                "When the game starts, show a short notice if two mods use the same key.");
            VerboseLog = Config.Bind("Debug", "VerboseLog", false, "Extra detail in BepInEx\\ModMenu.log.");

            MLog.Verbose = VerboseLog.Value;
            MLog.Open(Log, Version);
            MLog.Info($"Config: MenuKey={MenuKey.Value} UiScale={UiScale.Value} ShowOtherMods={ShowOtherMods.Value} WarnKeyClashes={WarnKeyClashes.Value}");

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<MenuBehaviour>();
                var go = new GameObject("ModMenu");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<MenuBehaviour>();
            }
            catch (Exception e) { MLog.Error("Couldn't create the menu object; the menu is off", e); return; }

            Patches.Apply(new Harmony(Guid));
            MLog.Info($"Mod Menu {Version} loaded. Press {MenuKey.Value} in game.");
        }
    }

    public class MenuBehaviour : MonoBehaviour
    {
        public MenuBehaviour(IntPtr ptr) : base(ptr) { }

        float _startedAt = -1f;
        bool _startupCheckDone;

        void Update()
        {
            if (_startedAt < 0f) _startedAt = Time.unscaledTime;
            // Other mods may load after us, so look at everyone's keys a few seconds in.
            if (!_startupCheckDone && Time.unscaledTime - _startedAt > 5f)
            {
                _startupCheckDone = true;
                try { Startup.Check(); } catch (Exception e) { MLog.Error("Startup key check failed", e); }
            }
            try { Menu.Tick(); }
            catch (Exception e) { MLog.Error("Menu update failed", e); Menu.Close(); }
        }

        void OnGUI()
        {
            try { Startup.DrawNotice(); } catch { }
            try { Menu.Draw(); }
            catch (Exception e) { MLog.Error("Menu draw failed", e); Menu.Close(); }
        }
    }
}
