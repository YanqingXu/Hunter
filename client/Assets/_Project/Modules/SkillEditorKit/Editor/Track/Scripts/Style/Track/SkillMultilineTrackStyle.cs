// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class SkillMultilineTrackStyle : SkillTrackStyleBase
{
    #region 常量
    private const string menuAssetPath = "Assets/_Project/Modules/SkillEditorKit/Editor/Track/Assets/MultilineTrackStyle/MultilineTrackMenu.uxml";
    private const string trackAssetPath = "Assets/_Project/Modules/SkillEditorKit/Editor/Track/Assets/MultilineTrackStyle/MultilineTrackContent.uxml";
    private const float headHeight = 35; // 5是间距
    private const float itemHeight = 32; // 2是底部外边距
    #endregion

    private Action addChildTrackAction;
    private Func<int, bool> deleteChildTrackFunc;
    private Action<int, int> swapChildTrackAction;
    private Action<ChildTrack, string> updateTrackNameAction;

    private VisualElement menuItemParent; // 子轨道的菜单父物体

    public List<ChildTrack> childTrackList = new List<ChildTrack>();

    public void Init(VisualElement menuParent, VisualElement contentParent, string title, Action addChildTrackAction, Func<int, bool> deleteChildTrackFunc, Action<int, int> swapChildTrackAction, Action<ChildTrack, string> updateTrackNameAction)
    {
        this.addChildTrackAction = addChildTrackAction;
        this.deleteChildTrackFunc = deleteChildTrackFunc;
        this.swapChildTrackAction = swapChildTrackAction;
        this.updateTrackNameAction = updateTrackNameAction;

        this.menuParent = menuParent;
        this.contentParent = contentParent;

        menuRoot = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(menuAssetPath).Instantiate().Query().ToList()[1];
        menuParent.Add(menuRoot);
        titleLabel = menuRoot.Q<Label>("Title");
        titleLabel.text = title;
        menuItemParent = menuRoot.Q("TackMenuList");

        menuItemParent.RegisterCallback<MouseDownEvent>(ItemParentMouseDown);
        menuItemParent.RegisterCallback<MouseMoveEvent>(ItemParentMouseMove);
        menuItemParent.RegisterCallback<MouseUpEvent>(ItemParentMouseUp);
        menuItemParent.RegisterCallback<MouseOutEvent>(ItemParentMouseOut);


        contentRoot = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(trackAssetPath).Instantiate().Query().ToList()[1];
        contentParent.Add(contentRoot);

        // 添加子轨道的按钮
        Button addButton = menuRoot.Q<Button>("AddButton");
        addButton.clicked += AddButtonClick;
        UpdateSize();
    }

    #region 子轨道鼠标交互
    private bool isDragging = false;
    private int selectTrackIndex = -1;

    private void ItemParentMouseDown(MouseDownEvent evt)
    {
        if (evt.button != 0 || childTrackList.Count == 0 || !SkillTimelineData.IsEditable(SkillEditorWindow.Instance.SkillConfig, out _)) return;
        for (var target = evt.target as VisualElement; target != null && target != menuItemParent; target = target.parent)
            if (target is Button || target is TextField) return;
        // 关闭旧的
        if (selectTrackIndex != -1)
        {
            childTrackList[selectTrackIndex].UnSelect();
        }

        // 通过高度推导出当前交互的是第几个
        float mousePostion = evt.localMousePosition.y - itemHeight / 2;
        selectTrackIndex = GetChildIndexByMousePosition(mousePostion);
        childTrackList[selectTrackIndex].Select();

        isDragging = true;
    }

    private void ItemParentMouseMove(MouseMoveEvent evt)
    {
        if (selectTrackIndex == -1 || isDragging == false) return;
        float mousePostion = evt.localMousePosition.y - itemHeight / 2;
        int mouseTrackIndex = GetChildIndexByMousePosition(mousePostion);
        if (mouseTrackIndex != selectTrackIndex) // 有实际的交换意义
        {
            SwapChildTrack(selectTrackIndex, mouseTrackIndex);
            selectTrackIndex = mouseTrackIndex; // 把选中的轨道更新为鼠标所在的轨道
        }
    }

    private void ItemParentMouseUp(MouseUpEvent evt)
    {
        isDragging = false;
        if (selectTrackIndex != -1)
        {
            childTrackList[selectTrackIndex].UnSelect();
            selectTrackIndex = -1;
        }
    }

    private void ItemParentMouseOut(MouseOutEvent evt)
    {
        // ItemParentMouseOut这个函数经常会无意义调用，因为子物体和我们产生遮挡关系
        // 检测鼠标位置是否真的离开了我们的范围
        if (!menuItemParent.contentRect.Contains(evt.localMousePosition))
        {
            isDragging = false;
            if (selectTrackIndex != -1)
            {
                childTrackList[selectTrackIndex].UnSelect();
                selectTrackIndex = -1;
            }
        }
    }

    private int GetChildIndexByMousePosition(float mousePositionY)
    {
        int trackIndex = Mathf.RoundToInt(mousePositionY / itemHeight);
        trackIndex = Mathf.Clamp(trackIndex, 0, childTrackList.Count - 1);
        return trackIndex;
    }
    #endregion

    private void SwapChildTrack(int index1, int index2)
    {
        if (index1 != index2)
        {
            // 不验证有效性，如果出错，说明本身逻辑就有问题
            ChildTrack childTrack1 = childTrackList[index1];
            ChildTrack childTrack2 = childTrackList[index2];
            childTrackList[index1] = childTrack2;
            childTrackList[index2] = childTrack1;
            UpdateChilds();
            // 上级轨道的实际数据变更
            swapChildTrackAction(index1, index2);
        }
    }

    private void UpdateSize()
    {
        float height = headHeight + (childTrackList.Count * itemHeight); ;
        contentRoot.style.height = height;
        menuRoot.style.height = height;
        menuItemParent.style.height = childTrackList.Count * itemHeight;
    }

    // 添加子轨道
    private void AddButtonClick()
    {
        addChildTrackAction?.Invoke();
    }

    public ChildTrack AddChildTrack()
    {
        ChildTrack childTrack = new ChildTrack();
        childTrack.Init(menuItemParent, childTrackList.Count, contentRoot, DeleteChildTrackAndData, DeleteChildTrack, updateTrackNameAction);
        childTrackList.Add(childTrack);
        UpdateSize();
        return childTrack;
    }

    // 删除子轨道以及子轨道对应的数据
    private void DeleteChildTrackAndData(ChildTrack childTrack)
    {
        isDragging = false; selectTrackIndex = -1;
        if (deleteChildTrackFunc == null) return;
        int index = childTrack.GetIndex();
        if (deleteChildTrackFunc(index))
        {
            childTrack.DoDestroy();
            childTrackList.RemoveAt(index);
            // 所有的子轨道都需要更新一下索引
            UpdateChilds(index);
            UpdateSize();
        }
    }

    // 删除子轨道显示层面
    private void DeleteChildTrack(ChildTrack childTrack)
    {
        int index = childTrack.GetIndex();
        childTrack.DoDestroy();
        childTrackList.RemoveAt(index);
        // 所有的子轨道都需要更新一下索引
        UpdateChilds(index);
        UpdateSize();
    }

    private void UpdateChilds(int startIndex = 0)
    {
        for (int i = startIndex; i < childTrackList.Count; i++)
        {
            childTrackList[i].SetIndex(i);
        }
    }

    #region 拖拽子轨道


    #endregion


    /// <summary>
    /// 多行轨道中的子轨道
    /// </summary>
    public class ChildTrack
    {
        private const string childTrackMenuAssetPath = "Assets/_Project/Modules/SkillEditorKit/Editor/Track/Assets/MultilineTrackStyle/MultilineTrackMenuItem.uxml";
        private const string childTrackContentAssetPath = "Assets/_Project/Modules/SkillEditorKit/Editor/Track/Assets/MultilineTrackStyle/MultilineTrackContentItem.uxml";

        public VisualElement menuRoot;
        public VisualElement trackRoot;

        public VisualElement menuParent;
        public VisualElement trackParent;

        private TextField trackNameField;

        private Action<ChildTrack> deleteAction;
        private Action<ChildTrack> destroyAction;
        private Action<ChildTrack, string> updateTrackNameAction;

        private static Color normalColor = new Color(0, 0, 0, 0);
        private static Color selectColor = Color.green;

        private VisualElement content;
        private int index;
        public void Init(VisualElement menuParent, int index, VisualElement trackParent, Action<ChildTrack> deleteAction, Action<ChildTrack> destroyAction, Action<ChildTrack, string> updateTrackNameAction)
        {
            this.menuParent = menuParent;
            this.trackParent = trackParent;
            this.deleteAction = deleteAction;
            this.destroyAction = destroyAction;
            this.updateTrackNameAction = updateTrackNameAction;

            menuRoot = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(childTrackMenuAssetPath).Instantiate().Query().ToList()[1];
            menuParent.Add(menuRoot);

            trackNameField = menuRoot.Q<TextField>("NameField");
            trackNameField.RegisterCallback<FocusInEvent>(TrackNameFileFieldFocusIn);
            trackNameField.RegisterCallback<FocusOutEvent>(TrackNameFileFieldFocusOut);

            Button deleteButton = menuRoot.Q<Button>("DeleteButton");
            deleteButton.clicked += () => deleteAction(this);

            trackRoot = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(childTrackContentAssetPath).Instantiate().Query().ToList()[1];
            trackParent.Add(trackRoot);

            SetIndex(index);
            UnSelect();
        }

        private string oldTrackNameFiledValue;
        private void TrackNameFileFieldFocusIn(FocusInEvent evt)
        {
            oldTrackNameFiledValue = trackNameField.value;
        }

        private void TrackNameFileFieldFocusOut(FocusOutEvent evt)
        {
            if (oldTrackNameFiledValue != trackNameField.value)
            {
                updateTrackNameAction?.Invoke(this, trackNameField.value);
            }
        }

        public static ChildTrack Shared(VisualElement root)
        {
            return new ChildTrack { trackRoot = root, destroyAction = child => child.content?.RemoveFromHierarchy() };
        }

        public void InitContent(VisualElement content)
        {
            this.content = content;
            if (menuRoot == null) content.style.top = 10;
            trackRoot.Add(content);
        }
        public void SetTrackName(string name)
        {
            if (trackNameField != null) trackNameField.value = name;
        }
        public int GetIndex()
        {
            return index;
        }
        public void SetIndex(int index)
        {
            this.index = index;
            float height = 0;
            Vector3 menuPos = menuRoot.transform.position;
            height = index * itemHeight;
            menuPos.y = height;
            menuRoot.transform.position = menuPos;

            Vector3 trackPos = trackRoot.transform.position;
            height = index * itemHeight + headHeight;
            trackPos.y = height;
            trackRoot.transform.position = trackPos;
        }

        public void Destroy()
        {
            destroyAction(this);
        }

        public void DoDestroy()
        {
            menuParent.Remove(menuRoot);
            trackParent.Remove(trackRoot);
        }

        public void Select()
        {
            menuRoot.style.backgroundColor = selectColor;
        }

        public void UnSelect()
        {
            menuRoot.style.backgroundColor = normalColor;
        }
    }
}

}
