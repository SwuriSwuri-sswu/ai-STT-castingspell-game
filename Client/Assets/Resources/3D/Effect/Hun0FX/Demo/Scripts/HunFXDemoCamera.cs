using UnityEngine;

namespace Hun0FX.Demo
{
    /// <summary>Orbit camera for the demo scenes. Right-drag (or Alt + left-drag) to orbit, wheel to zoom, F to reset.</summary>
    public class HunFXDemoCamera : MonoBehaviour
    {
        public Vector3 focus = new Vector3(0f, 1f, 1.75f);
        public float yaw = 215f, pitch = 22f, distance = 10f;
        public float minDistance = 3f, maxDistance = 40f;
        float targetDistance; Vector3 targetFocus; float yaw0, pitch0;

        void Awake() { targetDistance = distance; targetFocus = focus; yaw0 = yaw; pitch0 = pitch; }

        public void Frame(Vector3 point, float dist)
        {
            targetFocus = point;
            targetDistance = Mathf.Clamp(dist, minDistance, maxDistance);
        }

        void OnGUI()
        {
            var e = Event.current;
            if (e.type == EventType.MouseDrag && (e.button == 1 || (e.button == 0 && e.alt)))
            {
                yaw += e.delta.x * 0.3f;
                pitch = Mathf.Clamp(pitch + e.delta.y * 0.3f, -5f, 80f);
                e.Use();
            }
            else if (e.type == EventType.ScrollWheel)
            {
                targetDistance = Mathf.Clamp(targetDistance * (1f + e.delta.y * 0.05f), minDistance, maxDistance);
                e.Use();
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F)
            {
                yaw = yaw0; pitch = pitch0; e.Use();
            }
        }

        void LateUpdate()
        {
            float k = 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime);
            distance = Mathf.Lerp(distance, targetDistance, k);
            focus = Vector3.Lerp(focus, targetFocus, k);
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
        }
    }
}
