// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using UnityEditor;
using UnityEngine.UIElements;
using UnityEngine;

public class SkillAnimationTrackItemStyle : SkillTrackItemStyleBase
{
    private const string trackItemAssetPath = "Assets/SkillEditorKit/Editor/Track/Assets/TrackItem/AnimationTrackItem.uxml";
    private Label titleLabel;
    public VisualElement mainDragArea { get; private set; }
    public VisualElement animationOverLine { get; private set; }
    private VisualElement blendOverlay;
    private VisualElement blendOutOverlay;
    public void Init(SkillTrackStyleBase tracStyle, int startFrameIndex, float frameUnitWidth)
    {
        titleLabel = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(trackItemAssetPath).Instantiate().Query<Label>();
        root = titleLabel;
        mainDragArea = root.Q<VisualElement>("Main");
        animationOverLine = root.Q<VisualElement>("OverLine");
        blendOverlay = CreateBlendOverlay("AnimationBlendIn", true);
        blendOutOverlay = CreateBlendOverlay("AnimationBlendOut", false);
        root.Add(blendOutOverlay); root.Add(blendOverlay);
        tracStyle.AddItem(root);
    }

    private static VisualElement CreateBlendOverlay(string name, bool incoming)
    {
        var overlay = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
        overlay.style.position = Position.Absolute;
        if (incoming) overlay.style.left = 0; else overlay.style.right = 0;
        overlay.style.top = 0; overlay.style.bottom = 0;
        overlay.style.backgroundColor = new Color(.48f, .65f, 1, .2f);
        if (incoming) overlay.style.borderRightWidth = 1; else overlay.style.borderLeftWidth = 1;
        overlay.style.borderRightColor = overlay.style.borderLeftColor = new Color(.8f, .88f, 1, .8f);
        overlay.generateVisualContent += context =>
        {
            var rect = overlay.contentRect;
            if (rect.width < 1 || rect.height < 1) return;
            var mesh = context.Allocate(3, 3);
            Color32 tint = new Color(.8f, .88f, 1, .22f);
            mesh.SetNextVertex(new Vertex { position = new Vector3(0, incoming ? rect.height : 0, Vertex.nearZ), tint = tint });
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.width, incoming ? 0 : rect.height, Vertex.nearZ), tint = tint });
            mesh.SetNextVertex(new Vertex { position = new Vector3(incoming ? rect.width : 0, rect.height, Vertex.nearZ), tint = tint });
            mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
        };
        var blendLabel = new Label { pickingMode = PickingMode.Ignore };
        blendLabel.style.fontSize = 10; blendLabel.style.unityTextAlign = TextAnchor.LowerLeft;
        blendLabel.style.position = Position.Absolute; blendLabel.style.bottom = 1; blendLabel.style.left = 9;
        overlay.Add(blendLabel);
        return overlay;
    }

    public void SetTitle(string title)
    {
        titleLabel.text = title;
    }

    private static void SetOverlay(VisualElement overlay, int frames, float unit)
    {
        float width = frames * unit;
        overlay.style.display = width > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        overlay.style.width = width;
        var blendLabel = overlay.Q<Label>();
        blendLabel.text = $"融合 {frames} 帧";
        blendLabel.style.display = width >= 42 ? DisplayStyle.Flex : DisplayStyle.None;
        overlay.MarkDirtyRepaint();
    }

    public void SetBlendRanges(int incoming, int outgoing, float unit, int rate)
    {
        SetOverlay(blendOverlay, incoming, unit); SetOverlay(blendOutOverlay, outgoing, unit);
        root.tooltip = titleLabel.text;
        if (incoming > 0) root.tooltip += $"\n融入重叠区：{incoming} 帧 / {incoming / (double)Mathf.Max(1, rate):0.###} 秒";
        if (outgoing > 0) root.tooltip += $"\n融出重叠区：{outgoing} 帧 / {outgoing / (double)Mathf.Max(1, rate):0.###} 秒";
    }
}



}
