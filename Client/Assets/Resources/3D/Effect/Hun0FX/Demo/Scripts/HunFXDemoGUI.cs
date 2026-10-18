using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hun0FX.Demo
{
    /// <summary>Shared IMGUI look for the demo scenes (no UI packages needed).</summary>
    public static class DemoGUI
    {
        static GUIStyle box, title, label, small, item, itemOn;
        public static float Scale => Mathf.Clamp(Screen.height / 900f, 0.75f, 2f);

        public static void Begin()
        {
            if (box == null)
            {
                var bg = new Texture2D(1, 1); bg.SetPixel(0, 0, new Color(0.06f, 0.065f, 0.075f, 0.86f)); bg.Apply();
                var sel = new Texture2D(1, 1); sel.SetPixel(0, 0, new Color(1f, 0.55f, 0.24f, 0.9f)); sel.Apply();
                box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 10, 10) }; box.normal.background = bg;
                title = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold }; title.normal.textColor = Color.white;
                label = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true }; label.normal.textColor = new Color(0.86f, 0.88f, 0.9f);
                small = new GUIStyle(label) { fontSize = 11 }; small.normal.textColor = new Color(0.6f, 0.63f, 0.68f);
                item = new GUIStyle(GUI.skin.label) { fontSize = 12, padding = new RectOffset(6, 6, 2, 2) }; item.normal.textColor = new Color(0.8f, 0.82f, 0.85f);
                itemOn = new GUIStyle(item); itemOn.normal.background = sel; itemOn.normal.textColor = Color.black;
            }
            float s = Scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        }

        public static GUIStyle Box => box;
        public static GUIStyle Title => title;
        public static GUIStyle Label => label;
        public static GUIStyle Small => small;
        public static GUIStyle Item => item;
        public static GUIStyle ItemOn => itemOn;
        public static float W => Screen.width / Scale;
        public static float H => Screen.height / Scale;

        public static float Slider(string name, float v, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Width(70));
            v = GUILayout.HorizontalSlider(v, min, max, GUILayout.Width(150));
            GUILayout.Label(v.ToString("0.00"), small, GUILayout.Width(36));
            GUILayout.EndHorizontal();
            return v;
        }

        /// <summary>The effects are authored for HDR + bloom. Returns a warning when that is off.
        /// Works in Built-in and URP projects: URP settings are read by reflection, so no URP package is required.</summary>
        public static string RenderWarning(Camera cam)
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null)
            {
                if (cam != null && !cam.allowHDR) return "HDR is off on the camera: effects will look flat. Turn on Allow HDR, and add Bloom (Post Processing package) for the intended look.";
                return null;
            }
            if (rp.GetType().Name.IndexOf("Universal", System.StringComparison.Ordinal) < 0) return null;
            var hdr = rp.GetType().GetProperty("supportsHDR", BindingFlags.Public | BindingFlags.Instance);
            if (hdr != null && hdr.GetValue(rp) is bool on && !on)
                return "HDR is off in the URP asset: effects will look flat. Turn on HDR (Quality > HDR) in " + rp.name + ".";
            if (cam != null)
            {
                foreach (var c in cam.GetComponents<Component>())
                {
                    if (c == null || c.GetType().Name != "UniversalAdditionalCameraData") continue;
                    var pp = c.GetType().GetProperty("renderPostProcessing", BindingFlags.Public | BindingFlags.Instance);
                    if (pp != null && pp.GetValue(c) is bool post && !post) return "Post Processing is off on the camera: no bloom.";
                }
            }
            return null;
        }
    }
}
