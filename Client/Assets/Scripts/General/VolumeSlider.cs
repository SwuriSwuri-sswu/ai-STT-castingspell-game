using UnityEngine;
using UnityEngine.UI;

// 설정 화면 볼륨 슬라이더 (Slider 오브젝트에 붙이기, On Value Changed 칸은 비워 두기)
//   처음 실행할 때는 가운데 → 오른쪽 끝 = 2배, 왼쪽 끝 = 소리 없음
//   바꾼 값은 이 컴퓨터에 저장되어 다음에 켜도 그대로 (처음 상태로 되돌리기: 컴포넌트 ⋮ 메뉴 → 저장된 볼륨 지우기)
[RequireComponent(typeof(Slider))]
public class VolumeSlider : MonoBehaviour
{
    [Tooltip("Master = 전체 / Bgm = 배경 음악만 / Sfx = 효과음만")]
    public SoundManager.VolumeKind kind = SoundManager.VolumeKind.Master;

    private Slider slider;

    private void Awake()
    {
        slider = GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
    }

    private void OnEnable()
    {
        slider.SetValueWithoutNotify(SoundManager.GetVolume(kind));   // 지금 값으로 손잡이 위치 맞추기
        slider.onValueChanged.AddListener(HandleChanged);
    }

    private void OnDisable()
    {
        slider.onValueChanged.RemoveListener(HandleChanged);
        SoundManager.SaveVolumes();   // 설정창을 닫을 때 저장
    }

    [ContextMenu("저장된 볼륨 지우기 (처음 실행 상태로)")]
    private void ResetSaved()
    {
        SoundManager.ResetVolumes();
        if (slider != null) slider.SetValueWithoutNotify(SoundManager.GetVolume(kind));
        Debug.Log("[Sound] 저장된 볼륨을 지웠습니다. 다음부터 가운데에서 시작합니다.");
    }

    private void HandleChanged(float value) => SoundManager.SetVolume(kind, value);
}