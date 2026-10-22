using System;
using System.Collections.Generic;
using UnityEngine;

// 게임에서 쓰는 모든 소리를 모아 두는 에셋 (Assets/Resources/SoundLibrary.asset 하나만 있으면 됨)
//   만들기·채우기: 메뉴 Tools → Sound → "사운드 라이브러리 자동 채우기"
//   → 파일 이름(예: SFX_Chant_Start)이 같은 오디오를 찾아 아래 칸에 알아서 넣어 줌
//   소리를 바꾸고 싶으면 칸에 다른 파일을 끌어다 놓기만 하면 됨 (코드 수정 X)
[CreateAssetMenu(fileName = "SoundLibrary", menuName = "Sound/Sound Library")]
public class SoundLibrary : ScriptableObject
{
    // 자동 채우기용: 이 이름의 파일 (배열이면 "이름_1", "이름_2", "이름_ice" 처럼 이름_으로 시작하는 파일 전부)
    [AttributeUsage(AttributeTargets.Field)]
    public class FileAttribute : Attribute
    {
        public readonly string name;
        public FileAttribute(string name) { this.name = name; }
    }

    [Header("크기 (0~1, 설정 화면의 볼륨에 곱해짐)")]
    [Range(0f, 1f)] public float bgmVolume = 0.45f;
    [Range(0f, 1f)] public float ambienceVolume = 0.3f;
    [Range(0f, 1f)] public float sfxVolume = 0.85f;
    [Range(0f, 1f)] public float uiVolume = 0.6f;
    [Range(0f, 1f)] public float typingVolume = 0.3f;
    [Tooltip("타이핑이 이 시간(초) 넘게 멈추면 타이핑 소리도 멈춤 (줄바꿈·문단 끝에서 잠깐 조용해짐)")]
    public float typingStopGap = 0.14f;
    [Tooltip("파일마다 크기 따로 맞추기 (너무 크거나 작은 파일만 넣기)")]
    public List<ClipVolume> clipVolumes = new List<ClipVolume>();

    [Serializable]
    public class ClipVolume
    {
        public AudioClip clip;
        [Range(0f, 2f)] public float volume = 1f;
    }

    [Header("느낌")]
    [Tooltip("효과음 높낮이를 매번 살짝 다르게 (0.08 = ±8%). 같은 소리가 반복돼도 덜 질림")]
    [Range(0f, 0.3f)] public float pitchVariation = 0.08f;
    [Tooltip("영창(Space) 중 BGM·환경음 크기 (0.3 = 30%로 줄임). 마이크에 음악이 덜 들어감")]
    [Range(0f, 1f)] public float duckWhileChanting = 0.3f;
    [Tooltip("BGM이 바뀔 때 서로 섞이는 시간 (초)")]
    public float bgmFadeTime = 1.2f;
    [Tooltip("게임 오버 BGM 반복 재생 (짧은 효과음 같은 곡이면 끄기)")]
    public bool gameOverBgmLoop = false;
    [Tooltip("엔딩: 점수(결과 문구)가 다 나오고 몇 초 뒤에 엔딩 BGM을 시작할지")]
    public float endingBgmDelay = 0.5f;
    [Tooltip("상성 적중음을 폭발음보다 이만큼 늦게 (초). 동시에 나면 묻힘")]
    public float effectivenessDelay = 0.12f;
    [Tooltip("보스가 맞을 때 몹 피격음 높이 (낮을수록 묵직)")]
    public float bossHitPitch = 0.75f;

    [Header("씬 이름")]
    public string titleScene = "TitleScene";
    public string gameScene = "GameScene";
    public string bossScene = "BossScene";
    public string endingScene = "EndingScene";

    [Header("BGM")]
    [File("BGM_Title")] public AudioClip bgmTitle;
    [File("BGM_Stage1")] public AudioClip bgmStage1;
    [Tooltip("보스전 입장(걸어 들어가는 동안) ~ 안내창까지")]
    [File("BGM_BossEntrance")] public AudioClip bgmBossEntrance;
    [File("BGM_Boss")] public AudioClip bgmBoss;
    [File("BGM_BossPhase3")] public AudioClip bgmBossPhase3;
    [File("BGM_GameOver")] public AudioClip bgmGameOver;
    [File("BGM_Ending")] public AudioClip bgmEnding;

    [Header("환경음 (반복)")]
    [Tooltip("GameScene 횃불")]
    [File("AMB_Torch")] public AudioClip ambTorch;
    [Tooltip("BossScene 성 안")]
    [File("AMB_BossCastle")] public AudioClip ambBossCastle;

