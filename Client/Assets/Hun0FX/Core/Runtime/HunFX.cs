using UnityEngine;

namespace Hun0FX
{
    /// <summary>One-call spawning for hun0fx effects. Example:
    /// <code>HunFX.Spawn(hitPrefab, hitPoint, Quaternion.LookRotation(attackDir), color: new Color(0.6f, 0.3f, 1f));</code>
    /// </summary>
    public static class HunFX
    {
        public static HunFXController Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null,
            Color? color = null, float scale = 1f, float intensity = 1f, float tempo = 1f, bool autoDestroy = true)
        {
            // rotation is applied on top of the prefab's own root rotation: several packs author their roots at -90 deg X
            // (Unity's default particle system orientation), and replacing it would tip those effects over.
            var go = Object.Instantiate(prefab, position, rotation * prefab.transform.rotation, parent);
            var fx = go.GetComponent<HunFXController>();
            if (fx == null) fx = go.AddComponent<HunFXController>();
            if (color.HasValue) { fx.recolor = true; fx.targetColor = color.Value; }
            fx.scale = scale;
            fx.intensity = intensity;
            fx.tempo = tempo;
            fx.Play();
            if (autoDestroy && fx.duration > 0f) Object.Destroy(go, fx.duration / Mathf.Max(0.05f, tempo) + 0.5f);
            return fx;
        }
    }
}
