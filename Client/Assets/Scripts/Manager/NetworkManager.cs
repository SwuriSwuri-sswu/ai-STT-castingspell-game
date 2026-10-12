using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance { get; private set; }

    [Header("Server Configuration")]
    [SerializeField] private string baseUrl = "http://127.0.0.1:8000/v1";
    [SerializeField] private string accessToken = "valid_token";

    public string CurrentSessionId { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // [공통 헬퍼] Request에 인증/세션 헤더를 붙여주는 메서드
    private UnityWebRequest CreateRequest(string endpoint, string method, string jsonBody = null)
    {
        var request = new UnityWebRequest(baseUrl + endpoint, method);

        if (!string.IsNullOrEmpty(jsonBody))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        }

        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("access-token", accessToken);

        if (!string.IsNullOrEmpty(CurrentSessionId))
        {
            request.SetRequestHeader("session-id", CurrentSessionId);
        }

        return request;
    }

    // 1. 신규 게임 시작 (/v1/game/new)
    public IEnumerator CreateNewGame(Action<bool> onComplete = null)
    {
        using (UnityWebRequest request = CreateRequest("/game/new", "POST", "{}"))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                // 서버 응답 JSON을 파싱하여 session_id 바인딩
                var response = JsonUtility.FromJson<ApiResponse<GameSessionData>>(request.downloadHandler.text);
                if (response != null && response.data != null)
                {
                    CurrentSessionId = response.data.id;
                    Debug.Log($"세션 생성 및 ID 저장 완료: {CurrentSessionId}");
                    onComplete?.Invoke(true);
                }
                else
                {
                    Debug.LogError("세션 응답 파싱 실패");
                    onComplete?.Invoke(false);
                }
            }
            else
            {
                Debug.LogError("세션 생성 실패: " + request.error);
                onComplete?.Invoke(false);
            }
        }
    }

    // 2. 게임 중간 저장 (/v1/game/save)
    public IEnumerator SaveGame(SaveDataRequest saveData, Action<bool> onComplete = null)
    {
        string jsonBody = JsonUtility.ToJson(saveData);
        using (UnityWebRequest request = CreateRequest("/game/save", "PATCH", jsonBody))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("게임 진행 상황 저장 완료");
                onComplete?.Invoke(true);
            }
            else
            {
                Debug.LogError("게임 저장 실패: " + request.error);
                onComplete?.Invoke(false);
            }
        }
    }

    // 3. 게임 종료 및 데이터 저장 (/v1/game/exit)
    public IEnumerator ExitGame(ExitGameRequest exitData, Action<bool> onComplete = null)
    {
        string jsonBody = JsonUtility.ToJson(exitData);
        using (UnityWebRequest request = CreateRequest("/game/exit", "POST", jsonBody))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("게임 종료 및 저장 처리 완료");
                CurrentSessionId = null; // 세션 초기화
                onComplete?.Invoke(true);
            }
            else
            {
                Debug.LogError("게임 종료 처리 실패: " + request.error);
                onComplete?.Invoke(false);
            }
        }
    }

    // 4. 환경설정 불러오기 (/v1/settings)
    public IEnumerator GetSettings(Action<SettingsData> onComplete)
    {
        using (UnityWebRequest request = CreateRequest("/settings", "GET"))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<ApiResponse<SettingsData>>(request.downloadHandler.text);
                Debug.Log("환경설정 불러오기 성공");
                onComplete?.Invoke(response?.data);
            }
            else
            {
                Debug.LogError("환경설정 조회 실패: " + request.error);
                onComplete?.Invoke(null);
            }
        }
    }

    // 5. 환경설정 변경 저장 (/v1/settings)
    public IEnumerator UpdateSettings(SettingsUpdateRequest newSettings, Action<bool> onComplete = null)
    {
        string jsonBody = JsonUtility.ToJson(newSettings);
        using (UnityWebRequest request = CreateRequest("/settings", "PATCH", jsonBody))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("환경설정 업데이트 완료");
                onComplete?.Invoke(true);
            }
            else
            {
                Debug.LogError("환경설정 변경 실패: " + request.error);
                onComplete?.Invoke(false);
            }
        }
    }
}