# exceptions.py
from fastapi import status

class AppException(Exception):
    """프로젝트 기본 커스텀 예외"""
    def __init__(self, message: str = "요청 처리 중 오류가 발생했습니다.", code: int = status.HTTP_400_BAD_REQUEST):
        self.message = message
        self.code = code
        super().__init__(self.message)

class NotFoundError(AppException):
    def __init__(self, message: str = "요청한 리소스를 찾을 수 없습니다."):
        super().__init__(message=message, code=status.HTTP_404_NOT_FOUND)

class UnauthorizedError(AppException):
    def __init__(self, message: str = "인증 토큰이 유효하지 않습니다."):
        super().__init__(message=message, code=status.HTTP_401_UNAUTHORIZED)

class BadRequestError(AppException):
    def __init__(self, message: str = "잘못된 요청입니다."):
        super().__init__(message=message, code=status.HTTP_400_BAD_REQUEST)