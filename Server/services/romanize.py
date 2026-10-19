"""
실시간 자막용 한글 → 로마자 변환 (발음대로)

- koroman 사용 (국어의 로마자 표기법 + 발음 규칙). 설치: pip install koroman==1.0.16
- 화면 자막 전용이다. 판정·로그에는 원래 한글(text)을 그대로 쓴다.
- koroman 이 없으면 None 을 돌려준다 → 클라이언트는 한글을 그대로 보여 준다 (서버는 정상 동작).
"""

import logging
import re
from functools import lru_cache

log = logging.getLogger("voice.roman")

try:
    from koroman import romanize as _romanize
except ImportError:
    _romanize = None
    log.warning("[roman] koroman 이 설치되어 있지 않아 실시간 자막을 로마자로 바꾸지 않습니다 "
                "(Server 폴더에서 .venv\\Scripts\\pip install koroman==1.0.16)")

# Vosk 가 붙이는 [24] 같은 대괄호 표기 (자막에서는 뺀다)
_BRACKET = re.compile(r"\[[^\]]*\]")
_SPACES = re.compile(r"\s+")


def available() -> bool:
    return _romanize is not None


@lru_cache(maxsize=512)
def to_roman(text: str) -> str | None:
    """자막용 로마자. koroman 이 없으면 None, 남는 글자가 없으면 "" (예: "[1]" 만 들린 경우)."""
    if _romanize is None:
        return None
    cleaned = _SPACES.sub(" ", _BRACKET.sub(" ", text or "")).strip()
    if not cleaned:
        return ""
    try:
        return _romanize(cleaned)   # 기본값: 발음 규칙 적용 (해돋이 → haedoji)
    except Exception:
        log.exception("[roman] 로마자 변환 실패: \"%s\"", cleaned)
        return None
