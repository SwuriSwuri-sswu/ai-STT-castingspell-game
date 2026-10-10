using UnityEngine;
using TMPro;
using System.Collections;

public class BattleUIManager : MonoBehaviour
{
    public static BattleUIManager Instance; // 싱글톤 접근용

    [Header("UI 연결")]
    public TextMeshProUGUI effectivenessText; // "효과가 굉장했다!"를 띄울 Text 오브젝트

    void Awake()
    {
        Instance = this;
    }

    // 외부에서 상성 배율을 전달받아 텍스트를 출력하는 함수
    public void ShowEffectiveness(float multiplier)
    {
        if (effectivenessText == null) return;

        // 기존에 띄워져 있던 텍스트 코루틴을 멈추고 새로 띄움
        StopAllCoroutines();
        StartCoroutine(DisplayRoutine(multiplier));
    }

    private IEnumerator DisplayRoutine(float multiplier)
    {
        // 배율에 따른 텍스트 및 색상 지정
        if (multiplier >= 1.5f)
        {
            effectivenessText.text = "<color=#FF5555>효과가 굉장했다!</color>"; // 빨간색 계열
        }
        else if (multiplier <= 0.5f)
        {
            effectivenessText.text = "<color=#AAAAAA>효과가 별로 없었다...</color>"; // 회색 계열
        }
        else
        {
            effectivenessText.text = "<color=#FFFFFF>효과가 있었다.</color>"; // 흰색
        }

        effectivenessText.gameObject.SetActive(true);

        // 2초간 화면에 표시 후 비활성화
        yield return new WaitForSeconds(2.0f);
        effectivenessText.gameObject.SetActive(false);
    }
}