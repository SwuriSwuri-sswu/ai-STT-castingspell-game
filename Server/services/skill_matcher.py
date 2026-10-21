"""
스킬 키워드 매칭 (FULL 모드 전용)

플레이어가 스킬명만 외치는 방식을 전제로, 인식된 발화 전체와 스킬명을 비교한다.
문장에 섞여 나오는 방식으로 바꾸려면 부분 구간 탐색이 필요하며, 그때는
정확도 보정도 함께 의미를 잃으므로 데미지 계산 쪽도 같이 손봐야 한다.

스킬 목록은 Unity 가 skill_list 메시지로 보내며 parse_skill_list() 가 검사한다.
서버에는 기본 스킬 목록이 없다. 목록을 받기 전의 판정 요청은 NO_SKILL_LIST 오류가 된다.
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
# 이름이 비슷한 스킬 쌍은 find_collisions() 가 찾는다 (스킬 목록을 받을 때 서버 로그에 경고로 남는다).
AMBIGUITY_GAP = 0.15

# 문장 맨 앞에 붙는 간투사. "어 화염구" 처럼 말했을 때 점수가 깎이는 것을 막는다.
LEADING_FILLERS = ("어", "아", "음", "그", "저")


# ─────────────────────────────────────────────────────────────
# Whisper 힌트
# ─────────────────────────────────────────────────────────────


def build_initial_prompt(skills: dict) -> str:
    """Whisper 에 넘길 initial_prompt.

    프롬프트에 등장한 어휘 쪽으로 디코딩이 편향되므로 고유 스킬명 인식률이 오른다.
    다만 엉뚱한 소리에도 스킬명을 갖다 붙일 수 있다는 부작용이 있다.
    """
    return ", ".join(info["name"] for info in skills.values())


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


def match_skill(text: str, skills: dict) -> MatchResult:
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
        for skill_id, info in skills.items()
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
# 스킬 목록 (Unity 에서 받기)
# ─────────────────────────────────────────────────────────────

# 한 번에 받을 수 있는 최대 스킬 수.
# Whisper initial_prompt 는 길이 한계(약 223 토큰)가 있다. 30개 기준 136 토큰으로 측정됨.
MAX_SKILLS = 50


class SkillListError(ValueError):
    """Unity 가 보낸 스킬 목록의 형식이 잘못됨."""


def parse_skill_list(items) -> dict:
    """Unity 가 보낸 skills 배열을 검사해 매칭에 쓰는 dict 로 바꾼다.

    입력: [{"id": "...", "name": "...", "aliases": ["..."]}, ...]
    출력: {id: {"name": ..., "aliases": [name, 별칭...]}}
    매칭은 aliases 로만 하므로 name 을 항상 aliases 맨 앞에 넣는다.
    """
    if not isinstance(items, list) or not items:
        raise SkillListError("skills 는 비어 있지 않은 배열이어야 합니다")
    if len(items) > MAX_SKILLS:
        raise SkillListError(f"스킬이 너무 많습니다 ({len(items)} > {MAX_SKILLS})")

    skills: dict = {}
    for i, item in enumerate(items):
        if not isinstance(item, dict):
            raise SkillListError(f"{i}번째 항목이 객체가 아닙니다")

        sid = str(item.get("id") or "").strip()
        name = str(item.get("name") or "").strip()
        if not sid or not name:
            raise SkillListError(f"{i}번째 항목에 id 또는 name 이 없습니다")
        if sid in skills:
            raise SkillListError(f"id 가 중복됩니다: {sid}")
        if not normalize(name):
            raise SkillListError(f"비교할 수 있는 글자가 없는 이름입니다: {name}")

        raw_aliases = item.get("aliases") or []
        if not isinstance(raw_aliases, list):
            raise SkillListError(f"{name} 의 aliases 가 배열이 아닙니다")
        aliases = [str(a).strip() for a in raw_aliases if normalize(str(a))]

        # name 을 맨 앞에 두고 중복 제거 (순서 유지)
        skills[sid] = {"name": name, "aliases": list(dict.fromkeys([name, *aliases]))}

    return skills


def find_collisions(skills: dict, warn_at: float = 0.6) -> list[str]:
    """이름이 서로 비슷한 스킬 쌍을 돌려준다. 비슷한 쌍은 실제 발화에서 AMBIGUOUS(불발)가 나기 쉽다."""
    ids = list(skills)
    out = []
    for i in range(len(ids)):
        for j in range(i + 1, len(ids)):
            a, b = skills[ids[i]]["name"], skills[ids[j]]["name"]
            s = _ratio(normalize(a), normalize(b))
            if s >= warn_at:
                out.append(f"{a} <-> {b} 유사도 {s:.2f}")
    return out
