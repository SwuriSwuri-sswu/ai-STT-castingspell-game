using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Hun0FX.Demo
{
    /// <summary>
    /// Plays the effects in <see cref="entries"/> one at a time around a capsule character.
    /// Left / Right arrows browse, Space replays. The tuning sliders drive each effect's HunFXController.
    /// </summary>
    public class HunFXShowcase : MonoBehaviour
    {
        public string title = "hun0fx";
        public Transform actor;
        public Transform target;
        public HunFXDemoCamera cameraRig;
        public List<DemoEntry> entries = new List<DemoEntry>();
        public bool autoAdvance;
        public float replayGap = 0.6f;

        int index;
        string packFilter = "All";
        readonly List<int> view = new List<int>();
        readonly List<string> packs = new List<string>();
        readonly List<GameObject> live = new List<GameObject>();
        readonly List<HunFXController> liveFx = new List<HunFXController>();
        float replayAt = float.MaxValue;
        Vector2 scroll;
        bool recolor; float hue = 0.75f, intensity = 1f, scale = 1f, tempo = 1f;
        Camera cam;

        void Start()
        {
            cam = cameraRig ? cameraRig.GetComponent<Camera>() : Camera.main;
            packs.Add("All");
            foreach (var e in entries) if (!string.IsNullOrEmpty(e.pack) && !packs.Contains(e.pack)) packs.Add(e.pack);
            Filter("All");
        }

        void Filter(string pack)
        {
            packFilter = pack; view.Clear();
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].prefab != null && (pack == "All" || entries[i].pack == pack)) view.Add(i);
            index = 0; scroll = Vector2.zero; PlayCurrent();
        }

        DemoEntry Current => view.Count == 0 ? null : entries[view[Mathf.Clamp(index, 0, view.Count - 1)]];

        public void Next(int d) { if (view.Count == 0) return; index = (index + d + view.Count) % view.Count; PlayCurrent(); }

        void Clear()
        {
            StopAllCoroutines();
            foreach (var g in live) if (g) Destroy(g);
            live.Clear(); liveFx.Clear();
        }

        void Track(HunFXController c)
        {
            if (c == null) return;
            live.Add(c.gameObject); liveFx.Add(c); Tune(c);
        }

        void Tune(HunFXController c)
        {
            c.recolor = recolor; c.targetColor = Color.HSVToRGB(hue, 0.85f, 1f);
            c.intensity = intensity; c.scale = scale; c.tempo = tempo; c.Apply();
        }

        public void PlayCurrent()
        {
            Clear();
            var e = Current; if (e == null) return;
            if (e.placement == DemoPlacement.Projectile) StartCoroutine(Fly(e));
            else if (e.placement == DemoPlacement.Orbit) StartCoroutine(Orbit(e));
            else Track(DemoPlace.Spawn(e.prefab, e.placement, actor, target, e.offset, e.euler));
            replayAt = e.loop ? (autoAdvance ? Time.time + 5f : float.MaxValue) : Time.time + e.duration / Mathf.Max(0.05f, tempo) + replayGap;
            if (cameraRig) cameraRig.Frame((actor.position + target.position) * 0.5f + Vector3.up, e.viewDistance);
        }

        IEnumerator Orbit(DemoEntry e)
        {
            var rot = DemoPlace.Flat(actor.rotation);
            var center = DemoPlace.Point(DemoPlacement.Actor, actor, target, e.offset);
            float r = e.endOffset.x > 0f ? e.endOffset.x : 2.2f, a = 0f;
            var c = HunFX.Spawn(e.prefab, center + rot * new Vector3(0f, 0f, r), rot * Quaternion.LookRotation(Vector3.right), null, autoDestroy: false);
            Track(c);
            while (c)
            {
                a += e.speed * tempo / r * Time.deltaTime;
                c.transform.position = center + rot * new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                c.transform.rotation = rot * Quaternion.LookRotation(new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a)));   // face along the path
                yield return null;
            }
        }

        IEnumerator Fly(DemoEntry e)
        {
            var rot = DemoPlace.Flat(actor.rotation);
            var start = DemoPlace.Point(DemoPlacement.Actor, actor, target, e.offset);
            var end = target.position + rot * e.endOffset;
            var c = HunFX.Spawn(e.prefab, start, rot * Quaternion.Euler(e.euler), null, autoDestroy: false);
            Track(c);
            var dir = end - start; float len = dir.magnitude, t = 0f;
            while (t < len)
            {
                t += e.speed * tempo * Time.deltaTime;
                c.transform.position = start + dir / Mathf.Max(len, 1e-4f) * Mathf.Min(t, len);
                yield return null;
            }
            c.Stop();
            if (e.hitOnArrive) Track(HunFX.Spawn(e.hitOnArrive, end, rot));
            replayAt = Time.time + 1.4f / Mathf.Max(0.05f, tempo) + replayGap;
        }

        void Update()
        {
            if (Time.time < replayAt) return;
            if (autoAdvance) Next(1); else PlayCurrent();
        }

        void OnGUI()
        {
            var ev = Event.current;
            if (ev.type == EventType.KeyDown)
            {
                if (ev.keyCode == KeyCode.RightArrow || ev.keyCode == KeyCode.DownArrow) { Next(1); ev.Use(); }
                else if (ev.keyCode == KeyCode.LeftArrow || ev.keyCode == KeyCode.UpArrow) { Next(-1); ev.Use(); }
                else if (ev.keyCode == KeyCode.Space) { PlayCurrent(); ev.Use(); }
            }

            DemoGUI.Begin();
            var e = Current;
            GUILayout.BeginArea(new Rect(16, 16, 300, DemoGUI.H - 32));
            GUILayout.BeginVertical(DemoGUI.Box);
            GUILayout.Label(title, DemoGUI.Title);
            if (packs.Count > 2)
            {
                int sel = packs.IndexOf(packFilter);
                int ns = GUILayout.SelectionGrid(sel, packs.ToArray(), 3);
                if (ns != sel) Filter(packs[ns]);
            }
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(36))) Next(-1);
            GUILayout.Label(view.Count == 0 ? "-" : $"{index + 1} / {view.Count}", DemoGUI.Label, GUILayout.Width(70));
            if (GUILayout.Button(">", GUILayout.Width(36))) Next(1);
            if (GUILayout.Button("Replay")) PlayCurrent();
            GUILayout.EndHorizontal();
            if (e != null)
            {
                GUILayout.Label(e.id, DemoGUI.Title);
                GUILayout.Label($"{e.category} · {e.element} · {(e.loop ? "loop" : e.duration.ToString("0.00") + " s")}", DemoGUI.Small);
                if (!string.IsNullOrEmpty(e.note)) GUILayout.Label(e.note, DemoGUI.Label);
            }
            autoAdvance = GUILayout.Toggle(autoAdvance, " Auto play next");
            GUILayout.Space(8);
            GUILayout.Label("HunFXController", DemoGUI.Small);
            bool changed = false;
            bool nr = GUILayout.Toggle(recolor, " Recolor"); changed |= nr != recolor; recolor = nr;
            if (recolor)
            {
                float nh = DemoGUI.Slider("Hue", hue, 0f, 1f); changed |= nh != hue; hue = nh;
                var r = GUILayoutUtility.GetRect(260, 8); GUI.color = Color.HSVToRGB(hue, 0.85f, 1f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
            }
            float ni = DemoGUI.Slider("Intensity", intensity, 0.3f, 2f); changed |= ni != intensity; intensity = ni;
            float ns2 = DemoGUI.Slider("Scale", scale, 0.25f, 2f); changed |= ns2 != scale; scale = ns2;
            float nt = DemoGUI.Slider("Tempo", tempo, 0.5f, 2f); changed |= nt != tempo; tempo = nt;
            if (GUILayout.Button("Reset tuning")) { recolor = false; intensity = scale = tempo = 1f; changed = true; }
            if (changed) foreach (var c in liveFx) if (c) Tune(c);
            GUILayout.Space(8);
            GUILayout.Label("← → browse · Space replay · right-drag orbit · wheel zoom · F reset view", DemoGUI.Small);
            var warn = DemoGUI.RenderWarning(cam);
            if (warn != null) { GUI.color = new Color(1f, 0.75f, 0.4f); GUILayout.Label(warn, DemoGUI.Label); GUI.color = Color.white; }
            GUILayout.EndVertical();
            GUILayout.EndArea();

            // effect list
            float lw = 230;
            GUILayout.BeginArea(new Rect(DemoGUI.W - lw - 16, 16, lw, DemoGUI.H - 32), DemoGUI.Box);
            scroll = GUILayout.BeginScrollView(scroll);
            for (int i = 0; i < view.Count; i++)
                if (GUILayout.Button(entries[view[i]].id, i == index ? DemoGUI.ItemOn : DemoGUI.Item)) { index = i; PlayCurrent(); }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
