using System.Collections.Generic;
using UnityEngine;

namespace Hun0FX
{
    /// <summary>
    /// One tuning surface for every hun0fx effect prefab: color, intensity, scale, tempo and per-layer visibility.
    /// Tools/Hun0FX/Attach Controllers From Catalog adds it to each prefab and fills the catalog fields.
    /// Tuning never edits the shared materials or the prefab; it uses per-instance overrides only.
    /// </summary>
    [DisallowMultipleComponent]
    public class HunFXController : MonoBehaviour
    {
        [Header("Catalog (filled by the editor tool)")]
        [Tooltip("Catalog id, e.g. Hit_01_Fire")] public string effectId;
        [Tooltip("Dominant hue of the original colors, in degrees")] public float baseHue;
        [Range(0f, 1f)] public float baseSaturation = 0.6f;
        [Tooltip("Length in seconds; -1 means the effect loops")] public float duration = -1f;

        [Header("Tuning")]
        [Tooltip("Retarget the whole effect to the hue of targetColor")] public bool recolor;
        [ColorUsage(false)] public Color targetColor = Color.white;
        [Range(0.3f, 2f)] public float intensity = 1f;
        [Range(0.25f, 3f)] public float scale = 1f;
        [Range(0.5f, 2f)] public float tempo = 1f;
        [Tooltip("Child paths, relative to this object, of layers to hide (e.g. \"Preset_Ember (1)\")")]
        public List<string> hiddenLayers = new List<string>();

        static readonly string[] ColorProps = { "_color1", "_color2", "_Color_tint", "_TintColor", "_Main_Color", "_BG_Color" };
        static readonly int ColorIntensityId = Shader.PropertyToID("_ColorIntensity");

        // SH_HunFX_common / common_back / common_front compute brightness as color * (_ColorIntensity + Custom1.z).
        // Custom1.z (TEXCOORD1.z) carries the per-particle intensity curve, so both must be scaled.
        const int CustomIntensityComponent = 2;

        class Layer
        {
            public ParticleSystem ps;
            public ParticleSystemRenderer renderer;
            public string path;
            public ParticleSystem.MinMaxGradient startColor;
            public ParticleSystem.MinMaxGradient colorOverLifetime;
            public bool hasColorOverLifetime;
            public float simulationSpeed;
            public ParticleSystemScalingMode scalingMode;
            public bool rendererEnabled;
            public bool hasCustomIntensity;
            public ParticleSystem.MinMaxCurve customIntensity;
            public bool isSubEmitter;   // triggered by its parent system; never played directly
        }

        List<Layer> layers;
        Vector3 baseScale;
        MaterialPropertyBlock block;

        /// <summary>Child paths of every particle layer, usable with SetLayerVisible / hiddenLayers.</summary>
        public IEnumerable<string> LayerPaths { get { Cache(); foreach (var l in layers) yield return l.path; } }

        void Awake() { Cache(); }
        void OnEnable() { Apply(); }
        void OnValidate() { if (Application.isPlaying && layers != null) Apply(); }

        void Cache()
        {
            if (layers != null) return;
            layers = new List<Layer>();
            block = new MaterialPropertyBlock();
            baseScale = transform.localScale;
            var all = GetComponentsInChildren<ParticleSystem>(true);
            var subs = new HashSet<ParticleSystem>();
            foreach (var ps in all)
            {
                var sm = ps.subEmitters;
                if (!sm.enabled) continue;
                for (int i = 0; i < sm.subEmittersCount; i++) { var s = sm.GetSubEmitterSystem(i); if (s != null) subs.Add(s); }
            }
            foreach (var ps in all)
            {
                var main = ps.main;
                var col = ps.colorOverLifetime;
                var cd = ps.customData;
                var r = ps.GetComponent<ParticleSystemRenderer>();
                bool customIntensity = cd.enabled && cd.GetMode(ParticleSystemCustomData.Custom1) == ParticleSystemCustomDataMode.Vector;
                layers.Add(new Layer
                {
                    ps = ps, renderer = r, path = PathOf(ps.transform),
                    // Deep copies: gradients/curves read from a module can alias the live data, so a later write
                    // (recolor) would also change the cached "original" and ResetColor would not restore it.
                    startColor = Copy(main.startColor), colorOverLifetime = Copy(col.color), hasColorOverLifetime = col.enabled,
                    simulationSpeed = main.simulationSpeed, scalingMode = main.scalingMode,
                    rendererEnabled = r != null && r.enabled,
                    hasCustomIntensity = customIntensity,
                    customIntensity = customIntensity ? Copy(cd.GetVector(ParticleSystemCustomData.Custom1, CustomIntensityComponent)) : default,
                    isSubEmitter = subs.Contains(ps),
                });
            }
        }

