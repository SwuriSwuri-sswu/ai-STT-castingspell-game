using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// 게임 결과 기록 1개
[Serializable]
public class ResultRecord
{
    public string date;          // 저장한 날짜·시간 (yyyy-MM-dd HH:mm)
    public int monstersDefeated; // 처치한 몬스터 수
    public int spellsLearned;    // 배운 주문 수
    public float timeSurvived;   // 생존 시간 (초)
    public int score;            // 점수

    // 01:17 형식
    public string TimeText => $"{(int)(timeSurvived / 60f):00}:{(int)(timeSurvived % 60f):00}";
}

[Serializable]
class ResultRecordList
{
    public List<ResultRecord> records = new List<ResultRecord>();
}

// 결과 기록을 이 컴퓨터에 JSON 파일로 저장/불러오기 (서버 DB 없이)
// 파일 위치: Application.persistentDataPath/results.json
//   Mac 에디터: ~/Library/Application Support/<회사명>/<제품명>/results.json
public static class ResultRecordStore
{
    const string FileName = "results.json";
    const int MaxRecords = 100;   // 오래된 기록부터 지워서 이 개수만 유지

    static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    // 저장된 기록 전부 (최신이 앞)
    public static List<ResultRecord> LoadAll()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<ResultRecord>();
            string json = File.ReadAllText(FilePath);
            ResultRecordList list = JsonUtility.FromJson<ResultRecordList>(json);
            return list?.records ?? new List<ResultRecord>();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ResultRecordStore] 기록 불러오기 실패: " + e.Message);
            return new List<ResultRecord>();
        }
    }

    // 기록 하나 추가 저장. 성공하면 true
    public static bool Save(ResultRecord record)
    {
        if (record == null) return false;
        try
        {
            List<ResultRecord> all = LoadAll();
            if (string.IsNullOrEmpty(record.date)) record.date = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            all.Insert(0, record);
            if (all.Count > MaxRecords) all.RemoveRange(MaxRecords, all.Count - MaxRecords);

            string json = JsonUtility.ToJson(new ResultRecordList { records = all }, true);
            File.WriteAllText(FilePath, json);
            Debug.Log($"[ResultRecordStore] 저장 완료 ({all.Count}개) → {FilePath}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ResultRecordStore] 저장 실패: " + e.Message);
            return false;
        }
    }

    // 결과창이 뜰 때 한 줄로 호출: ResultRecordStore.SaveResult(처치수, 배운주문수, 생존시간초, 점수);
    // 같은 판에서 결과창이 두 번 떠도 한 번만 저장됨 (1초 안에 같은 기록이 또 오면 무시)
    static float lastSaveTime = -999f;
    static int lastSavedScore = int.MinValue;

    public static bool SaveResult(int monstersDefeated, int spellsLearned, float timeSurvivedSeconds, int score)
    {
        if (Time.unscaledTime - lastSaveTime < 1f && score == lastSavedScore) return false;
        lastSaveTime = Time.unscaledTime;
        lastSavedScore = score;

        return Save(new ResultRecord
        {
            monstersDefeated = monstersDefeated,
            spellsLearned = spellsLearned,
            timeSurvived = timeSurvivedSeconds,
            score = score,
        });
    }

    // 시간이 "01:17" 같은 글자일 때 쓰는 버전 (숫자 타입이 int/float 어느 쪽이어도 됨)
    public static bool SaveResult(object kills, object spells, string timeText, object score)
    {
        return SaveResult(ToInt(kills), ToInt(spells), ParseTime(timeText), ToInt(score));
    }

    static int ToInt(object v)
    {
        try { return v == null ? 0 : Mathf.RoundToInt(Convert.ToSingle(v)); }
        catch { return 0; }
    }

    // "mm:ss" 또는 "hh:mm:ss" → 초
    static float ParseTime(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        float total = 0f;
        foreach (string part in text.Trim().Split(':'))
        {
            float.TryParse(part, out float n);
            total = total * 60f + n;
        }
        return total;
    }

    // 최고 점수 기록 (없으면 null)
    public static ResultRecord Best()
    {
        ResultRecord best = null;
        foreach (ResultRecord r in LoadAll())
            if (best == null || r.score > best.score) best = r;
        return best;
    }

    // 전부 지우기 (테스트용)
    public static void Clear()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }
}