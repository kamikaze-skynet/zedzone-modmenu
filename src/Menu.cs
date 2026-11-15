using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Key = UnityEngine.InputSystem.Key;

namespace ModMenu
{
    // The menu window. Drawn with plain GUI boxes and labels; clicks, scrolling and typing are
    // read from the Input System in Update and hit-tested here, because the game's build strips
    // most of Unity's immediate-mode widgets (no text fields, toggles or scroll views).
    internal static class Menu
    {
        static bool _open;
        static float _closedAt = -10f;
        internal static int BlockHits;
        internal static bool BlocksGameInput => _open || Time.unscaledTime - _closedAt < 0.25f;

        const int KeysPage = -1;
        static int _page = KeysPage;           // -1 = every mod's keys, else index into _mods
        static int _scroll, _modScroll;
        static readonly List<ModInfo> _mods = new List<ModInfo>();
        static readonly List<Row> _rows = new List<Row>();

        static Setting _editing;               // number / text being typed
        static string _buffer = "";
        static Setting _capturing;             // waiting for a key press
        static Setting _hover;
        static string _status = "";
        static float _statusUntil;

        sealed class Row
        {
            internal string Header;            // section / mod title row
            internal Setting S;
            internal Rect R, Minus, Val, Plus, Reset;
        }

        // layout (screen pixels, recomputed every frame)
        static float S;
        static Rect _win, _left, _right, _desc, _btnReload, _btnClose, _keysBtn;
        static readonly List<Rect> _modRects = new List<Rect>();
        static int _visibleRows, _visibleMods;
        static float RowH => 28f * S;

        // ------------------------------------------------------------------ open / close
        internal static void Close()
        {
            if (_open) { _closedAt = Time.unscaledTime; MLog.Debug($"Menu closed (game input checks blocked: {BlockHits})"); }
            _open = false; BlockHits = 0; _editing = null; _capturing = null;
        }

        static void Open()
        {
            _open = true;
            Registry.Refresh();
            RebuildMods();
            if (_page >= _mods.Count) _page = KeysPage;
            Rebuild();
            MLog.Debug("Menu opened");
        }

        static void Status(string s) { _status = s; _statusUntil = Time.unscaledTime + 6f; }

        static void RebuildMods()
        {
            _mods.Clear();
            _mods.AddRange(Registry.Visible);
        }

        static void Rebuild()
        {
            _rows.Clear();
            if (_page == KeysPage)
            {
                foreach (var g in Registry.AllKeys.GroupBy(s => s.Mod))
                {
                    _rows.Add(new Row { Header = g.Key.Name });
                    foreach (var s in g) _rows.Add(new Row { S = s });
                }
            }
            else if (_page >= 0 && _page < _mods.Count)
            {
                string section = null;
                foreach (var s in _mods[_page].Settings)
                {
                    if (s.Section != section) { section = s.Section; _rows.Add(new Row { Header = section }); }
                    _rows.Add(new Row { S = s });
                }
            }
            _scroll = Mathf.Clamp(_scroll, 0, Math.Max(0, _rows.Count - Math.Max(1, _visibleRows)));
        }

        static void GoTo(int page) { _page = page; _scroll = 0; _editing = null; _capturing = null; Rebuild(); }

        // ------------------------------------------------------------------ update (input)
        internal static void Tick()
        {
            if (_capturing != null) { TickCapture(); return; }
            if (_editing != null) { if (TickEdit()) return; }
            else if (In.Down(Plugin.MenuKey.Value)) { if (_open) Close(); else Open(); return; }
            if (!_open) return;

            if (_editing == null && In.KeyDown(Key.Escape)) { Close(); return; }

            Vector2 m = In.MouseGui();
            float wheel = In.Wheel();
            if (wheel != 0f)
            {
                if (_right.Contains(m)) _scroll = Mathf.Clamp(_scroll + (wheel > 0 ? -3 : 3), 0, Math.Max(0, _rows.Count - _visibleRows));
                else if (_left.Contains(m)) _modScroll = Mathf.Clamp(_modScroll + (wheel > 0 ? -2 : 2), 0, Math.Max(0, _mods.Count - _visibleMods));
            }

            bool left = In.Click(), right = In.RightClick();
            if (!left && !right) return;
            if (_editing != null) Commit();     // clicking anywhere else finishes typing

            if (left && _keysBtn.Contains(m)) { GoTo(KeysPage); return; }
            for (int i = 0; i < _modRects.Count; i++)
                if (_modRects[i].Contains(m) && left) { GoTo(_modScroll + i); return; }
            if (left && _btnReload.Contains(m)) { Reload(); return; }
            if (left && _btnClose.Contains(m)) { Close(); return; }

            foreach (var r in _rows)
            {
                if (r.S == null || r.R.width <= 0f || !r.R.Contains(m)) continue;
                ClickRow(r, m, right);
                return;
            }
        }

