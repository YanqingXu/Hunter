using YouYou;
public sealed class UILogonBackgroundForm : BoundUIForm
{
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        GameEntry.UI.OpenUIForm(global::UIFormId.UI_Login);
    }
}
