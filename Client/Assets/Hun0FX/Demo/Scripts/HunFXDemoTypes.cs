using UnityEngine;

namespace Hun0FX.Demo
{
    /// <summary>Where a demo effect is spawned. Offsets are in the actor's frame (+Z = the actor's forward).</summary>
    public enum DemoPlacement
    {
        Actor,          // actor position + offset, actor rotation + euler
        ActorAttached,  // parented to the actor (status effects)
        Target,         // target position + offset (chest hits, cut lines)
        TargetGround,   // target position projected to the ground + offset
        Projectile,     // starts at actor + offset, flies to target + endOffset, then spawns hitOnArrive
        Orbit,          // circles around actor + offset (radius = endOffset.x, speed in m/s), for looping projectiles with trails
    }

    [System.Serializable]
    public class DemoEntry
    {
        public GameObject prefab;
        public string id;
        public string pack;
        public string category;
        public string element;
        public DemoPlacement placement;
        public Vector3 offset;
        public Vector3 euler;
        [Header("Projectile / Orbit")]
        public Vector3 endOffset;
        public float speed = 9f;
        public GameObject hitOnArrive;
        [Header("Timing / view")]
        public bool loop;
        public float duration = 1f;
        public float viewDistance = 10f;
        [TextArea] public string note;
    }

    public static class DemoPlace
    {
        public static Vector3 Point(DemoPlacement p, Transform actor, Transform target, Vector3 offset)
        {
            var rot = Flat(actor.rotation);
            switch (p)
            {
                case DemoPlacement.Target: return target.position + rot * offset;
                case DemoPlacement.TargetGround: { var t = target.position; t.y = actor.position.y; return t + rot * offset; }
                default: return actor.position + rot * offset;
            }
        }

        public static Quaternion Flat(Quaternion q)
        {
            var f = q * Vector3.forward; f.y = 0f;
            return f.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(f.normalized, Vector3.up);
        }

        /// <summary>Spawn an effect with the hun0fx controller. Loops are left running; one-shots are destroyed after their duration.</summary>
        public static HunFXController Spawn(GameObject prefab, DemoPlacement p, Transform actor, Transform target, Vector3 offset, Vector3 euler, float scale = 1f)
        {
            var rot = Flat(actor.rotation) * Quaternion.Euler(euler);
            if (p == DemoPlacement.ActorAttached)
                return HunFX.Spawn(prefab, actor.position + Flat(actor.rotation) * offset, rot, actor, scale: scale);
            return HunFX.Spawn(prefab, Point(p, actor, target, offset), rot, null, scale: scale);
        }
    }
}
