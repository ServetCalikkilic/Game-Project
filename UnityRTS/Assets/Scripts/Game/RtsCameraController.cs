using UnityEngine;

namespace Rts.Game
{
    /// <summary>WASD / arrow keys pan, mouse wheel zooms. Keys come from IMGUI events (RtsInput).</summary>
    public sealed class RtsCameraController : MonoBehaviour
    {
        public Camera Cam;
        public float PanSpeed = 45f;
        public float MinHeight = 14f, MaxHeight = 80f;

        public void FocusOn(Vector3 groundPoint)
        {
            float h = Mathf.Clamp(Cam.transform.position.y, MinHeight, MaxHeight);
            Cam.transform.position = groundPoint + new Vector3(0, h, -h * 0.7f);
        }

        void Update()
        {
            var input = RtsInput.Instance;
            if (Cam == null || input == null) return;
            var move = Vector3.zero;
            if (input.Held(KeyCode.W) || input.Held(KeyCode.UpArrow)) move.z += 1;
            if (input.Held(KeyCode.S) && input.CtrlHeld || input.Held(KeyCode.DownArrow)) move.z -= 1;   // plain S is "stop"; Ctrl+S or Down pans back
            if (input.Held(KeyCode.A) || input.Held(KeyCode.LeftArrow)) move.x -= 1;
            if (input.Held(KeyCode.D) || input.Held(KeyCode.RightArrow)) move.x += 1;

            var t = Cam.transform;
            float h = t.position.y;
            t.position += move.normalized * PanSpeed * (h / 45f) * Time.unscaledDeltaTime;

            if (Mathf.Abs(input.Scroll) > 0.01f && !input.OverUi)
            {
                float newH = Mathf.Clamp(h - input.Scroll * 3f, MinHeight, MaxHeight);
                var p = t.position;
                t.position = new Vector3(p.x, newH, p.z);
            }
            var pos = t.position;
            t.position = new Vector3(Mathf.Clamp(pos.x, -120, 120), pos.y, Mathf.Clamp(pos.z, -150, 100));
        }
    }
}
