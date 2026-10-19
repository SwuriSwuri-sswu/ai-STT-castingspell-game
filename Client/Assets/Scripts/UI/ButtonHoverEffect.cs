using UnityEngine;
using UnityEngine.EventSystems; // UI 이벤트 처리를 위해 필수!

public class ButtonHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("타겟팅 이미지 오브젝트")]
    public GameObject targetingEffect;

    // 마우스가 버튼 안으로 들어올 때 (Hover On)
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (targetingEffect != null)
        {
            targetingEffect.SetActive(true);
        }
    }

    // 마우스가 버튼 밖으로 나갈 때 (Hover Off)
    public void OnPointerExit(PointerEventData eventData)
    {
        if (targetingEffect != null)
        {
            targetingEffect.SetActive(false);
        }
    }

    // 버튼을 클릭했을 때 (클릭 시 타겟팅 사라지게 하기)
    public void OnPointerClick(PointerEventData eventData)
    {
        if (targetingEffect != null)
        {
            targetingEffect.SetActive(false);
        }
    }
}