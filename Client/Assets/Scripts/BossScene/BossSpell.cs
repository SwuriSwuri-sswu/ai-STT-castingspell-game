using UnityEngine;

// 보스가 쏘는 마법 (BossController가 생성. 직접 붙일 필요 없음)
// 보스 손 → 플레이어(카메라 앞)까지 살짝 휘며 날아와서 닿으면 PlayerHealth.TakeDamage → 기존 피격 연출 그대로
// 비주얼 프리팹이 없으면 속성 색의 빛나는 구슬을 자동으로 만듦
public class BossSpell : MonoBehaviour
{
    public bool Done { get; private set; }

    private Vector3 start;
    private float speed;
    private float damage;
    private string reason;
    private GameObject impactPrefab;
    private float arcHeight;
    private float travel;      // 0 → 1
    private float distance;
    private Vector3 sideDir;

    // 발사
    public static BossSpell Launch(GameObject visualPrefab, Vector3 from, Color color, float scale,
                                   float speed, float damage, string reason, GameObject impactPrefab, float arcHeight = 0.8f)
    {
        GameObject go = visualPrefab != null ? Instantiate(visualPrefab, from, Quaternion.identity) : CreateOrb(color);
        go.transform.position = from;
        go.transform.localScale *= scale;

        // 플레이어 마법용 프리팹을 재사용할 때: 몹을 쫓아가는 스크립트·충돌은 끔
        foreach (ProjectileController pc in go.GetComponentsInChildren<ProjectileController>()) Destroy(pc);
        foreach (Collider c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
        foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;

        BossSpell spell = go.AddComponent<BossSpell>();
        spell.start = from;
        spell.speed = Mathf.Max(1f, speed);
        spell.damage = damage;
        spell.reason = reason;
        spell.impactPrefab = impactPrefab;
        spell.arcHeight = arcHeight;
        spell.sideDir = Random.insideUnitSphere * 0.6f;
        return spell;
    }

    // 플레이어가 맞는 지점 (카메라 바로 앞, 살짝 아래)
    private static Vector3 HitPoint()
    {
        Camera cam = Camera.main;
        if (cam == null) return Vector3.zero;
        return cam.transform.position + cam.transform.forward * 0.6f - cam.transform.up * 0.15f;
    }

    private void Update()
    {
        if (Done) return;
        Vector3 end = HitPoint();
        if (distance <= 0f) distance = Mathf.Max(0.1f, Vector3.Distance(start, end));

        travel += speed * Time.deltaTime / distance;
        float t = Mathf.Clamp01(travel);

        // 살짝 위로 휘었다가 내려오는 궤적 + 약간 옆으로 흔들림
        Vector3 pos = Vector3.Lerp(start, end, t);
        float arc = Mathf.Sin(t * Mathf.PI);
        pos += Vector3.up * arcHeight * arc + sideDir * arc;

        Vector3 dir = pos - transform.position;
        if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir);
        transform.position = pos;

        if (t >= 1f) Hit();
    }

    private void Hit()
    {
        Done = true;
        if (PlayerHealth.Instance != null) PlayerHealth.Instance.TakeDamage(damage, reason);
        if (impactPrefab != null) EffectUtil.SpawnAndAutoDestroy(impactPrefab, transform.position, Quaternion.identity);
        Vanish();
    }

    // 맞지 않고 사라지기 (보스가 쓰러지는 등)
    public void Cancel()
    {
        if (Done) return;
        Done = true;
        Vanish();
    }

    private void Vanish()
    {
        float tail = EffectUtil.StopEmitting(gameObject);
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!(r is ParticleSystemRenderer) && !(r is TrailRenderer)) r.enabled = false;
        }
        foreach (Light l in GetComponentsInChildren<Light>()) l.enabled = false;
        Destroy(gameObject, tail + 0.3f);
    }

    // 기본 비주얼: 빛나는 구슬 + 꼬리 + 빛
    private static GameObject CreateOrb(Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "BossSpell_Orb";
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * 0.35f;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader != null)
        {
            Material m = new Material(shader);
            m.color = Color.Lerp(color, Color.white, 0.35f);
            go.GetComponent<Renderer>().sharedMaterial = m;

            TrailRenderer trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.25f;
            trail.startWidth = 0.3f;
            trail.endWidth = 0f;
            Material tm = new Material(shader);
            tm.color = color;
            trail.sharedMaterial = tm;
            trail.startColor = color;
            trail.endColor = new Color(color.r, color.g, color.b, 0f);
        }
        else
        {
            go.GetComponent<Renderer>().material.color = color;
        }

        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = 4f;
        light.intensity = 3f;
        return go;
    }
}
