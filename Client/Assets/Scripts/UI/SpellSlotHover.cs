using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// 스펠 칸에 마우스를 올리면 칸을 살짝 키우고 글자를 발음 로마자로 바꾼다 (SpellBookUI 가 자동으로 붙임)
public class SpellSlotHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [HideInInspector] public TMP_Text label;
    [HideInInspector] public string normalText;   // 평소 글자 (이름)
    [HideInInspector] public string hoverText;    // 올렸을 때 글자 (로마자). 비어 있으면 아무 변화 없음
    [HideInInspector] public Vector3 baseScale = Vector3.one;
    [HideInInspector] public float hoverScale = 1.1f;

    bool hovering;

    public void OnPointerEnter(PointerEventData e)
    {
        if (string.IsNullOrEmpty(hoverText)) return;
        hovering = true;
        transform.localScale = baseScale * hoverScale;
        if (label != null) label.text = hoverText;
    }

    public void OnPointerExit(PointerEventData e) => Restore();
    void OnDisable() => Restore();   // 도감을 닫을 때 원래대로

    void Restore()
    {
        if (!hovering) return;
        hovering = false;
        transform.localScale = baseScale;
        if (label != null) label.text = normalText;
    }
}
