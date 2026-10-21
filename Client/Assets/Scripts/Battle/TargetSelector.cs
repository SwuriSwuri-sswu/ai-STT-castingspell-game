using UnityEngine;
using UnityEngine.EventSystems;

// 마우스 좌클릭으로 몹을 골라 '현재 타겟'으로 지정
// EnemyController.GetNearest()가 이 타겟을 먼저 돌려주므로, 스펠이 선택한 몹을 노림
// 씬에 빈 오브젝트 하나 만들어서 붙이면 됨 (GameScene / Stage1 씬 등 전투 씬마다 1개)
public class TargetSelector : MonoBehaviour
{
    public static EnemyController Selected { get; private set; }

    // 씬에 켜져 있는 TargetSelector (없으면 예전처럼 자동 타겟)
    private static TargetSelector active;

    // 클릭한 몹에게만 마법이 나가는 모드인지 (TargetSelector가 씬에 없으면 false)
    public static bool RequireClick => active != null && active.requireClickToCast;

    [Header("타겟 규칙")]
    [Tooltip("켜면: 클릭한 몹에게만 마법이 나감. 아무 몹도 선택 안 했으면 시전 불가\n" +
             "끄면: 클릭한 몹 우선, 선택 없으면 가장 가까운 몹 자동 타겟")]
    public bool requireClickToCast = true;

    [Header("입력")]
    [Tooltip("몹 선택 마우스 버튼 (0 = 좌클릭, 1 = 우클릭)")]
    public int mouseButton = 0;
    [Tooltip("클릭이 닿는 최대 거리")]
    public float maxDistance = 300f;
    [Tooltip("레이캐스트가 맞을 레이어 (기본 전부)")]
    public LayerMask clickMask = ~0;

    [Tooltip("콜라이더를 빗나가도 이 거리(화면 픽셀) 안에 몹 중심이 있으면 선택")]
    public float pickRadius = 80f;

    [Header("선택 표시")]
    [Tooltip("선택된 몹 머리 위에 띄울 표시 (화살표, 링 등). 비우면 노란 공으로 대신 표시")]
    public GameObject markerPrefab;
    [Tooltip("몹 몸 중앙에서 위로 얼마나 띄울지")]
    public float markerHeight = 1.5f;
    [Tooltip("표시가 위아래로 살짝 움직이는 크기")]
    public float bobAmount = 0.15f;

    private GameObject marker;
    private Renderer[] markerRenderers;
    private EnemyController coloredFor;
    private Renderer[] targetRenderers; 

    private void OnEnable()
    {
        active = this;
    }

    private void OnDisable()
    {
        if (active == this) active = null;
        Selected = null;
        if (marker != null) marker.SetActive(false);
    }

    private void Update()
    {
        // 선택한 몹이 죽었거나, 이미 날아가는 마법으로 쓰러질 예정이면 선택 해제 → 가까운 몹으로 자동 타겟
        if (Selected != null && !Selected.IsTargetable) Selected = null;

        if (Input.GetMouseButtonDown(mouseButton)) TrySelect();

        UpdateMarker();
    }

    private void TrySelect()
    {
        // UI(버튼, 책 등) 위를 클릭한 건 무시
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector2 mouse = Input.mousePosition;
        EnemyController best = null;
        float bestDist = float.MaxValue;

        // 1. 클릭 방향에 걸린 몹들 중, 화면에서 몸 중심이 마우스에 가장 가까운 몹
        Ray ray = cam.ScreenPointToRay(mouse);
        RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance, clickMask, QueryTriggerInteraction.Collide);
        foreach (RaycastHit hit in hits)
        {
            EnemyController enemy = hit.collider.GetComponentInParent<EnemyController>();
            if (enemy == null || !enemy.IsTargetable) continue;
            float d = ScreenDistance(cam, enemy, mouse);
            if (d < bestDist) { bestDist = d; best = enemy; }
        }

        // 2. 아무 몹 콜라이더에도 안 닿았으면, 화면에서 가까운 몹 (pickRadius 안)
        if (best == null)
        {
            foreach (EnemyController enemy in EnemyController.AliveEnemies)
            {
                if (enemy == null || !enemy.IsTargetable) continue;
                float d = ScreenDistance(cam, enemy, mouse);
                if (d < pickRadius && d < bestDist) { bestDist = d; best = enemy; }
            }
        }

        if (best == null) return;
        Selected = best;
        Debug.Log($"[TargetSelector] 타겟 선택: {best.DisplayName}");
    }

    // 몹 몸 중심을 화면 좌표로 바꿔서 마우스와의 거리 (카메라 뒤면 무한대)
    private static float ScreenDistance(Camera cam, EnemyController enemy, Vector2 mouse)
    {
        Vector3 sp = cam.WorldToScreenPoint(enemy.AimPoint);
        if (sp.z < 0f) return float.MaxValue;
        return Vector2.Distance(mouse, new Vector2(sp.x, sp.y));
    }

    // 코드에서 직접 지정/해제하고 싶을 때
    public static void Select(EnemyController enemy) => Selected = enemy;
    public static void Clear() => Selected = null;

    private void UpdateMarker()
    {
        if (marker == null) marker = CreateMarker();
        if (marker == null) return;

        bool show = Selected != null;
        if (marker.activeSelf != show) marker.SetActive(show);
        if (!show) { coloredFor = null; return; }

        // 대상이 바뀌면 그 몹 속성 색으로
        if (Selected != coloredFor)
        {
            SetMarkerColor(EnemyController.ElementColor(Selected.enemyType));
            targetRenderers = Selected.GetComponentsInChildren<Renderer>();
            coloredFor = Selected;
        }

        // 몹 머리 꼭대기 + markerHeight 만큼 위
        float top = Selected.AimPoint.y;
        if (targetRenderers != null)
        {
            foreach (Renderer r in targetRenderers)
            {
                if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
                if (r.gameObject == marker) continue;
                top = Mathf.Max(top, r.bounds.max.y);
            }
        }
        Vector3 center = Selected.AimPoint;
        float bob = Mathf.Sin(Time.time * 4f) * bobAmount;
        marker.transform.position = new Vector3(center.x, top + markerHeight + bob, center.z);
    }

    private void SetMarkerColor(Color c)
    {
        if (markerRenderers == null) return;
        foreach (Renderer r in markerRenderers)
        {
            if (r == null) continue;
            Material mat = r.material;   // 이 표시 전용 복사본
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        }
    }

    private GameObject CreateMarker()
    {
        GameObject m;
        if (markerPrefab != null)
        {
            m = Instantiate(markerPrefab);
        }
        else
        {
            // 대체 표시: 작은 노란 공
            m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m.transform.localScale = Vector3.one * 0.3f;
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit != null)
            {
                Material mat = new Material(unlit);
                mat.color = new Color(1f, 0.85f, 0.2f);
                m.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }
        m.name = "TargetMarker";

        // 표시 자체가 클릭을 막지 않게 콜라이더 제거
        foreach (Collider c in m.GetComponentsInChildren<Collider>()) Destroy(c);
        m.SetActive(false);
        markerRenderers = m.GetComponentsInChildren<Renderer>(true);
        return m;
    }
}