# handlers.py
from fastapi import Request, status
from fastapi.responses import JSONResponse
from fastapi.exceptions import RequestValidationError
from exceptions import AppException

# 1. 애플리케이션 비즈니스 커스텀 예외 핸들러
async def app_exception_handler(request: Request, exc: AppException):
    return JSONResponse(
        status_code=exc.code,
        content={
            "status": "fail" if exc.code < 500 else "error",
            "code": exc.code,
            "message": exc.message,
            "data": None
        }
    )

# 2. Pydantic 요청 파라미터 검증 실패 예외 핸들러
async def validation_exception_handler(request: Request, exc: RequestValidationError):
    return JSONResponse(
        status_code=status.HTTP_400_BAD_REQUEST,
        content={
            "status": "fail",
            "code": status.HTTP_400_BAD_REQUEST,
            "message": "요청 데이터 형식이 바르지 않습니다.",
            "detail": exc.errors()
        }
    )

# 3. 알 수 없는 서버 내부 500 에러 핸들러
async def global_exception_handler(request: Request, exc: Exception):
    print(f"[Unhandled Internal Error]: {exc}")
    return JSONResponse(
        status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
        content={
            "status": "error",
            "code": status.HTTP_500_INTERNAL_SERVER_ERROR,
            "message": "서버 내부 오류가 발생했습니다."
        }
    )