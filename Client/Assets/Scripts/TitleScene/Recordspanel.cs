using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

// 타이틀 화면의 '기록 보기' 패널
// 패널 오브젝트에 붙이고, RECORDS 버튼 On Click → Open(), 닫기 버튼 On Click → Close() 연결
public class RecordsPanel : MonoBehaviour
{
    [Tooltip("최고 기록 표시 텍스트 (비워도 됨)")]
    public TMP_Text bestText;
    [Tooltip("최근 기록 목록 표시 텍스트")]
    public TMP_Text listText;
    [Tooltip("목록에 보여줄 최근 기록 수")]
    [Min(1)] public int showCount = 10;
    [Tooltip("시작할 때 패널 숨기기")]
    public bool hideOnStart = true;

    void Start()
    {
        if (hideOnStart) gameObject.SetActive(false);
    }

    public void Open()
    {
        gameObject.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    public void Refresh()
    {
        List<ResultRecord> all = ResultRecordStore.LoadAll();

        if (bestText != null)
        {
            ResultRecord best = ResultRecordStore.Best();
            bestText.text = best == null
                ? "BEST  -"
                : $"BEST  {best.score}";
        }

        if (listText == null) return;
        if (all.Count == 0)
        {
            listText.text = "No records yet.";
            return;
        }

        StringBuilder sb = new StringBuilder();
        int n = Mathf.Min(showCount, all.Count);
        for (int i = 0; i < n; i++)
        {
            ResultRecord r = all[i];
            string shortDate = r.date != null && r.date.Length >= 16 ? r.date.Substring(5) : r.date;   // 10-22 05:34
            sb.AppendLine($"{shortDate}   Score {r.score}   Kills {r.monstersDefeated}   Spells {r.spellsLearned}   {r.TimeText}");
        }
        listText.text = sb.ToString();
    }
}