    [Header("영창·시전")]
    [File("SFX_Chant_Start")] public AudioClip chantStart;
    [Tooltip("판정 성공 '띠링'")]
    [File("SFX_Cast_Success")] public AudioClip castSuccess;
    [File("SFX_Cast_Fail")] public AudioClip castFail;
    [Tooltip("스펠 SpellData의 Cast Sound가 비어 있을 때 속성별로 이 중 하나")]
    [File("SFX_Cast_Normal")] public AudioClip[] castNormal;
    [File("SFX_Cast_Fire")] public AudioClip[] castFire;
    [File("SFX_Cast_Water")] public AudioClip[] castWater;
    [File("SFX_Cast_Grass")] public AudioClip[] castGrass;

    [Header("적중 폭발 (SpellData의 Hit Sound가 비어 있을 때 속성별로 이 중 하나)")]
    [File("SFX_Hit_Explosion_펑")] public AudioClip[] hitNormal;
    [File("SFX_Hit_Explosion_Fire")] public AudioClip[] hitFire;
    [File("SFX_Hit_Explosion_Water")] public AudioClip[] hitWater;
    [File("SFX_Hit_Explosion_Grass")] public AudioClip[] hitGrass;

    [Header("전투")]
    [File("SFX_Monster_Appear")] public AudioClip monsterAppear;
    [File("SFX_Monster_Hit")] public AudioClip monsterHit;
    [File("SFX_Monster_Die")] public AudioClip monsterDie;
    [File("SFX_Player_Hit")] public AudioClip playerHit;
    [File("SFX_SuperEffective")] public AudioClip superEffective;
    [File("SFX_NotEffective")] public AudioClip notEffective;
    [File("SFX_Spell_Get")] public AudioClip spellGet;

    [Header("보스전")]
    [File("SFX_Turn_Player")] public AudioClip turnPlayer;
    [File("SFX_Turn_Boss")] public AudioClip turnBoss;
    [File("SFX_Boss_Shoot")] public AudioClip bossShoot;
    [Tooltip("보스 마법 폭발 (이 중 하나를 무작위로). SFX_Boss_Explosion, SFX_Boss_Explosion_2 ...")]
    [File("SFX_Boss_Explosion")] public AudioClip[] bossExplosions;
    [File("SFX_Boss_Ground")] public AudioClip bossGround;
    [File("SFX_Boss_Charge")] public AudioClip bossCharge;
    [File("SFX_Boss_ChargeBreak")] public AudioClip bossChargeBreak;
    [File("SFX_Boss_PowerUp")] public AudioClip bossPowerUp;
    [File("SFX_Boss_Stun")] public AudioClip bossStun;
    [File("SFX_Boss_Die")] public AudioClip bossDie;
    [File("SFX_Minion_Portal")] public AudioClip minionPortal;
    [Tooltip("보스전 입장 때 걷는 발소리 (이 중 하나를 무작위로). SFX_Footstep_1, SFX_Footstep_2 ...")]
    [File("SFX_Footstep")] public AudioClip[] footsteps;

    [Header("UI·연출")]
    [Tooltip("모든 버튼 클릭 (따로 연결할 필요 없음)")]
    [File("SFX_UI_Click")] public AudioClip uiClick;
    [Tooltip("버튼에 마우스를 올릴 때 (비워 두면 소리 없음)")]
    [File("SFX_UI_Hover")] public AudioClip uiHover;
    [File("SFX_UI_BookOpen")] public AudioClip bookOpen;
    [Tooltip("비워 두면 Book Open 소리를 씀")]
    [File("SFX_UI_BookClose")] public AudioClip bookClose;
    [Tooltip("결과 문구 타이핑")]
    [File("SFX_UI_Typing")] public AudioClip typing;
    [File("SFX_GameOver_Fall")] public AudioClip gameOverFall;
    [File("SFX_Victory")] public AudioClip victory;

    // ---------- 코드에서 사용 ----------
    private Dictionary<AudioClip, float> volumeCache;

    public float VolumeOf(AudioClip clip)
    {
        if (clip == null) return 1f;
        if (volumeCache == null)
        {
            volumeCache = new Dictionary<AudioClip, float>();
            foreach (ClipVolume c in clipVolumes)
                if (c != null && c.clip != null) volumeCache[c.clip] = c.volume;
        }
        return volumeCache.TryGetValue(clip, out float v) ? v : 1f;
    }

    private void OnValidate() => volumeCache = null;   // Inspector에서 바꾸면 바로 반영

    public AudioClip[] CastFor(MagicElement e)
    {
        switch (e)
        {
            case MagicElement.Fire: return castFire;
            case MagicElement.Water: return castWater;
            case MagicElement.Grass: return castGrass;
            default: return castNormal;
        }
    }

    public AudioClip[] HitFor(MagicElement e)
    {
        switch (e)
        {
            case MagicElement.Fire: return hitFire;
            case MagicElement.Water: return hitWater;
            case MagicElement.Grass: return hitGrass;
            default: return hitNormal;
        }
    }
}