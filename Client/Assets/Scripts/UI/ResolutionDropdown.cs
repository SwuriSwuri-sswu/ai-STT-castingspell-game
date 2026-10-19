using System.Collections.Generic;
using TMPro;
using UnityEngine;

// 설정 패널의 해상도 드롭다운 (각 씬의 Resolution_Dropdown 에 붙이기)
// - 패널이 열릴 때 항목을 GameSettingManager.ResolutionOptions 로 다시 채우고, 지금 해상도를 선택 표시한다
// - 고르면 GameSettingManager.SetResolutionByIndex → 해상도 적용 + 바로 서버에 저장 (PATCH /v1/settings)
// - Inspector 에서 On Value Changed 를 따로 연결하지 않아도 된다
// ※ 에디터의 Game 창에서는 Screen.SetResolution 이 적용되지 않는다. 빌드한 게임에서 확인할 것
[RequireComponent(typeof(TMP_Dropdown))]
public class ResolutionDropdown : MonoBehaviour
{
    TMP_Dropdown dropdown;

    void Awake()
    {
        dropdown = GetComponent<TMP_Dropdown>();
        dropdown.onValueChanged.AddListener(OnChanged);
    }

    void OnDestroy()
    {
        if (dropdown != null) dropdown.onValueChanged.RemoveListener(OnChanged);
    }

    void OnEnable() => Refresh();

    // 항목을 다시 채우고 지금 해상도를 선택 표시 (이벤트는 보내지 않음)
    public void Refresh()
    {
        var options = new List<string>(GameSettingManager.ResolutionOptions.Length);
        foreach (string r in GameSettingManager.ResolutionOptions) options.Add(r.Replace("x", " x "));   // "1920x1080" → "1920 x 1080"
        dropdown.ClearOptions();
        dropdown.AddOptions(options);

        int index = GameSettingManager.Instance != null ? GameSettingManager.Instance.ResolutionIndex : 0;
        dropdown.SetValueWithoutNotify(index);
        dropdown.RefreshShownValue();
    }

    void OnChanged(int index)
    {
        if (GameSettingManager.Instance != null) GameSettingManager.Instance.SetResolutionByIndex(index);
        else Debug.LogWarning("[ResolutionDropdown] GameSettingManager 가 없어 해상도를 바꾸지 않았습니다.");
    }
}
