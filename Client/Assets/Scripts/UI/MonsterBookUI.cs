using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 몹 도감 (MonsterPanel에 붙이기)
//
// 구성 (이름 규칙만 맞추면 자동 인식)
//   MonsterPanel                ← 이 스크립트
//     ├ MonsterPanel_1          ← 페이지 (이름이 "MonsterPanel_숫자" → 순서대로 페이지)
//     │   ├ Normal  ← 속성 묶음 (Sections에 등록)
//     │   │   ├ Normal_Title / Normal_Icon / (선택) Normal_Count
//     │   │   └ Mob_Slot × N    ← 이름에 "Mob_Slot"이 들어간 오브젝트 = 몹 한 칸
//     │   │       ├ (RawImage)  ← 초상화
//     │   │       └ (TextMeshPro) ← 설명
//     │   ├ Fire …
//     │   └ Arrow (Button)      ← 다음 장 (마지막 장에서는 이전 장)
//     └ MonsterPanel_2 …
//
// 표시
//   - 만난 몹: 초상화 + 이름(속성 색) · 등급 · 속성 / 공격 속도와 데미지 / 약점
//   - 못 만난 몹: 검은 실루엣 + ???
//   - 처음 만난 뒤 아직 도감에서 안 본 몹: NEW
public class MonsterBookUI : MonoBehaviour
{
    [Serializable]
    public class ElementSection
    {
        public MagicElement element;
        [Tooltip("이 속성 칸들이 들어 있는 부모 (예: MonsterPanel_1/Fire)")]
        public Transform root;
        [Tooltip("(선택) 만난 몹 수 '2 / 7'")]
        public TMP_Text countText;
        [NonSerialized] public List<Slot> slots;
    }

    public class Slot
    {
        public GameObject go;
        public RawImage portrait;
        public TMP_Text desc;
    }

    [Header("몹 목록 (비우면 EnemyManager에 등록된 몹)")]
    [Tooltip("TitleScene처럼 EnemyManager가 없는 씬에서는 몹 프리팹을 전부 넣어 주세요")]
    public List<EnemyController> allMonsters = new List<EnemyController>();

    [Header("속성 묶음 (Normal / Fire / Water / Grass)")]
    public List<ElementSection> sections = new List<ElementSection>();
    [Tooltip("몹 한 칸으로 인식할 오브젝트 이름 (포함되면 인식, 대소문자 무시)")]
    public string slotKeyword = "Mob_Slot";
    [Tooltip("몹 수보다 칸이 많으면 남는 칸 숨기기")]
    public bool hideUnusedSlots = true;

    [Header("페이지 (비우면 'MonsterPanel_1, _2…' 자식을 자동으로)")]
    public List<GameObject> pages = new List<GameObject>();
    [Tooltip("페이지 안의 버튼 중 이름에 이 단어가 있으면 넘기기 버튼으로 자동 연결")]
    public string arrowKeyword = "Arrow";

    [Header("설명 문구 ({name} {rank} {element} {interval} {damage} {weakness} {kills})")]
    [TextArea(3, 5)]
    public string descFormat = "{name} <size=75%>{rank}</size>  {element}\n공격 속도: {interval}초마다 {damage} 공격\n약점: {weakness}";
    public string weaknessFormat = "{weak} 속성 마법";
    public string noWeaknessText = "없음";
    [TextArea(1, 3)]
    public string lockedText = "???";
    public string newBadge = "NEW";

    [Header("색")]
    [Tooltip("못 만난 몹 초상화 (검은 실루엣)")]
    public Color silhouetteColor = new Color(0f, 0f, 0f, 0.85f);
    public Color descColor = new Color(0.29f, 0.18f, 0.10f);
    public Color lockedDescColor = new Color(0.29f, 0.18f, 0.10f, 0.4f);
    public Color newBadgeColor = new Color(0.85f, 0.35f, 0.05f);
    [Tooltip("도감에서 노말 몹 이름 색 (게임 화면 자막의 금색은 그대로)")]
    public Color normalNameColor = Color.black;

    private bool initialized;
    private int currentPage;
    private readonly List<string> shownNew = new List<string>();

    private void OnEnable()
    {
        if (!initialized) Initialize();
        ShowPage(0);
        Refresh();
        MonsterPhotoStudio.OnPhotoReady += HandlePhotoReady;
        if (MonsterPhotoStudio.Instance != null)
        {
            foreach (EnemyController m in Monsters()) MonsterPhotoStudio.Instance.Request(m);
        }
    }

    private void OnDisable()
    {
        MonsterPhotoStudio.OnPhotoReady -= HandlePhotoReady;
        MonsterCollection.MarkSeen(shownNew);
        shownNew.Clear();
    }

    private void HandlePhotoReady(string key) => Refresh();

