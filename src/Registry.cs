#pragma warning disable CS0618 // BepInEx's KeyCode copy is obsolete but a mod could still use it
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using BepInEx.Unity.IL2CPP.Configuration;
using BxKey = BepInEx.Unity.IL2CPP.UnityEngine.KeyCode;

namespace ModMenu
{
    internal enum Kind { Bool, Key, Shortcut, Enum, Int, Float, Text, Choice, Other }

    internal sealed class Setting
    {
        internal ModInfo Mod;
        internal ConfigEntryBase Entry;
        internal Kind Kind;
        internal string Section => Entry.Definition.Section;
        internal string Name => Entry.Definition.Key;
        internal string Description => Entry.Description?.Description ?? "";
        internal object[] Choices;          // enum values or an AcceptableValueList
        internal double Min = double.NaN, Max = double.NaN;
        internal bool HasRange => !double.IsNaN(Min) && !double.IsNaN(Max);

        internal string Show(object v)
        {
            if (v == null) return "";
            switch (Kind)
            {
                case Kind.Bool: return (bool)v ? "On" : "Off";
                case Kind.Float: return Convert.ToDouble(v, CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture);
                case Kind.Key: try { return In.KeyName((KeyCode)Convert.ToInt32(v)); } catch { return Convert.ToString(v, CultureInfo.InvariantCulture); }
                case Kind.Shortcut: return v.ToString() is string s && s.Length > 0 ? In.KeyNames(s) : "None";
                default: return Convert.ToString(v, CultureInfo.InvariantCulture);
            }
        }

        internal string Value => Show(Entry.BoxedValue);
        internal bool IsDefault => Equals(Entry.BoxedValue, Entry.DefaultValue) || Value == Show(Entry.DefaultValue);

        // The single key this setting binds (keys and one-key shortcuts), or None.
        internal KeyCode BoundKey
        {
            get
            {
                try
                {
                    if (Kind == Kind.Key) return (KeyCode)Convert.ToInt32(Entry.BoxedValue);
                    if (Kind == Kind.Shortcut) return (KeyCode)(int)((KeyboardShortcut)Entry.BoxedValue).MainKey;
                }
                catch { }
                return KeyCode.None;
            }
        }

        internal bool Set(object v, out string error)
        {
            error = null;
            try
            {
                var old = Entry.BoxedValue;
                Entry.BoxedValue = v;            // clamps to the acceptable range; saves the .cfg
                MLog.Info($"{Mod.Name}: [{Section}] {Name} = {Show(Entry.BoxedValue)} (was {Show(old)})");
                return true;
            }
            catch (Exception e) { error = e.Message; MLog.Error($"{Mod.Name}: couldn't set {Name}", e); return false; }
        }

        internal bool SetKey(KeyCode k, out string error)
        {
            // Enum.ToObject also covers a mod using BepInEx's own KeyCode copy (same numbers)
            if (Kind == Kind.Shortcut) return Set(new KeyboardShortcut(k), out error);
            return Set(Enum.ToObject(Entry.SettingType, (int)k), out error);
        }

        internal bool Parse(string text, out object v)
        {
            v = null;
            text = text.Trim();
            var ci = CultureInfo.InvariantCulture;
            try
            {
                switch (Kind)
                {
                    case Kind.Int:
                        if (!double.TryParse(text, NumberStyles.Float, ci, out var d)) return false;
                        v = Convert.ChangeType(Math.Round(d), Entry.SettingType, ci); return true;
                    case Kind.Float:
                        if (!double.TryParse(text, NumberStyles.Float, ci, out var f)) return false;
                        v = Convert.ChangeType(f, Entry.SettingType, ci); return true;
                    case Kind.Text:
                        v = text; return true;
                }
            }
            catch { }
            return false;
        }

