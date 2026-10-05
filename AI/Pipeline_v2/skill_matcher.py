"""
스킬 키워드 매칭 (FULL 모드 전용)

플레이어가 스킬명만 외치는 방식을 전제로, 인식된 발화 전체와 스킬명을 비교한다.
문장에 섞여 나오는 방식으로 바꾸려면 부분 구간 탐색이 필요하며, 그때는
정확도 보정도 함께 의미를 잃으므로 데미지 계산 쪽도 같이 손봐야 한다.

단독 실행하면 스킬명 충돌 검사와 샘플 판정 결과를 출력한다.
    python3 skill_matcher.py
"""

import re
import unicodedata
from dataclasses import dataclass

# ─────────────────────────────────────────────────────────────
# 튜닝 상수
# ─────────────────────────────────────────────────────────────

# 발화 전체와 스킬명의 자모 유사도가 이 값 미만이면 스킬로 보지 않는다.
# 낮추면 잘 발동하지만 일상 대화에 오발동할 위험이 커진다.
MATCH_THRESHOLD = 0.50

# 1위와 2위 스킬의 점수 차가 이보다 작으면 모호하다고 보고 불발시킨다.
# 스킬을 추가할 때마다 check_skill_collisions() 로 여유를 확인할 것.
AMBIGUITY_GAP = 0.15

# 문장 맨 앞에 붙는 간투사. "어 화염구" 처럼 말했을 때 점수가 깎이는 것을 막는다.
LEADING_FILLERS = ("어", "아", "음", "그", "저")


# ─────────────────────────────────────────────────────────────
# 스킬 정의
# ─────────────────────────────────────────────────────────────

# aliases 에는 테스트 중 반복 관측된 오인식 표기를 추가한다.
# 알고리즘을 손보는 것보다 여기에 한 줄 넣는 편이 훨씬 효과적이다.
SKILLS = {
    "strike": {
        "name": "때리기",
        "base_damage": 1000,
        "aliases": ["때리기"],
    },
    "guard": {
        "name": "막아주기",
        "base_damage": 800,
        "aliases": ["막아주기"],
    },
    "blast": {
        "name": "날리기",
        "base_damage": 900,
        "aliases": ["날리기"],
    },
}


def build_initial_prompt() -> str:
    """Whisper 에 넘길 initial_prompt.

    프롬프트에 등장한 어휘 쪽으로 디코딩이 편향되므로 고유 스킬명 인식률이 오른다.
    다만 엉뚱한 소리에도 스킬명을 갖다 붙일 수 있다는 부작용이 있다.
    """
    return ", ".join(info["name"] for info in SKILLS.values())


# ─────────────────────────────────────────────────────────────
# 정규화 / 자모 분해
# ─────────────────────────────────────────────────────────────


def normalize(text: str) -> str:
    """비교 전 노이즈 제거.

    공백을 전부 없애는 이유는 Whisper 가 띄어쓰기를 일관되게 내지 않기 때문이다.
    ("화염구" 와 "화염 구" 를 같게 만든다)
    """
    text = text.strip().lower()

    # 선행 간투사 제거. 한 글자짜리만, 맨 앞에서만 떼어낸다.
    for f in LEADING_FILLERS:
        if text.startswith(f + " "):
            text = text[len(f) + 1 :]
            break

    text = re.sub(r"[^0-9a-z가-힣ㄱ-ㅎㅏ-ㅣ]", "", text)
    return text


def to_jamo(text: str) -> str:
    """한글을 초성/중성/종성으로 분해한다.

    NFD 가 돌려주는 자모는 일반 'ㅂ'(U+3142) 이 아니라 조합용 자모다.
    비교 대상 양쪽 모두 이 함수를 거치게 해야 한다.
    """
    return unicodedata.normalize("NFD", text)


def _ratio(a: str, b: str) -> float:
    """자모 단위 유사도 (0~1). 입력은 정규화된 문자열을 기대한다."""
    from difflib import SequenceMatcher

    return SequenceMatcher(None, to_jamo(a), to_jamo(b)).ratio()