        string PathOf(Transform t)
        {
            if (t == transform) return "";
            var names = new List<string>();
            for (var x = t; x != null && x != transform; x = x.parent) names.Add(x.name);
            names.Reverse();
            return string.Join("/", names);
        }

        /// <summary>Push the current tuning values to every layer.</summary>
        public void Apply()
        {
            Cache();
            float hueShift = 0f, satMul = 1f;
            if (recolor)
            {
                Color.RGBToHSV(targetColor, out float th, out float ts, out _);
                hueShift = th * 360f - baseHue;
                satMul = ts < 0.08f ? 0f : Mathf.Clamp(ts / Mathf.Max(0.25f, baseSaturation), 0.4f, 1.6f);
            }
            transform.localScale = baseScale * scale;
            bool scaled = !Mathf.Approximately(scale, 1f);

            foreach (var L in layers)
            {
                var main = L.ps.main;
                main.simulationSpeed = L.simulationSpeed * tempo;
                main.scalingMode = scaled ? ParticleSystemScalingMode.Hierarchy : L.scalingMode;   // Local-scaled layers would ignore the root scale
                main.startColor = recolor ? Grade(L.startColor, hueShift, satMul) : Copy(L.startColor);
                if (L.hasColorOverLifetime)
                {
                    var col = L.ps.colorOverLifetime;
                    col.color = recolor ? Grade(L.colorOverLifetime, hueShift, satMul) : Copy(L.colorOverLifetime);
                }

                if (L.renderer == null) continue;
                L.renderer.enabled = L.rendererEnabled && !hiddenLayers.Contains(L.path);
                var mat = L.renderer.sharedMaterial;
                if (mat == null) continue;

                block.Clear();
                bool hasIntensity = mat.HasProperty(ColorIntensityId);
                if (hasIntensity)
                {
                    block.SetFloat(ColorIntensityId, mat.GetFloat(ColorIntensityId) * intensity);
                    if (L.hasCustomIntensity)
                    {
                        var cd = L.ps.customData;
                        cd.SetVector(ParticleSystemCustomData.Custom1, CustomIntensityComponent, Scale(L.customIntensity, intensity));
                    }
                }
                foreach (var prop in ColorProps)
                {
                    if (!mat.HasProperty(prop)) continue;
                    var c = mat.GetColor(prop);
                    if (recolor) c = Grade(c, hueShift, satMul);
                    if (!hasIntensity) { c.r *= intensity; c.g *= intensity; c.b *= intensity; }
                    block.SetColor(prop, c);
                }
                L.renderer.SetPropertyBlock(block);
            }
        }

        /// <summary>Restart the effect (hidden layers stay silent).</summary>
        public void Play()
        {
            Apply();
            foreach (var L in layers)
            {
                L.ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (!hiddenLayers.Contains(L.path) && !L.isSubEmitter) L.ps.Play(false);
            }
        }