        static void ClickRow(Row r, Vector2 m, bool right)
        {
            var s = r.S;
            if (r.Reset.Contains(m) && !right)
            {
                if (s.Set(s.Entry.DefaultValue, out var err)) Status($"{s.Name} reset to {s.Value}");
                else Status($"Couldn't reset {s.Name}: {err}");
                return;
            }
            switch (s.Kind)
            {
                case Kind.Bool:
                    if (r.Val.Contains(m)) s.Set(!(bool)s.Entry.BoxedValue, out _);
                    break;
                case Kind.Key:
                case Kind.Shortcut:
                    if (!r.Val.Contains(m)) break;
                    if (right) { s.SetKey(KeyCode.None, out _); Status($"{s.Name} cleared (no key)"); }
                    else { _capturing = s; Status($"Press the new key for {s.Name}. Esc cancels."); }
                    break;
                case Kind.Enum:
                case Kind.Choice:
                    if (r.Minus.Contains(m)) s.Cycle(-1);
                    else if (r.Val.Contains(m)) s.Cycle(right ? -1 : 1);
                    break;
                case Kind.Int:
                case Kind.Float:
                    if (r.Minus.Contains(m)) s.Nudge(-1, In.Shift);
                    else if (r.Plus.Contains(m)) s.Nudge(1, In.Shift);
                    else if (r.Val.Contains(m) && !right) { _editing = s; _buffer = s.Value; }
                    break;
                case Kind.Text:
                    if (r.Val.Contains(m) && !right) { _editing = s; _buffer = s.Value; }
                    break;
            }
        }

        static void TickCapture()
        {
            if (In.KeyDown(Key.Escape) || In.Click() || In.RightClick()) { _capturing = null; Status("Key unchanged"); return; }
            if (In.AnyBindableDown(out var code, out bool unusable))
            {
                var s = _capturing;
                _capturing = null;
                if (s.SetKey(code, out var err))
                {
                    var clash = Registry.ClashesWith(s);
                    Status(clash.Count == 0 ? $"{s.Mod.Name}: {s.Name} = {code}"
                                            : $"{s.Mod.Name}: {s.Name} = {code}, but that key is also {string.Join(", ", clash)}");
                }
                else Status($"Couldn't set {s.Name}: {err}");
            }
            else if (unusable) Status("The mods can't read that key (numpad, Ctrl and a few others). Pick another, or Esc to cancel.");
        }

        // returns true when the key press was used by the text box
        static bool TickEdit()
        {
            if (In.KeyDown(Key.Escape)) { _editing = null; Status("Edit cancelled"); return true; }
            if (In.KeyDown(Key.Enter) || In.KeyDown(Key.NumpadEnter)) { Commit(); return true; }
            if (In.KeyDown(Key.Backspace) && _buffer.Length > 0)
                _buffer = In.Ctrl ? "" : _buffer.Substring(0, _buffer.Length - 1);
            bool shift = In.Shift;
            bool numeric = _editing.Kind != Kind.Text;
            foreach (var (k, c, sc) in In.TypeKeys)
            {
                if (!In.KeyDown(k)) continue;
                char ch = shift ? sc : c;
                if (numeric && !(char.IsDigit(ch) || ch == '.' || ch == '-' || ch == ',')) continue;
                if (numeric && ch == ',') ch = '.';
                if (_buffer.Length < 200) _buffer += ch;
            }
            return false;
        }

        static void Commit()
        {
            var s = _editing;
            _editing = null;
            if (s == null) return;
            if (!s.Parse(_buffer, out var v)) { Status($"'{_buffer}' isn't a valid value for {s.Name}"); return; }
            if (s.Set(v, out var err)) Status($"{s.Name} = {s.Value}");
            else Status($"Couldn't set {s.Name}: {err}");
        }