    // =========================================================
    // 초기화: 칸·페이지·화살표 자동 인식
    // =========================================================
    private void Initialize()
    {
        initialized = true;

        foreach (ElementSection section in sections)
        {
            section.slots = new List<Slot>();
            if (section.root == null) continue;
            foreach (Transform t in section.root.GetComponentsInChildren<Transform>(true))
            {
                if (t == section.root) continue;
                if (t.name.IndexOf(slotKeyword, StringComparison.OrdinalIgnoreCase) < 0) continue;
                section.slots.Add(new Slot
                {
                    go = t.gameObject,
                    portrait = t.GetComponentInChildren<RawImage>(true),
                    desc = t.GetComponentInChildren<TMP_Text>(true),
                });
            }
        }

        if (pages == null || pages.Count == 0)
        {
            pages = new List<GameObject>();
            string prefix = gameObject.name + "_";
            foreach (Transform child in transform)
            {
                if (child.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) pages.Add(child.gameObject);
            }
            pages.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        }

        // 각 페이지의 화살표 버튼 연결
        for (int i = 0; i < pages.Count; i++)
        {
            foreach (Button button in pages[i].GetComponentsInChildren<Button>(true))
            {
                string n = button.name.ToLowerInvariant();
                bool isPrev = n.Contains("prev") || n.Contains("left") || n.Contains("back");
                bool isNext = n.Contains("next") || n.Contains("right");
                if (!isPrev && !isNext)
                {
                    if (n.IndexOf(arrowKeyword.ToLowerInvariant(), StringComparison.Ordinal) < 0) continue;
                    // 그냥 "Arrow": 마지막 장이면 이전, 아니면 다음
                    isPrev = i == pages.Count - 1 && pages.Count > 1;
                    isNext = !isPrev;
                }
                if (isPrev) button.onClick.AddListener(PrevPage);
                else button.onClick.AddListener(NextPage);
            }
        }

        int slotCount = 0;
        foreach (ElementSection s in sections) slotCount += s.slots.Count;
        Debug.Log($"[MonsterBookUI] 페이지 {pages.Count}장, 몹 칸 {slotCount}개, 몹 {Monsters().Count}종");
    }

    // =========================================================
    // 페이지 넘기기 (버튼 OnClick에 직접 연결해도 됨)
    // =========================================================
    public void NextPage() => ShowPage(currentPage + 1);
    public void PrevPage() => ShowPage(currentPage - 1);

    public void ShowPage(int index)
    {
        if (pages == null || pages.Count == 0) return;
        currentPage = Mathf.Clamp(index, 0, pages.Count - 1);
        for (int i = 0; i < pages.Count; i++)
        {
            if (pages[i] != null) pages[i].SetActive(i == currentPage);
        }
    }

    // =========================================================
    // 내용 채우기
    // =========================================================
    private List<EnemyController> Monsters()
    {
        if (allMonsters != null && allMonsters.Count > 0) return allMonsters;
        return EnemyManager.Instance != null ? EnemyManager.Instance.AllPrefabs : new List<EnemyController>();
    }

    public void Refresh()
    {
        if (!initialized) return;
        List<EnemyController> all = Monsters();

        foreach (ElementSection section in sections)
        {
            if (section.slots == null) continue;

            // 이 속성 몹을 등급(하급→상급) → 이름 순으로
            List<EnemyController> list = all.FindAll(m => m != null && m.enemyType == section.element);
            list.Sort((a, b) =>
            {
                int r = a.rank.CompareTo(b.rank);
                return r != 0 ? r : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            int met = 0;
            for (int i = 0; i < section.slots.Count; i++)
            {
                Slot slot = section.slots[i];
                bool used = i < list.Count;
                if (hideUnusedSlots) slot.go.SetActive(used);
                if (!used)
                {
                    if (slot.desc != null) slot.desc.text = "";
                    if (slot.portrait != null) slot.portrait.enabled = false;
                    continue;
                }

                EnemyController mob = list[i];
                string key = mob.gameObject.name;
                bool known = MonsterCollection.IsEncountered(key);
                if (known) met++;

                // 초상화: 만났으면 원래 색, 못 만났으면 검은 실루엣
                if (slot.portrait != null)
                {
                    Texture2D photo = MonsterPhotoStudio.GetPhoto(mob);
                    slot.portrait.texture = photo;
                    slot.portrait.enabled = photo != null;
                    slot.portrait.color = known ? Color.white : silhouetteColor;
                }

                if (slot.desc != null)
                {
                    if (known)
                    {
                        bool isNew = !MonsterCollection.IsSeen(key);
                        if (isNew && !shownNew.Contains(key)) shownNew.Add(key);
                        slot.desc.text = BuildDesc(mob, isNew);
                        slot.desc.color = descColor;
                    }
                    else
                    {
                        slot.desc.text = lockedText;
                        slot.desc.color = lockedDescColor;
                    }
                }
            }

            if (section.countText != null) section.countText.text = $"{met} / {list.Count}";
        }
    }

    private string BuildDesc(EnemyController mob, bool isNew)
    {
        Color nameColor = mob.enemyType == MagicElement.Normal ? normalNameColor : EnemyController.ElementColor(mob.enemyType);
        string hex = ColorUtility.ToHtmlStringRGB(nameColor);
        string name = $"<color=#{hex}><b>{mob.DisplayName}</b></color>";
        if (isNew && !string.IsNullOrEmpty(newBadge))
        {
            name += $" <size=70%><color=#{ColorUtility.ToHtmlStringRGB(newBadgeColor)}>{newBadge}</color></size>";
        }

        mob.GetAttackStats(out float interval, out float damage);

        string weakness = noWeaknessText;
        if (EnemyController.TryGetWeakness(mob.enemyType, out MagicElement weak))
        {
            string weakHex = ColorUtility.ToHtmlStringRGB(EnemyController.ElementColor(weak));
            weakness = weaknessFormat.Replace("{weak}", $"<color=#{weakHex}>{EnemyController.ElementToKorean(weak)}</color>");
        }

        return descFormat
            .Replace("{name}", name)
            .Replace("{rank}", EnemyController.RankToKorean(mob.rank))
            .Replace("{element}", EnemyController.ElementToKorean(mob.enemyType))
            .Replace("{interval}", interval.ToString("0.#"))
            .Replace("{damage}", damage.ToString("0.#"))
            .Replace("{weakness}", weakness)
            .Replace("{kills}", MonsterCollection.GetKills(mob.gameObject.name).ToString());
    }

    [ContextMenu("몹 도감 기록 초기화 (테스트용)")]
    private void ClearRecord()
    {
        MonsterCollection.Clear();
        if (isActiveAndEnabled) Refresh();
    }
}