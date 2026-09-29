// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class SkillCustomEventInspector : SkillEventDataInspectorBase<EventTrackItem, EventTrack>
{
    private List<string> eventTypeChoiceList;
    public override void OnDraw()
    {
        eventTypeChoiceList = new List<string>(Enum.GetNames(typeof(SkillEventType)));

        // 类型选择
        DropdownField eventTypeDropDownField = new DropdownField("事件类型", eventTypeChoiceList, (int)trackItem.CustomEvent.EventType);
        eventTypeDropDownField.RegisterValueChangedCallback(OnEventDropDownFieldValueChanged);
        root.Add(eventTypeDropDownField);

        if (trackItem.CustomEvent.EventType == SkillEventType.Custom)
        {
            // 名称
            TextField nameField = new TextField("事件名称");
            nameField.value = trackItem.CustomEvent.CustomEventName;
            nameField.RegisterValueChangedCallback(OnEventNameFieldValueChanged);
            root.Add(nameField);
        }
        // 参数
        IntegerField intArgField = new IntegerField("Int参数");
        intArgField.value = trackItem.CustomEvent.IntArg;
        intArgField.RegisterValueChangedCallback(OnEventIntArgFieldValueChanged);
        root.Add(intArgField);

        FloatField floatArgField = new FloatField("Float参数");
        floatArgField.value = trackItem.CustomEvent.FloatArg;
        floatArgField.RegisterValueChangedCallback(OnEventFloatArgFieldValueChanged);
        root.Add(floatArgField);

        TextField stringArgField = new TextField("String参数");
        stringArgField.value = trackItem.CustomEvent.StringArg;
        stringArgField.RegisterValueChangedCallback(OnEventStringArgFieldValueChanged);
        root.Add(stringArgField);

        ObjectField objectArgField = new ObjectField("Object参数");
        objectArgField.objectType = typeof(UnityEngine.Object);
        objectArgField.allowSceneObjects = false;
        objectArgField.value = trackItem.CustomEvent.ObjectArg;
        objectArgField.RegisterValueChangedCallback(OnEventObjectArgFieldValueChanged);
        root.Add(objectArgField);


        // 删除
        Button deleteButton = new Button(DeleteEventTrackItemButtonClick);
        deleteButton.text = "删除";
        deleteButton.style.backgroundColor = new Color(1, 0, 0, 0.5f);
        root.Add(deleteButton);
    }
    private void OnEventDropDownFieldValueChanged(ChangeEvent<string> evt)
    {
        SkillEventType eventType = (SkillEventType)eventTypeChoiceList.IndexOf(evt.newValue);
        SkillEditorChangeUtility.Apply("修改技能事件类型", () =>
        {
            trackItem.CustomEvent.EventType = eventType;
            if (eventType != SkillEventType.Custom) trackItem.CustomEvent.CustomEventName = "";
        });
        SkillEditorInspector.Instance?.Show();
    }
    private void OnEventNameFieldValueChanged(ChangeEvent<string> evt)
    {
        SkillEditorChangeUtility.Apply("修改技能事件名称", () => trackItem.CustomEvent.CustomEventName = evt.newValue);
    }
    private void OnEventIntArgFieldValueChanged(ChangeEvent<int> evt)
    {
        SkillEditorChangeUtility.Apply("修改技能事件整数参数", () => trackItem.CustomEvent.IntArg = evt.newValue);
    }

    private void OnEventFloatArgFieldValueChanged(ChangeEvent<float> evt)
    {
        SkillEditorChangeUtility.Apply("修改技能事件浮点参数", () => trackItem.CustomEvent.FloatArg = evt.newValue);
    }

    private void OnEventStringArgFieldValueChanged(ChangeEvent<string> evt)
    {
        SkillEditorChangeUtility.Apply("修改技能事件文本参数", () => trackItem.CustomEvent.StringArg = evt.newValue);
    }

    private void OnEventObjectArgFieldValueChanged(ChangeEvent<UnityEngine.Object> evt)
    {
        SkillEditorChangeUtility.Apply("修改技能事件对象参数", () => trackItem.CustomEvent.ObjectArg = evt.newValue);
    }

    private void DeleteEventTrackItemButtonClick()
    {
        SkillEditorClipboard.DeleteSelected();
    }

}

}