        // A sensible +/- step for a number.
        internal double Step
        {
            get
            {
                if (HasRange)
                {
                    double s = (Max - Min) / 20.0;
                    if (Kind == Kind.Int) return Math.Max(1, Math.Round(s));
                    return Nice(s);
                }
                double cur = Math.Abs(Convert.ToDouble(Entry.BoxedValue, CultureInfo.InvariantCulture));
                if (Kind == Kind.Int) return cur >= 200 ? Math.Pow(10, Math.Floor(Math.Log10(cur)) - 1) : cur >= 20 ? 5 : 1;
                if (cur < 1) return 0.05;
                if (cur < 10) return 0.5;
                return Nice(cur / 10.0);
            }
        }

        static double Nice(double s)
        {
            if (s <= 0) return 0.1;
            double p = Math.Pow(10, Math.Floor(Math.Log10(s)));
            double m = s / p;
            return (m < 1.5 ? 1 : m < 3.5 ? 2 : m < 7.5 ? 5 : 10) * p;
        }

        internal void Nudge(int dir, bool big)
        {
            double step = Step * (big ? 10 : 1);
            double cur = Convert.ToDouble(Entry.BoxedValue, CultureInfo.InvariantCulture);
            double next = cur + dir * step;
            if (Kind == Kind.Float) next = Math.Round(next / step) * step;   // stay on the grid, no 0.30000004
            if (HasRange) next = Math.Max(Min, Math.Min(Max, next));
            if (Kind == Kind.Int) next = Math.Round(next);
            Set(Convert.ChangeType(next, Entry.SettingType, CultureInfo.InvariantCulture), out _);
        }

        internal void Cycle(int dir)
        {
            if (Choices == null || Choices.Length == 0) return;
            int i = Array.FindIndex(Choices, c => Equals(c, Entry.BoxedValue));
            i = ((i < 0 ? 0 : i + dir) % Choices.Length + Choices.Length) % Choices.Length;
            Set(Choices[i], out _);
        }
    }

    internal sealed class ModInfo
    {
        internal string Guid, Name, Version;
        internal ConfigFile Config;
        internal bool Own;
        internal readonly List<Setting> Settings = new List<Setting>();
    }

    // Collects every loaded BepInEx plugin that has settings.
    internal static class Registry
    {
        internal static readonly List<ModInfo> Mods = new List<ModInfo>();
        static int _pluginCount = -1;

