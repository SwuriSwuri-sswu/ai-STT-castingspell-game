using UnityEngine;

/// <summary>
/// string 필드에 붙이면 Inspector 에서 현재 연결된 마이크 목록을 토글로 선택할 수 있다.
/// 실제 표시는 Editor/MicrophoneDeviceDrawer.cs 가 담당한다.
/// 선택값: 빈 문자열 = 시스템 기본 장치, 그 외 = Microphone.devices 의 장치 이름
/// </summary>
public class MicrophoneDeviceAttribute : PropertyAttribute { }