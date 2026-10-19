using System.Collections.Generic;
using TMPro;
using UnityEngine;

// 설정 패널의 마이크 선택 드롭다운 (각 씬의 MicSetting_Dropdown 에 붙이기)
// - 패널이 열릴 때마다 연결된 마이크 목록을 새로 채운다 (게임 중에 꽂은 마이크도 보이게)
// - 맨 위 "Default (System)" = OS 기본 입력 장치 (Windows: 설정 > 소리 > 입력 / macOS: 시스템 설정 > 사운드 > 입력)
// - 고르면 GameSettingManager 가 이 컴퓨터에 저장(PlayerPrefs)하고, 지금 씬의 VoiceInput 에 바로 적용한다
// - Inspector 에서 On Value Changed 를 따로 연결하지 않아도 된다
[RequireComponent(typeof(TMP_Dropdown))]
public class MicDropdown : MonoBehaviour
{
    public const string DefaultLabel = "Default (System)";

    TMP_Dropdown dropdown;
    readonly List<string> devices = new List<string>();   // 드롭다운 항목 순서와 같은 장치 이름 ("" = Default)

    void Awake()
    {
        dropdown = GetComponent<TMP_Dropdown>();
        dropdown.onValueChanged.AddListener(OnChanged);

        // 긴 장치 이름이 여러 줄로 접혀 위아래 항목과 겹치지 않게: 한 줄로 고정하고 넘치면 "…"
        // (목록 항목은 itemText 견본을 복사해 만들어지므로, 목록을 채우기 전에 설정해 두면 모든 항목에 적용된다)
        foreach (TMP_Text label in new[] { dropdown.captionText, dropdown.itemText })
        {
            if (label == null) continue;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    void OnDestroy()
    {
        if (dropdown != null) dropdown.onValueChanged.RemoveListener(OnChanged);
    }

    void OnEnable() => Refresh();

    // 목록을 다시 채우고 지금 쓰는 마이크를 선택 표시 (이벤트는 보내지 않음)
    public void Refresh()
    {
        GameSettingManager gsm = GameSettingManager.Instance;

        devices.Clear();
        devices.Add("");
        devices.AddRange(gsm != null ? gsm.RefreshMicrophones() : Microphone.devices);

        var options = new List<string>(devices.Count);
        foreach (string d in devices) options.Add(string.IsNullOrEmpty(d) ? DefaultLabel : d);
        dropdown.ClearOptions();
        dropdown.AddOptions(options);

        // 저장한 장치가 지금 없으면 Default 로 표시 (VoiceInput 도 그때는 기본 장치를 쓴다)
        string current = gsm != null ? gsm.selectedMicName : "";
        dropdown.SetValueWithoutNotify(Mathf.Max(0, devices.IndexOf(current ?? "")));
        dropdown.RefreshShownValue();
    }

    void OnChanged(int index)
    {
        if (index < 0 || index >= devices.Count) return;

        if (GameSettingManager.Instance != null)
        {
            GameSettingManager.Instance.SetMicrophone(devices[index]);
        }
        else
        {
            // 설정 매니저가 없는 씬(테스트 등): 저장 없이 지금 씬에만 적용
            foreach (VoiceInput input in FindObjectsByType<VoiceInput>(FindObjectsSortMode.None))
                input.DeviceName = devices[index];
            Debug.LogWarning("[MicDropdown] GameSettingManager 가 없어 마이크 선택을 저장하지 않았습니다 (지금 씬에만 적용).");
        }
    }
}
