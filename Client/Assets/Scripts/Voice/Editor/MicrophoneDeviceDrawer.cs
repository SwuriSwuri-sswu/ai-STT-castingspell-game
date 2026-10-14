using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [MicrophoneDevice] 가 붙은 string 필드를 "연결된 마이크 목록 + 시스템 기본 장치" 토글로 표시한다.
/// 하나를 체크하면 나머지는 해제된다 (한 개만 선택).
/// 목록은 Inspector 가 다시 그려질 때 Microphone.devices 에서 새로 읽는다.
/// </summary>
[CustomPropertyDrawer(typeof(MicrophoneDeviceAttribute))]
public class MicrophoneDeviceDrawer : PropertyDrawer
{
    const float Line = 18f;   // 한 줄 높이
    const float Gap = 2f;     // 줄 간격

    /// <summary>저장된 장치 이름이 현재 목록에 없는지 (빈 문자열 = 기본 장치는 항상 있음으로 본다)</summary>
    static bool IsMissing(string value) =>
        !string.IsNullOrEmpty(value) && Array.IndexOf(Microphone.devices, value) < 0;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String) return Line;

        // 제목 1줄 + 기본 장치 1줄 + 장치 수 + (없는 장치 경고 2줄)
        int rows = 2 + Microphone.devices.Length;
        float height = rows * (Line + Gap);
        if (IsMissing(property.stringValue)) height += Line * 2 + Gap;
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.LabelField(position, label.text, "[MicrophoneDevice] 는 string 필드에만 사용할 수 있습니다");
            return;
        }

        EditorGUI.BeginProperty(position, label, property);

        string current = property.stringValue;
        string[] devices = Microphone.devices;
        Rect r = new Rect(position.x, position.y, position.width, Line);

        // 제목
        EditorGUI.LabelField(r, label);
        EditorGUI.indentLevel++;

        // 시스템 기본 장치 (빈 문자열)
        r.y += Line + Gap;
        bool isDefault = string.IsNullOrEmpty(current);
        if (EditorGUI.ToggleLeft(r, "(시스템 기본 장치)", isDefault) && !isDefault)
            property.stringValue = "";

        // 연결된 장치 목록. 이미 선택된 항목을 다시 눌러도 해제되지 않는다 (항상 하나는 선택된 상태)
        foreach (string d in devices)
        {
            r.y += Line + Gap;
            bool selected = current == d;
            if (EditorGUI.ToggleLeft(r, d, selected) && !selected)
                property.stringValue = d;
        }

        if (devices.Length == 0)
        {
            r.y += Line + Gap;
            EditorGUI.LabelField(r, "연결된 마이크가 없습니다");
        }

        // 저장된 장치가 지금 연결되어 있지 않은 경우
        if (IsMissing(current))
        {
            r.y += Line + Gap;
            Rect box = new Rect(r.x, r.y, r.width, Line * 2);
            EditorGUI.HelpBox(box, $"'{current}' 이(가) 연결되어 있지 않아 시스템 기본 장치를 사용합니다.", MessageType.Warning);
        }

        EditorGUI.indentLevel--;
        EditorGUI.EndProperty();
    }
}