# ─────────────────────────────────────────────────────────────
# 결과 객체
# ─────────────────────────────────────────────────────────────


@dataclass
class MatchResult:
    skill_id: str | None
    match: float
    second_match: float
    reject_reason: str | None  # None 이면 성공

    @property
    def ok(self) -> bool:
        return self.skill_id is not None and self.reject_reason is None


# ─────────────────────────────────────────────────────────────
# 공개 함수
# ─────────────────────────────────────────────────────────────


def match_skill(text: str) -> MatchResult:
    """인식 텍스트에서 스킬을 판별한다.

    match 값은 게이트 통과 여부와 무관하게 항상 채워지므로
    불발 원인을 확인하는 데 쓸 수 있다.
    """
    norm = normalize(text)
    if not norm:
        return MatchResult(None, 0.0, 0.0, "EMPTY_TEXT")

    # 스킬별 최고점 (별칭 중 가장 잘 맞는 것)
    scores = [
        (skill_id, max(_ratio(norm, normalize(a)) for a in info["aliases"]))
        for skill_id, info in SKILLS.items()
    ]
    scores.sort(key=lambda x: x[1], reverse=True)

    best_id, best = scores[0]
    second = scores[1][1] if len(scores) > 1 else 0.0

    if best < MATCH_THRESHOLD:
        return MatchResult(None, best, second, "NO_MATCH")

    if best - second < AMBIGUITY_GAP:
        return MatchResult(None, best, second, "AMBIGUOUS")

    return MatchResult(best_id, best, second, None)


def normalized_match(match: float) -> float:
    """게이트를 통과한 match 를 0~1 로 다시 편다.

    통과한 값은 [MATCH_THRESHOLD, 1.0] 구간에 몰려 있다. 이 재정규화를 빼면
    간신히 통과한 발화도 보정을 거의 다 받게 되어 의도한 차등이 사라진다.
    """
    m = (match - MATCH_THRESHOLD) / (1.0 - MATCH_THRESHOLD)
    return min(max(m, 0.0), 1.0)


# ─────────────────────────────────────────────────────────────
# 자체 점검
# ─────────────────────────────────────────────────────────────


def check_skill_collisions(warn_at: float = 0.6) -> None:
    """스킬명끼리 얼마나 비슷한지 전수 출력.

    높게 나오는 쌍이 있으면 임계값을 손보는 것보다 스킬명 자체를 바꾸는 편이 확실하다.
    """
    ids = list(SKILLS)
    print(f"--- 스킬명 상호 유사도 (경고 기준 {warn_at}) ---")
    worst = 0.0
    for i in range(len(ids)):
        for j in range(i + 1, len(ids)):
            a, b = SKILLS[ids[i]]["name"], SKILLS[ids[j]]["name"]
            s = _ratio(normalize(a), normalize(b))
            worst = max(worst, s)
            flag = "   <-- 위험" if s >= warn_at else ""
            print(f"  {a} <-> {b} : {s:.3f}{flag}")
    print(f"  최고치 {worst:.3f}\n")


if __name__ == "__main__":
    check_skill_collisions()

    samples = [
        "때리기",
        "때리기!",
        "어 때리기",
        "대리기",  # 오인식 ㄸ -> ㄷ
        "막아주기",
        "막아 주기",  # 공백은 normalize 가 제거하므로 위와 동일 취급
        "날리기",
        "날리끼",  # 오인식 경음화
        "오늘 점심 뭐 먹지",
        "저기 저놈한테 날리기 써",  # FULL 모드에서는 불발이 정상
    ]

    print(f"--- 판정 (임계값 {MATCH_THRESHOLD}, 모호성 간격 {AMBIGUITY_GAP}) ---")
    for s in samples:
        r = match_skill(s)
        if r.ok:
            tag = f"{SKILLS[r.skill_id]['name']} (보정계수 {normalized_match(r.match):.2f})"
        else:
            tag = f"불발 [{r.reject_reason}]"
        print(f"  {s:<22} -> {tag:<26} match={r.match:.3f} 2nd={r.second_match:.3f}")