        static void Reload()
        {
            int n = 0;
            foreach (var mod in Registry.Mods)
            {
                try { mod.Config.Reload(); n++; }
                catch (Exception e) { MLog.Error($"Reloading {mod.Name}'s config failed", e); }
            }
            Registry.Refresh(true);
            RebuildMods();
            if (_page >= _mods.Count) _page = KeysPage;
            Rebuild();
            Status($"Re-read {n} config files from BepInEx\\config");
        }

        // ------------------------------------------------------------------ layout + draw
        static void Layout()
        {
            S = Mathf.Clamp(Screen.height / 1080f, 0.6f, 3f) * Mathf.Clamp(Plugin.UiScale.Value, 0.5f, 2.5f);
            float w = Mathf.Min(1060f * S, Screen.width - 20f), h = Mathf.Min(720f * S, Screen.height - 20f);
            _win = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            float pad = 10f * S, top = _win.y + 36f * S, bottom = _win.yMax - 40f * S;
            float leftW = 250f * S;

            _left = new Rect(_win.x + pad, top, leftW, bottom - top);
            _keysBtn = new Rect(_left.x, _left.y, leftW, 32f * S);
            _modRects.Clear();
            float my = _keysBtn.yMax + 10f * S, mh = 30f * S;
            _visibleMods = Math.Max(1, (int)((_left.yMax - my) / (mh + 3f * S)));
            _modScroll = Mathf.Clamp(_modScroll, 0, Math.Max(0, _mods.Count - _visibleMods));
            for (int i = 0; i < _visibleMods && _modScroll + i < _mods.Count; i++)
                _modRects.Add(new Rect(_left.x, my + i * (mh + 3f * S), leftW, mh));

            float rx = _left.xMax + pad;
            _desc = new Rect(rx, bottom - 72f * S, _win.xMax - pad - rx, 72f * S);
            _right = new Rect(rx, top + 30f * S, _desc.width, _desc.y - 6f * S - (top + 30f * S));
            _visibleRows = Math.Max(1, (int)(_right.height / RowH));

            float by = _win.yMax - 34f * S, bw = 150f * S;
            _btnClose = new Rect(_win.xMax - pad - bw, by, bw, 28f * S);
            _btnReload = new Rect(_btnClose.x - 8f * S - bw, by, bw, 28f * S);
        }

