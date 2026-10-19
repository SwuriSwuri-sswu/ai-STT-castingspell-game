using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 몹 도감 초상화 자동 촬영
// 몹 프리팹을 맵에서 먼 곳에 하나씩 잠깐 꺼내서 전용 카메라로 찍고, 사진(Texture2D)으로 보관합니다.
// → 몹마다 카메라·RenderTexture를 따로 만들 필요 없음. 새 몹을 추가해도 자동으로 찍힘
//
// 씬에 빈 오브젝트 하나 만들고 붙이기 (GameScene / TitleScene 각각)
//   - GameScene: EnemyManager에 등록된 몹을 게임 시작 때 미리 촬영
//   - TitleScene처럼 EnemyManager가 없는 씬: Monsters 목록에 몹 프리팹 넣기
public class MonsterPhotoStudio : MonoBehaviour
{
    public static MonsterPhotoStudio Instance { get; private set; }

    [Header("촬영할 몹 (비우면 EnemyManager에 등록된 몹 전부)")]
    public List<EnemyController> monsters = new List<EnemyController>();
    [Tooltip("씬 시작 때 미리 전부 찍어 두기 (도감을 처음 열 때 사진이 바로 보이게)")]
    public bool captureOnStart = true;

    [Header("촬영 설정")]
    [Tooltip("사진 크기 (픽셀)")]
    public int photoSize = 256;
    [Tooltip("촬영 장소 (맵과 겹치지 않는 먼 곳)")]
    public Vector3 studioPosition = new Vector3(0f, -500f, 0f);
    [Tooltip("촬영용 레이어 (메인 카메라 Culling Mask에서 빠져 있는 레이어. 주인공 초상화용 Portrait를 같이 써도 됨)")]
    public string photoLayer = "Portrait";
    public float fieldOfView = 25f;
    [Tooltip("몹을 비스듬히 돌려서 찍는 각도 (0 = 정면)")]
    public float yawAngle = 25f;
    [Tooltip("위에서 내려다보는 정도 (0 = 수평)")]
    [Range(0f, 1f)] public float lookDown = 0.2f;
    [Tooltip("여백 (1 = 몹이 사진에 딱 맞음, 클수록 여백이 많음, 1보다 작으면 살짝 잘림)")]
    public float padding = 1.05f;
    [Tooltip("Idle 애니메이션을 이 시간만큼 진행시킨 자세로 찍기 (0 = 첫 프레임)")]
    public float poseTime = 0.3f;

    // 키 = 프리팹 이름
    private static readonly Dictionary<string, Texture2D> photos = new Dictionary<string, Texture2D>();
    private readonly Queue<EnemyController> queue = new Queue<EnemyController>();
    private bool working;
    private Camera photoCamera;
    private RenderTexture rt;

    public static event System.Action<string> OnPhotoReady;

    public static Texture2D GetPhoto(EnemyController prefab)
    {
        if (prefab == null) return null;
        photos.TryGetValue(prefab.gameObject.name, out Texture2D tex);
        return tex;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (rt != null) rt.Release();
    }

    private IEnumerator Start()
    {
        if (!captureOnStart) yield break;
        yield return null; // 다른 매니저들이 준비될 때까지 한 프레임 대기
        RequestAll();
    }

    public void RequestAll()
    {
        List<EnemyController> list = monsters;
        if ((list == null || list.Count == 0) && EnemyManager.Instance != null) list = EnemyManager.Instance.AllPrefabs;
        if (list == null) return;
        foreach (EnemyController m in list) Request(m);
    }

    // 아직 사진이 없으면 촬영 예약
    public void Request(EnemyController prefab)
    {
        if (prefab == null || photos.ContainsKey(prefab.gameObject.name) || queue.Contains(prefab)) return;
        queue.Enqueue(prefab);
        if (!working) StartCoroutine(ProcessQueue());
    }

    private IEnumerator ProcessQueue()
    {
        working = true;
        SetupCamera();
        while (queue.Count > 0)
        {
            EnemyController prefab = queue.Dequeue();
            if (prefab != null && !photos.ContainsKey(prefab.gameObject.name))
            {
                yield return Capture(prefab);
            }
        }
        working = false;
    }

