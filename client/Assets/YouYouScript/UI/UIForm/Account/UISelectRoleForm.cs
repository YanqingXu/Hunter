using Google.Protobuf.Collections;
using SuperScrollView;
using UnityEngine.UI;
using YouYou;
using Role = YouYou.Proto.WS2C_ReturnRoleList.Types.WS2C_ReturnRoleList_Item;

public sealed class UISelectRoleForm : BoundUIForm
{
    private RepeatedField<Role> roles;
    private Role selected;
    private LoopListView2 list;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        list = Get<LoopListView2>("ScrollView");
        list.InitListView(0, GetItem);
        Get<Button>("btnCreateRole").onClick.AddListener(() => { GameEntry.UI.OpenUIForm(global::UIFormId.UI_CreateRole, roles?.Count ?? 0); Close(); });
        Get<Button>("btnEnterGame").onClick.AddListener(() =>
        {
            if (selected == null) return;
            GameEntry.Data.UserDataManager.ShareUserData.CurrRoleId = selected.RoleId;
            GameEntry.Data.UserDataManager.ShareUserData.CurrJobId = selected.JobId;
            GameEntry.Procedure.ChangeState(ProcedureState.EnterGame);
            Close();
        });
    }

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        roles = GameEntry.Data.UserDataManager.ReturnRoleListData?.RoleList;
        selected = null;
        list.SetListItemCount(roles?.Count ?? 0, false);
        list.RefreshAllShownItem();
        if (roles != null && roles.Count > 0) SelectRole(roles[0]);
    }

    private LoopListViewItem2 GetItem(LoopListView2 view, int index)
    {
        if (roles == null || index < 0 || index >= roles.Count) return null;
        var item = view.NewListViewItem("ItemPrefab");
        Role role = roles[index];
        item.transform.Find("txtName").GetComponent<Text>().text = role.NickName;
        var job = GameEntry.DataTable.JobList.GetEntity(role.JobId);
        if (job.HasValue) item.transform.Find("imgIconMask/imgIcon").GetComponent<YouYouImage>().LoadImage(job.Value.HeadPic);
        var button = item.transform.Find("btnClick").GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => SelectRole(role));
        return item;
    }

    private void SelectRole(Role role)
    {
        if (selected == role) return;
        selected = role;
        DispatchInt(CommonEventId.OnSelectJobComplete, role.JobId);
    }

    protected override void OnClose() { list.SetListItemCount(0, false); roles = null; selected = null; base.OnClose(); }
}