        internal static void Draw()
        {
            if (!_open) return;
            Layout();
            var skin = GUI.skin;
            int oldLabel = skin.label.fontSize, oldBox = skin.box.fontSize;
            var oldAlign = skin.label.alignment;
            try
            {
                int fs = Mathf.RoundToInt(14f * S);
                skin.label.fontSize = fs; skin.box.fontSize = fs;
                Vector2 m = In.MouseGui();

                GUI.Box(_win, ""); GUI.Box(_win, ""); GUI.Box(_win, "");   // stacked for a darker backing
                GUI.Label(new Rect(_win.x + 10f * S, _win.y + 7f * S, _win.width - 20f * S, 24f * S),
                    $"<b>Mod Menu</b>   <color=#aaaaaa>{Plugin.MenuKey.Value} / Esc to close   ·   click a value to change it, Shift+click -/+ for big steps</color>");

                // left: keys page + mod list
                int clashes = Registry.ClashCount();
                Button(_keysBtn, clashes > 0 ? $"All keys  <color=#ff6b6b>({clashes} clash{(clashes == 1 ? "" : "es")})</color>" : "All keys", _page == KeysPage, m);
                for (int i = 0; i < _modRects.Count; i++)
                {
                    var mod = _mods[_modScroll + i];
                    bool modClash = mod.Settings.Any(s => (s.Kind == Kind.Key || s.Kind == Kind.Shortcut) && Registry.ClashesWith(s).Count > 0);
                    string label = (mod.Own ? mod.Name : $"<color=#bbbbbb>{mod.Name}</color>") + (modClash ? " <color=#ff6b6b>!</color>" : "");
                    Button(_modRects[i], label, _page == _modScroll + i, m, TextAnchor.MiddleLeft);
                }
                if (_mods.Count > _visibleMods)
                    GUI.Label(new Rect(_left.x, _left.yMax - 4f * S, _left.width, 22f * S), $"<color=#888888>{_modScroll + 1}-{Math.Min(_mods.Count, _modScroll + _visibleMods)} of {_mods.Count}, scroll for more</color>");

                // right: page title
                string title;
                if (_page == KeysPage) title = "<b>All keys</b>   <color=#aaaaaa>every mod's hotkeys in one place; red = used twice</color>";
                else { var md = _mods[_page]; title = $"<b>{md.Name}</b> {md.Version}   <color=#888888>{md.Guid}.cfg</color>"; }
                GUI.Label(new Rect(_right.x, _right.y - 30f * S, _right.width, 26f * S), title);

                GUI.Box(_right, "");
                _hover = null;
                foreach (var r in _rows) r.R = default;
                if (_rows.Count == 0)
                    GUI.Label(Inset(new Rect(_right.x, _right.y, _right.width, RowH)), _page == KeysPage ? "No mod has a hotkey setting." : "This mod has no settings.");
                for (int i = 0; i < _visibleRows && _scroll + i < _rows.Count; i++)
                    DrawRow(_rows[_scroll + i], new Rect(_right.x + 2f * S, _right.y + i * RowH, _right.width - 4f * S, RowH), m);
                if (_rows.Count > _visibleRows)
                {
                    skin.label.alignment = TextAnchor.MiddleRight;
                    GUI.Label(new Rect(_right.x, _right.y - 30f * S, _right.width - 6f * S, 26f * S),
                        $"<color=#888888>{_scroll + 1}-{Math.Min(_rows.Count, _scroll + _visibleRows)} of {_rows.Count}, scroll for more</color>");
                    skin.label.alignment = oldAlign;
                }

                // description of the hovered (or edited) setting
                GUI.Box(_desc, "");
                var d = _capturing ?? _editing ?? _hover;
                GUI.Label(Inset(_desc), d != null ? Describe(d)
                    : "<color=#aaaaaa>" + Wrap("Point at a setting to see what it does. Changes are saved to the mod's .cfg file at once. Most take effect right away; a few only when the game restarts.", _desc.width) + "</color>");

                Button(_btnReload, "Re-read files", false, m);
                Button(_btnClose, "Close", false, m);

                if (_status.Length > 0 && Time.unscaledTime < _statusUntil)
                {
                    var sr = new Rect(_win.x, _win.yMax + 4f * S, _win.width, 28f * S);
                    GUI.Box(sr, ""); GUI.Box(sr, "");
                    GUI.Label(Inset(sr), _status);
                }
            }
            finally
            {
                skin.label.fontSize = oldLabel; skin.box.fontSize = oldBox;
                skin.label.alignment = oldAlign;
            }
        }

        static void DrawRow(Row r, Rect row, Vector2 m)
        {
            if (r.Header != null)
            {
                GUI.Label(Inset(row), $"<b><color=#ffd27f>{r.Header}</color></b>");
                return;
            }
            var s = r.S;
            r.R = row;
            bool hot = row.Contains(m);
            if (hot) { GUI.Box(row, ""); _hover = s; }

            float W = row.width, x = row.x, y = row.y + 2f * S, h = row.height - 4f * S;
            float bw = 24f * S;
            r.Minus = new Rect(x + W * 0.46f, y, bw, h);
            r.Val = new Rect(x + W * 0.46f, y, W * 0.36f, h);
            r.Plus = new Rect(r.Val.xMax - bw, y, bw, h);
            r.Reset = new Rect(x + W * 0.84f, y, W * 0.16f - 4f * S, h);

            string name = s.Name + (s.IsDefault ? "" : " <color=#7fd4ff>*</color>");
            GUI.Label(Inset(new Rect(x, row.y, W * 0.45f, row.height)), name);

            switch (s.Kind)
            {
                case Kind.Bool:
                    bool on = (bool)s.Entry.BoxedValue;
                    Button(r.Val, on ? "<color=#8fe08f><b>On</b></color>" : "<color=#ff9f7f>Off</color>", on, m);
                    break;
                case Kind.Key:
                case Kind.Shortcut:
                    string kt = _capturing == s ? "<color=#ffd27f>press a key...</color>" : s.Value;
                    if (_capturing != s && Registry.ClashesWith(s).Count > 0) kt = $"<color=#ff6b6b><b>{kt}</b>  (clash)</color>";
                    Button(r.Val, kt, _capturing == s, m);
                    break;
                case Kind.Enum:
                case Kind.Choice:
                    Button(r.Minus, "<", false, m);
                    Button(r.Plus, ">", false, m);
                    Label(Mid(r), Fit(s.Value, Mid(r).width), TextAnchor.MiddleCenter);
                    break;
                case Kind.Int:
                case Kind.Float:
                    Button(r.Minus, "-", false, m);
                    Button(r.Plus, "+", false, m);
                    var mid = Mid(r);
                    GUI.Box(mid, ""); if (_editing == s) GUI.Box(mid, "");
                    Label(mid, _editing == s ? _buffer + Caret() : s.Value, TextAnchor.MiddleCenter);
                    break;
                case Kind.Text:
                    GUI.Box(r.Val, ""); if (_editing == s) GUI.Box(r.Val, "");
                    Label(Inset(r.Val), _editing == s ? Fit(_buffer, r.Val.width, true) + Caret() : Fit(s.Value.Length == 0 ? "<color=#888888>(empty)</color>" : s.Value, r.Val.width), TextAnchor.MiddleLeft);
                    break;
                default:
                    Label(r.Val, $"<color=#aaaaaa>{Fit(s.Value, r.Val.width)}</color>", TextAnchor.MiddleCenter);
                    break;
            }
            if (s.Kind != Kind.Other)
            {
                if (s.IsDefault) Label(r.Reset, "<color=#666666>default</color>", TextAnchor.MiddleCenter);
                else Button(r.Reset, "Default", false, m);
            }
        }

