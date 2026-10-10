from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker, declarative_base

# SQLite DB 파일 경로 설정
SQLALCHEMY_DATABASE_URL = "sqlite:///./game.db"

# 엔진 생성
# 파이썬과 SQLite DB 사이 데이터를 주고 받는 통로
# "check_same_thread": False - 동시 처리할 때 SQLite가 잘작동하게 함
engine = create_engine(
    SQLALCHEMY_DATABASE_URL, connect_args = {"check_same_thread": False}
)

# 세션 생성기(실제 DB 작업을 할 때 하나씩 꺼내 씀)
# 실제로 DB에 들어가서 데이터를 넣거나 빼올 세션 만듦
SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)

# 테이블 모델들이 상속받을 기본 클래스
# 테이블들이 공통으로 물려받아야 할 기본 성질을 담아둠
Base = declarative_base()

# DB 세션을 열고 닫는 함수
def get_db():
    db = SessionLocal() # 세션 시작
    try:
        yield db # main.py의 API 로직 실행
    finally:
        db.close()