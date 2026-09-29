using UnityEngine.UI;
using YouYou;
public sealed class UITaskForm : BoundUIForm
{
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        Get<Button>("Task1").onClick.AddListener(() => GameEntry.UI.OpenUIForm(global::UIFormId.UI_TaskDetail));
    }
}
