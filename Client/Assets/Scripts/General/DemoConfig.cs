using UnityEngine;

// 시연 모드 설정 (Assets/Resources/DemoConfig.asset). 설명은 DemoMode.cs 맨 위 참고
[CreateAssetMenu(fileName = "DemoConfig", menuName = "Demo/Demo Config")]
public class DemoConfig : ScriptableObject
{
    [Tooltip("켜면 시연 모드. main 브랜치에서는 반드시 꺼 두기")]
    public bool enabled = false;

    [Header("게임씬")]
    [Tooltip("인트로 이야기 건너뛰고 바로 시작")]
    public bool skipIntro = true;
    [Tooltip("몹 등장 순서 (등급). 처치 수만큼 다음 칸으로. 칸이 모자라면 마지막 등급 반복")]
    public MonsterRank[] rankSequence = { MonsterRank.Low, MonsterRank.Mid, MonsterRank.High };
    [Tooltip("(선택) 특정 몹을 순서대로 내보내고 싶으면 여기에. 비운 칸은 위 등급에서 무작위")]
    public EnemyController[] mobSequence = new EnemyController[0];
    [Tooltip("이만큼 잡으면 보스씬으로 이동")]
    [Min(1)] public int stage1Kills = 3;
    [Tooltip("시작할 때 잠가 둘 스펠 (첫 몹을 잡으면 이 스펠이 드랍). 비우면 기본 해금이 아닌 스펠 중 마지막 것")]
    public SpellData lockedSpell;
    [Tooltip("플레이어 체력이 1 밑으로 내려가지 않음 (맞는 연출·체력 감소는 그대로)")]
    public bool playerInvincible = true;

    [Header("보스씬")]
    [Tooltip("입장 때 걷는 속도 배율")]
    [Min(0.1f)] public float bossWalkSpeed = 1.6f;
    [Tooltip("보스전 안내창 생략")]
    public bool skipBossTutorial = true;
    [Tooltip("내 턴 + 마왕 턴을 몇 번 한 뒤 마왕이 쓰러질지")]
    [Min(1)] public int bossRounds = 2;
    [Tooltip("마지막 라운드의 마왕 턴 직전에 페이즈 전환 연출 보여 주기")]
    public bool showPhaseChange = true;
    [Tooltip("내 턴 제한 시간 (초). 음성 판정이 느릴 수 있으니 넉넉하게")]
    public float playerTurnTime = 20f;

    [Header("비상 키 (마이크·서버가 안 될 때)")]
    public bool emergencyKeys = true;
    [Tooltip("지금 적에게 상성이 좋은 스펠 시전")]
    public KeyCode bestSpellKey = KeyCode.F1;
    [Tooltip("아무 스펠이나 시전")]
    public KeyCode anySpellKey = KeyCode.F2;

    [Header("기록")]
    [Tooltip("게임을 켤 때마다 도감·보스 클리어·인트로 본 기록을 지움 (리허설 후에도 매번 같은 시연)")]
    public bool resetRecordsOnLaunch = true;
    public string gameSceneName = "GameScene";
}