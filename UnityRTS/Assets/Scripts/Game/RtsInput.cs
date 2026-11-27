using System.Collections.Generic;
using UnityEngine;

namespace Rts.Game
{
    /// <summary>
    /// Input via IMGUI events, so the project works with either the legacy Input Manager or the new Input System
    /// (UnityEngine.Input would throw when only the new system is active). Events seen in OnGUI during frame N are
    /// consumed by Update in frame N+1 and cleared in LateUpdate.
    /// </summary>
    public sealed class RtsInput : MonoBehaviour
    {
        public static RtsInput Instance { get; private set; }

        /// <summary>Mouse position in screen pixels, origin bottom-left (like Camera.ScreenPointToRay).</summary>
        public Vector2 MousePos { get; private set; }
        public Vector2 MouseGuiPos { get; private set; }     // origin top-left (IMGUI space)
        public bool LeftDown, LeftUp, RightDown;
        public bool ShiftHeld, CtrlHeld;
        public float Scroll;
        readonly HashSet<KeyCode> _held = new HashSet<KeyCode>();
        readonly HashSet<KeyCode> _pressed = new HashSet<KeyCode>();

        /// <summary>Screen-space (top-left origin) rectangles of HUD panels; world clicks inside them are ignored.
        /// Panels register in OnGUI; the list is swapped in LateUpdate (one frame of latency).</summary>
        public static readonly List<Rect> UiBlockers = new List<Rect>();
        static readonly List<Rect> NextBlockers = new List<Rect>();
        public static void AddBlocker(Rect screenRectTopLeft) { NextBlockers.Add(screenRectTopLeft); }
        public bool OverUi => IsOverUi(MouseGuiPos);
        public static bool IsOverUi(Vector2 guiPos) { foreach (var r in UiBlockers) if (r.Contains(guiPos)) return true; return false; }

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public bool Held(KeyCode k) => _held.Contains(k);
        public bool Pressed(KeyCode k) => _pressed.Contains(k);

        void OnGUI()
        {
            var e = Event.current;
            MouseGuiPos = e.mousePosition;
            MousePos = new Vector2(e.mousePosition.x, Screen.height - e.mousePosition.y);
            ShiftHeld = e.shift; CtrlHeld = e.control;
            switch (e.rawType)
            {
                case EventType.MouseDown: if (e.button == 0) LeftDown = true; else if (e.button == 1) RightDown = true; break;
                case EventType.MouseUp: if (e.button == 0) LeftUp = true; break;
                case EventType.KeyDown:
                    if (e.keyCode != KeyCode.None) { if (_held.Add(e.keyCode)) _pressed.Add(e.keyCode); }
                    break;
                case EventType.KeyUp: _held.Remove(e.keyCode); break;
                case EventType.ScrollWheel: Scroll += -e.delta.y; break;
            }
        }

        void OnApplicationFocus(bool focus) { if (!focus) _held.Clear(); }

        void LateUpdate()
        {
            UiBlockers.Clear(); UiBlockers.AddRange(NextBlockers); NextBlockers.Clear();
            LeftDown = LeftUp = RightDown = false;
            Scroll = 0f;
            _pressed.Clear();
        }
    }
}