        static Rect Mid(Row r) => new Rect(r.Minus.xMax + 3f * S, r.Val.y, r.Plus.x - r.Minus.xMax - 6f * S, r.Val.height);

        static string Describe(Setting s)
        {
            var bits = new List<string> { $"default {s.Show(s.Entry.DefaultValue)}" };
            if (s.HasRange) bits.Add($"{s.Show(s.Min)} to {s.Show(s.Max)}");
            if (s.Kind == Kind.Key || s.Kind == Kind.Shortcut) bits.Add("right-click the key to clear it");
            if (s.Kind == Kind.Enum || s.Kind == Kind.Choice) bits.Add("click to cycle, right-click to go back");
            string text = $"<b>{s.Mod.Name} / {s.Section} / {s.Name}</b>   <color=#aaaaaa>({string.Join(", ", bits)})</color>\n{Wrap(s.Description, _desc.width)}";
            var clash = Registry.ClashesWith(s);
            if (clash.Count > 0) text += $"\n<color=#ff6b6b>{s.Value} is also {string.Join(", ", clash)}.</color>";
            return text;
        }

        // Line-break plain text to a width by hand (word wrap on GUIStyle may be stripped from the game).
        static string Wrap(string t, float width)
        {
            if (string.IsNullOrEmpty(t)) return "";
            int max = Math.Max(20, (int)(width / (7.2f * S)));
            var sb = new System.Text.StringBuilder();
            foreach (var para in t.Replace("\r", "").Split('\n'))
            {
                int col = 0;
                foreach (var word in para.Split(' '))
                {
                    if (col > 0 && col + 1 + word.Length > max) { sb.Append('\n'); col = 0; }
                    else if (col > 0) { sb.Append(' '); col++; }
                    sb.Append(word); col += word.Length;
                }
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        static string Caret() => (int)(Time.unscaledTime * 2f) % 2 == 0 ? "|" : " ";

        // Cut a long value so it fits; keep the end while typing.
        static string Fit(string t, float width, bool keepEnd = false)
        {
            int max = Math.Max(4, (int)(width / (7.5f * S)));
            if (t == null || t.Length <= max || t.Contains("<color")) return t;
            return keepEnd ? "..." + t.Substring(t.Length - max + 3) : t.Substring(0, max - 3) + "...";
        }

        static Rect Inset(Rect r) => new Rect(r.x + 6f * S, r.y + 2f * S, r.width - 12f * S, r.height - 4f * S);

        static void Label(Rect r, string text, TextAnchor a)
        {
            var skin = GUI.skin;
            var old = skin.label.alignment;
            skin.label.alignment = a;
            GUI.Label(r, text);
            skin.label.alignment = old;
        }

        static void Button(Rect r, string text, bool on, Vector2 mouse, TextAnchor a = TextAnchor.MiddleCenter)
        {
            GUI.Box(r, "");
            if (on) { GUI.Box(r, ""); GUI.Box(r, ""); }
            else if (r.Contains(mouse)) GUI.Box(r, "");
            Label(a == TextAnchor.MiddleLeft ? Inset(r) : r, on ? $"<b>{text}</b>" : text, a);
        }
    }
}