        internal static void Refresh(bool force = false)
        {
            Dictionary<string, PluginInfo> plugins;
            try { plugins = IL2CPPChainloader.Instance?.Plugins; }
            catch (Exception e) { MLog.Error("Couldn't read the plugin list", e); return; }
            if (plugins == null) return;
            if (!force && plugins.Count == _pluginCount) return;
            _pluginCount = plugins.Count;

            Mods.Clear();
            foreach (var kv in plugins)
            {
                try
                {
                    var info = kv.Value;
                    if (!(info?.Instance is BasePlugin bp) || bp.Config == null) continue;
                    var mod = new ModInfo
                    {
                        Guid = info.Metadata.GUID,
                        Name = Clean(info.Metadata.Name),
                        Version = info.Metadata.Version?.ToString() ?? "",
                        Config = bp.Config,
                    };
                    mod.Own = mod.Guid.StartsWith(Plugin.OwnPrefix, StringComparison.OrdinalIgnoreCase);
                    foreach (var pair in (IEnumerable<KeyValuePair<ConfigDefinition, ConfigEntryBase>>)bp.Config)
                        mod.Settings.Add(Describe(mod, pair.Value));
                    if (mod.Settings.Count == 0) continue;
                    // keep each section together, in the order the mod declared them
                    var order = mod.Settings.Select(s => s.Section).Distinct().ToList();
                    var sorted = mod.Settings.OrderBy(s => order.IndexOf(s.Section)).ToList();
                    mod.Settings.Clear(); mod.Settings.AddRange(sorted);
                    Mods.Add(mod);
                }
                catch (Exception e) { MLog.Error($"Couldn't read the settings of {kv.Key}", e); }
            }
            Mods.Sort((a, b) =>
            {
                bool am = a.Guid == Plugin.Guid, bm = b.Guid == Plugin.Guid;   // Mod Menu itself goes last
                if (am != bm) return am ? 1 : -1;
                if (a.Own != b.Own) return a.Own ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            MLog.Info($"Found {Mods.Count} mods with settings: " + string.Join(", ", Mods.Select(m => $"{m.Name} {m.Version} ({m.Settings.Count})")));
        }

        internal static IEnumerable<ModInfo> Visible =>
            Mods.Where(m => m.Own || m.Guid == Plugin.Guid || Plugin.ShowOtherMods.Value);

        // "AmmoStacks (100-round ammo & arrow stacks, no weight)" -> "AmmoStacks"
        static string Clean(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            int p = name.IndexOf(" (", StringComparison.Ordinal);
            return p > 2 ? name.Substring(0, p) : name;
        }

        static Setting Describe(ModInfo mod, ConfigEntryBase e)
        {
            var s = new Setting { Mod = mod, Entry = e };
            var t = e.SettingType;
            var acc = e.Description?.AcceptableValues;
            if (t == typeof(bool)) s.Kind = Kind.Bool;
            else if (t == typeof(KeyCode) || t == typeof(BxKey)) s.Kind = Kind.Key;
            else if (t == typeof(KeyboardShortcut)) s.Kind = Kind.Shortcut;
            else if (t.IsEnum) { s.Kind = Kind.Enum; s.Choices = Enum.GetValues(t).Cast<object>().ToArray(); }
            else if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(uint)) s.Kind = Kind.Int;
            else if (t == typeof(float) || t == typeof(double) || t == typeof(decimal)) s.Kind = Kind.Float;
            else if (t == typeof(string)) s.Kind = Kind.Text;
            else s.Kind = Kind.Other;

            if (acc != null)
            {
                try
                {
                    var at = acc.GetType();
                    var list = at.GetProperty("AcceptableValues")?.GetValue(acc) as IEnumerable;
                    if (list != null) { s.Choices = list.Cast<object>().ToArray(); if (s.Kind != Kind.Enum) s.Kind = Kind.Choice; }
                    var min = at.GetProperty("MinValue")?.GetValue(acc);
                    var max = at.GetProperty("MaxValue")?.GetValue(acc);
                    if (min != null && max != null)
                    {
                        s.Min = Convert.ToDouble(min, CultureInfo.InvariantCulture);
                        s.Max = Convert.ToDouble(max, CultureInfo.InvariantCulture);
                    }
                }
                catch (Exception ex) { MLog.Debug($"{mod.Name}/{e.Definition.Key}: acceptable values unreadable: {ex.Message}"); }
            }
            return s;
        }

        // ------------------------------------------------------------------ keys
        internal static IEnumerable<Setting> AllKeys =>
            Visible.SelectMany(m => m.Settings).Where(s => s.Kind == Kind.Key || s.Kind == Kind.Shortcut);

        // Keys the game itself uses (it can't be rebound from here, but a mod shouldn't take them).
        internal static readonly Dictionary<KeyCode, string> GameKeys = new Dictionary<KeyCode, string>
        {
            { KeyCode.F5, "the game's quick save" },
            { KeyCode.F12, "Steam's screenshot key" },
            { KeyCode.Escape, "the game's menu" },
        };

        // Everything else bound to the same key as s ("DogShare: TakeBackDogKey", "the game's quick save").
        internal static List<string> ClashesWith(Setting s)
        {
            var list = new List<string>();
            var k = s.BoundKey;
            if (k == KeyCode.None) return list;
            if (GameKeys.TryGetValue(k, out var g)) list.Add(g);
            foreach (var o in AllKeys)
                if (o != s && o.BoundKey == k) list.Add($"{o.Mod.Name}: {o.Name}");
            return list;
        }

        internal static int ClashCount() => AllKeys.Count(s => ClashesWith(s).Count > 0);
    }
}