    private void SetupCamera()
    {
        if (photoCamera != null) return;
        int layer = LayerMask.NameToLayer(photoLayer);
        if (layer < 0)
        {
            Debug.LogWarning($"[MonsterPhotoStudio] '{photoLayer}' 레이어가 없습니다. Layers에 추가해 주세요.");
            layer = 0;
        }

        rt = new RenderTexture(photoSize, photoSize, 24, RenderTextureFormat.ARGB32);
        rt.Create();

        GameObject camGo = new GameObject("MonsterPhotoCamera");
        camGo.transform.SetParent(transform, false);
        photoCamera = camGo.AddComponent<Camera>();
        photoCamera.enabled = false;
        photoCamera.clearFlags = CameraClearFlags.SolidColor;
        photoCamera.backgroundColor = new Color(0f, 0f, 0f, 0f); // 투명 배경
        photoCamera.cullingMask = 1 << layer;
        photoCamera.fieldOfView = fieldOfView;
        photoCamera.nearClipPlane = 0.05f;
        photoCamera.farClipPlane = 100f;
        photoCamera.targetTexture = rt;
    }

    private IEnumerator Capture(EnemyController prefab)
    {
        int layer = Mathf.Max(0, LayerMask.NameToLayer(photoLayer));

        // 1. 꺼진 부모 아래에 복제 → 몹 스크립트(공격·등록 등)가 실행되지 않음
        GameObject holder = new GameObject("PhotoHolder_" + prefab.gameObject.name);
        holder.SetActive(false);
        holder.transform.position = studioPosition;

        GameObject clone = Instantiate(prefab.gameObject, holder.transform);
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localScale = prefab.transform.localScale;
        // 정면(+Z)이 카메라를 보도록 + 비스듬히
        clone.transform.localRotation = Quaternion.Euler(0f, 180f + prefab.facingYawOffset + yawAngle, 0f);

        // 켜기 전에 즉시 제거해야 Awake/OnEnable(몹 등록·공격 등)이 실행되지 않음
        foreach (MonoBehaviour mb in clone.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
        foreach (Collider c in clone.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (ParticleSystemRenderer pr in clone.GetComponentsInChildren<ParticleSystemRenderer>(true)) pr.enabled = false;
        foreach (Transform t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

        holder.SetActive(true);
        yield return null; // 한 프레임 (Animator 초기화)

        // 2. Idle 자세 잡기
        Animator anim = clone.GetComponentInChildren<Animator>();
        if (anim != null && anim.runtimeAnimatorController != null)
        {
            anim.Update(0f);
            if (poseTime > 0f) anim.Update(poseTime);
        }

        // 3. 몹 크기에 맞춰 카메라 위치 자동 조절
        Bounds b = new Bounds(clone.transform.position, Vector3.zero);
        bool has = false;
        foreach (Renderer r in clone.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        if (has)
        {
            // 몹의 가장 긴 변(높이 또는 가로폭)이 사진에 딱 맞는 거리
            float halfSize = Mathf.Max(b.extents.y, Mathf.Max(b.extents.x, b.extents.z)) * padding;
            float depth = Mathf.Max(b.extents.x, b.extents.z);
            float dist = halfSize / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad) + depth;
            float radius = b.extents.magnitude;
            Vector3 dir = new Vector3(0f, lookDown, -1f).normalized; // 몹 앞(-Z 쪽)에서 바라봄
            photoCamera.transform.position = b.center + dir * dist;
            photoCamera.transform.LookAt(b.center);
            photoCamera.farClipPlane = dist + radius * 2f;

            // 4. 촬영: 이번 프레임 끝에 렌더된 결과를 사진으로 복사
            photoCamera.enabled = true;
            yield return new WaitForEndOfFrame();
            photoCamera.enabled = false;

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(photoSize, photoSize, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, photoSize, photoSize), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            tex.name = "Photo_" + prefab.gameObject.name;

            photos[prefab.gameObject.name] = tex;
            OnPhotoReady?.Invoke(prefab.gameObject.name);
        }
        else
        {
            Debug.LogWarning($"[MonsterPhotoStudio] {prefab.gameObject.name}: 보이는 모델이 없어 촬영하지 못했습니다.");
        }

        Destroy(holder);
    }
}