        /// <summary>Stop emitting; existing particles finish unless clear is true.</summary>
        public void Stop(bool clear = false)
        {
            Cache();
            foreach (var L in layers)
                L.ps.Stop(false, clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
        }

        public bool IsAlive()
        {
            Cache();
            foreach (var L in layers) if (L.ps.IsAlive(false)) return true;
            return false;
        }

        public void SetColor(Color c) { recolor = true; targetColor = c; Apply(); }
        public void ResetColor() { recolor = false; Apply(); }
        public void SetIntensity(float v) { intensity = v; Apply(); }
        public void SetScale(float v) { scale = v; Apply(); }
        public void SetTempo(float v) { tempo = v; Apply(); }

        /// <summary>Show or hide one layer. Showing a layer while the effect is running resumes it.</summary>
        public void SetLayerVisible(string path, bool visible)
        {
            Cache();
            hiddenLayers.Remove(path);
            if (!visible) hiddenLayers.Add(path);
            Apply();
            if (!visible) return;
            bool running = false;
            foreach (var L in layers) if (L.path != path && L.ps.isPlaying) { running = true; break; }
            if (!running) return;
            foreach (var L in layers) if (L.path == path && !L.ps.isPlaying && !L.isSubEmitter) L.ps.Play(false);
        }

        // ---------- copies (modules can return live references) ----------
        static Gradient Copy(Gradient g)
        {
            if (g == null) return null;
            var o = new Gradient();
            o.SetKeys(g.colorKeys, g.alphaKeys);
            o.mode = g.mode;
            o.colorSpace = g.colorSpace;
            return o;
        }

        static ParticleSystem.MinMaxGradient Copy(ParticleSystem.MinMaxGradient m)
        {
            switch (m.mode)
            {
                case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(m.color);
                case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(m.colorMin, m.colorMax);
                case ParticleSystemGradientMode.Gradient: return new ParticleSystem.MinMaxGradient(Copy(m.gradient));
                case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(Copy(m.gradientMin), Copy(m.gradientMax));
                case ParticleSystemGradientMode.RandomColor:
                {
                    var r = new ParticleSystem.MinMaxGradient(Copy(m.gradient));
                    r.mode = ParticleSystemGradientMode.RandomColor;
                    return r;
                }
            }
            return m;
        }

        static AnimationCurve Copy(AnimationCurve c)
        {
            if (c == null) return null;
            return new AnimationCurve(c.keys) { preWrapMode = c.preWrapMode, postWrapMode = c.postWrapMode };
        }

        static ParticleSystem.MinMaxCurve Copy(ParticleSystem.MinMaxCurve m) { return Scale(m, 1f); }

        static ParticleSystem.MinMaxCurve Scale(ParticleSystem.MinMaxCurve m, float k)
        {
            switch (m.mode)
            {
                case ParticleSystemCurveMode.Constant: return new ParticleSystem.MinMaxCurve(m.constant * k);
                case ParticleSystemCurveMode.TwoConstants: return new ParticleSystem.MinMaxCurve(m.constantMin * k, m.constantMax * k);
                case ParticleSystemCurveMode.Curve: return new ParticleSystem.MinMaxCurve(m.curveMultiplier * k, Copy(m.curve));
                case ParticleSystemCurveMode.TwoCurves: return new ParticleSystem.MinMaxCurve(m.curveMultiplier * k, Copy(m.curveMin), Copy(m.curveMax));
            }
            return m;
        }

        // ---------- color grading (hue rotate + saturation scale, HDR-safe) ----------
        static Color Grade(Color c, float hueShift, float satMul)
        {
            float a = c.a;
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = Mathf.Repeat(h + hueShift / 360f, 1f);
            s = Mathf.Clamp01(s * satMul);
            var o = Color.HSVToRGB(h, s, v, true);
            o.a = a;
            return o;
        }

        static Gradient Grade(Gradient g, float hueShift, float satMul)
        {
            if (g == null) return null;
            var keys = g.colorKeys;
            for (int i = 0; i < keys.Length; i++) keys[i].color = Grade(keys[i].color, hueShift, satMul);
            var o = new Gradient();
            o.SetKeys(keys, g.alphaKeys);
            o.mode = g.mode;
            o.colorSpace = g.colorSpace;
            return o;
        }

        static ParticleSystem.MinMaxGradient Grade(ParticleSystem.MinMaxGradient m, float hs, float sm)
        {
            switch (m.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(Grade(m.color, hs, sm));
                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(Grade(m.colorMin, hs, sm), Grade(m.colorMax, hs, sm));
                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(Grade(m.gradient, hs, sm));
                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(Grade(m.gradientMin, hs, sm), Grade(m.gradientMax, hs, sm));
                case ParticleSystemGradientMode.RandomColor:
                {
                    var r = new ParticleSystem.MinMaxGradient(Grade(m.gradient, hs, sm));
                    r.mode = ParticleSystemGradientMode.RandomColor;
                    return r;
                }
            }
            return m;
        }
    }
}
