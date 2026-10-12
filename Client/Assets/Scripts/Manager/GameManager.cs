using System;
using UnityEngine;

public enum GameState
{
    Title,
    InGame,
    Paused,
    StageClear,
    GameOver
}

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Game State")]
    public GameState CurrentState { get; private set; }

    [Header("In-Game Player Data")]
    public int curStage = 1;
    public int playScore = 0;
    public int voiceScore = 0;
    public int monsterKills = 0;
    public string characterSkin = "default_character";
    public string wandSkin = "default_wand";

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

    private void Start()
    {
        StartGameSession();
    }

    private void Update()
    {
        // 인게임 및 일시정지 상태에서 ESC 키로 일시정지 토글
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (CurrentState == GameState.InGame || CurrentState == GameState.Paused)
            {
                TogglePause();
            }
        }
    }

    // 1. 게임 시작 세션 연결
    public void StartGameSession()
    {
        SetState(GameState.Title);
        StartCoroutine(NetworkManager.Instance.CreateNewGame((isSuccess) =>
        {
            if (isSuccess)
            {
                Debug.Log("게임 세션 준비 완료");
            }
        }));
    }

    // 2. 게임 상태 전환
    public void SetState(GameState newState)
    {
        CurrentState = newState;

        switch (CurrentState)
        {
            case GameState.Title:
                Time.timeScale = 1f;
                break;
            case GameState.InGame:
                Time.timeScale = 1f;
                break;
            case GameState.Paused:
                Time.timeScale = 0f; // 시간 정지
                break;
            case GameState.StageClear:
                Time.timeScale = 1f;
                OnStageClear();
                break;
            case GameState.GameOver:
                Time.timeScale = 1f;
                OnGameOver();
                break;
        }
    }

    // 3. 일시정지 토글
    public void TogglePause()
    {
        if (CurrentState == GameState.InGame)
        {
            SetState(GameState.Paused);
        }
        else if (CurrentState == GameState.Paused)
        {
            SetState(GameState.InGame);
        }
    }

    // 4. 몬스터 처치 시 호출
    public void AddMonsterKill(int scoreAmount)
    {
        monsterKills++;
        playScore += scoreAmount;
        Debug.Log($"몬스터 처치! 현재 킬 수: {monsterKills}, 점수: {playScore}");

        if (monsterKills % 5 == 0)
        {
            SkillManager.Instance.UnlockRandomSpell();
        }
    }

    // 5. AI 음성 인식 결과 수신 시 호출
    public void OnSpellRecognized(string spellName, int score = 50)
    {
        voiceScore += score;
        Debug.Log($"시전된 스펠: {spellName} (점수 +{score})");

        // 마법 시전 연출/로직 처리
    }

    // 6. 데이터 초기화 (재시작 시)
    public void ResetPlayerData()
    {
        curStage = 1;
        playScore = 0;
        voiceScore = 0;
        monsterKills = 0;
    }

    // 7. 스테이지 클리어 처리 및 서버 저장
    private void OnStageClear()
    {
        Debug.Log($"Stage {curStage} Clear!");

        SaveDataRequest saveRequest = new SaveDataRequest
        {
            character_skin = characterSkin,
            wand_skin = wandSkin,
            cur_stage = curStage,
            voice_score = voiceScore,
            play_score = playScore,
            monster_kills = monsterKills
        };

        StartCoroutine(NetworkManager.Instance.SaveGame(saveRequest, (isSuccess) =>
        {
            if (isSuccess)
            {
                curStage++;
            }
        }));
    }

    // 8. 게임 종료 시 호출
    private void OnGameOver()
    {
        Debug.Log("Game Over");

        ExitGameRequest exitRequest = new ExitGameRequest
        {
            end_time = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            character_skin = characterSkin,
            wand_skin = wandSkin,
            cur_stage = curStage,
            voice_score = voiceScore,
            play_score = playScore,
            monster_kills = monsterKills
        };

        StartCoroutine(NetworkManager.Instance.ExitGame(exitRequest));
    }
}