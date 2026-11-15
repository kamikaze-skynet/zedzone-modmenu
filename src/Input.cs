using System;
using System.Collections.Generic;
using UnityEngine;
using Key = UnityEngine.InputSystem.Key;

namespace ModMenu
{
    // Keyboard / mouse through the Input System (what the game uses), with legacy Input as fallback.
    internal static class In
    {
        // Keys we can type into a text/number box: (key, normal char, shifted char)
        internal static readonly List<(Key, char, char)> TypeKeys = new List<(Key, char, char)>();
        // Input System key -> KeyCode, limited to keys the mods can actually detect
        // (they map KeyCode -> Key by name, plus digits / Enter / backquote).
        static readonly Dictionary<Key, KeyCode> Bindable = new Dictionary<Key, KeyCode>();
        static bool _legacyBroken;

        static In()
        {
            for (char c = 'a'; c <= 'z'; c++)
                TypeKeys.Add(((Key)Enum.Parse(typeof(Key), c.ToString().ToUpperInvariant()), c, char.ToUpperInvariant(c)));
            string shiftedDigits = ")!@#$%^&*(";
            for (int d = 0; d <= 9; d++)
            {
                TypeKeys.Add(((Key)Enum.Parse(typeof(Key), "Digit" + d), (char)('0' + d), shiftedDigits[d]));
                TypeKeys.Add(((Key)Enum.Parse(typeof(Key), "Numpad" + d), (char)('0' + d), (char)('0' + d)));
            }
            TypeKeys.Add((Key.Space, ' ', ' '));
            TypeKeys.Add((Key.Minus, '-', '_'));
            TypeKeys.Add((Key.NumpadMinus, '-', '-'));
            TypeKeys.Add((Key.Period, '.', '>'));
            TypeKeys.Add((Key.NumpadPeriod, '.', '.'));
            TypeKeys.Add((Key.Comma, ',', '<'));
            TypeKeys.Add((Key.Semicolon, ';', ':'));
            TypeKeys.Add((Key.Slash, '/', '?'));
            TypeKeys.Add((Key.Backslash, '\\', '|'));
            TypeKeys.Add((Key.Equals, '=', '+'));

            foreach (Key k in Enum.GetValues(typeof(Key)))
            {
                if (k == Key.None || k == Key.Escape) continue;
                string n = k.ToString();
                if (n.StartsWith("Digit") && n.Length == 6) Bindable[k] = KeyCode.Alpha0 + (n[5] - '0');
                else if (k == Key.Enter) Bindable[k] = KeyCode.Return;
                else if (k == Key.Backquote) Bindable[k] = KeyCode.BackQuote;
                else if (Enum.TryParse(n, out KeyCode kc)) Bindable[k] = kc;
            }
        }

        internal static bool KeyDown(Key k)
        {
            try { var kb = UnityEngine.InputSystem.Keyboard.current; return kb != null && kb[k].wasPressedThisFrame; }
            catch { return false; }
        }

        internal static bool KeyHeld(Key k)
        {
            try { var kb = UnityEngine.InputSystem.Keyboard.current; return kb != null && kb[k].isPressed; }
            catch { return false; }
        }

        internal static bool Shift => KeyHeld(Key.LeftShift) || KeyHeld(Key.RightShift);
        internal static bool Ctrl => KeyHeld(Key.LeftCtrl) || KeyHeld(Key.RightCtrl);

        internal static bool Down(KeyCode key)
        {
            var k = Map(key);
            if (k != Key.None && UnityEngine.InputSystem.Keyboard.current != null) return KeyDown(k);
            if (_legacyBroken) return false;
            try { return Input.GetKeyDown(key); } catch { _legacyBroken = true; return false; }
        }

        static Key Map(KeyCode key)
        {
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return (Key)Enum.Parse(typeof(Key), "Digit" + (key - KeyCode.Alpha0));
            if (key == KeyCode.Return) return Key.Enter;
            if (key == KeyCode.BackQuote) return Key.Backquote;
            return Enum.TryParse(key.ToString(), out Key k) ? k : Key.None;
        }

        // For "press a key to bind": the key pressed this frame, if any.
        // unusable = a key was pressed that the mods couldn't detect (numpad, Ctrl, ...).
        internal static bool AnyBindableDown(out KeyCode code, out bool unusable)
        {
            code = KeyCode.None; unusable = false;
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb == null) return false;
                foreach (var kv in Bindable)
                {
                    bool down;
                    try { down = kb[kv.Key].wasPressedThisFrame; } catch { continue; }
                    if (!down) continue;
                    code = kv.Value; return true;
                }
                if (kb.anyKey.wasPressedThisFrame && !kb[Key.Escape].wasPressedThisFrame) unusable = true;
            }
            catch { }
            return false;
        }

        // Mouse position in GUI coordinates (origin top-left).
        internal static Vector2 MouseGui()
        {
            Vector2 p;
            try
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                p = mouse != null ? mouse.position.ReadValue() : (Vector2)Input.mousePosition;
            }
            catch { p = Vector2.zero; }
            return new Vector2(p.x, Screen.height - p.y);
        }

        internal static bool Click() => Button(0);
        internal static bool RightClick() => Button(1);

        static bool Button(int b)
        {
            try
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null) return (b == 0 ? mouse.leftButton : mouse.rightButton).wasPressedThisFrame;
                return Input.GetMouseButtonDown(b);
            }
            catch { return false; }
        }

        internal static float Wheel()
        {
            try
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null) return mouse.scroll.ReadValue().y;
                return Input.mouseScrollDelta.y;
            }
            catch { return 0f; }
        }
    }
}
