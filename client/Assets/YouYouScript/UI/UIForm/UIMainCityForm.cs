using UnityEngine.UI;
using YouYou;
public sealed class UIMainCityForm : BoundUIForm
{
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        Get<Button>("btnSkill_Normal").onClick.AddListener(() => DispatchInt(CommonEventId.OnPlaySkill, 1));
    }
}
