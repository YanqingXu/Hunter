using UnityEngine;
using UnityEngine.UI;
using YouYou;
public sealed class UILoadingForm : BoundUIForm
{
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        GameEntry.Event.CommonEvent.AddEventListener(SysEventId.LoadingProgressChange, OnProgress);
    }
    private void OnProgress(object userData)
    {
        if (!(userData is BaseParams args)) return;
        float progress = Mathf.Clamp01(args.FloatParam1);
        if (args.IntParam1 == 1) Get<Text>("txtTip").text = GameEntry.Localization.GetString("Loading.ChangeScene", Mathf.FloorToInt(progress * 100));
        Get<Scrollbar>("Scrollbar").size = progress;
    }
    protected override void OnClose()
    {
        GameEntry.Event?.CommonEvent.RemoveEventListener(SysEventId.LoadingProgressChange, OnProgress);
        base.OnClose();
    }
}
