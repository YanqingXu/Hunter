using System.Collections.Generic;
using SuperScrollView;
using UnityEngine.UI;
using YouYou;
using YouYou.DataTable;
using YouYou.Proto;

public sealed class UICreateRoleForm : BoundUIForm
{
    private List<DTJob> jobs;
    private int selectedJob;
    private LoopListView2 list;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        list = Get<LoopListView2>("ScrollView");
        list.InitListView(0, GetItem);
        Get<Button>("btnCreate").onClick.AddListener(() =>
        {
            if (selectedJob == 0) return;
            GameEntry.Data.UserDataManager.ShareUserData.CurrJobId = selectedJob;
            GameEntry.Socket.SendMainMsg(new C2WS_CreateRole
                { JobId = selectedJob, Sex = 0, NickName = Get<InputField>("inputNickName").text });
        });
        Get<Button>("btnClose").onClick.AddListener(() => { GameEntry.UI.OpenUIForm(global::UIFormId.UI_SelectRole); Close(); });
    }

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        Get<Button>("btnClose").gameObject.SetActive(userData is int count && count > 0);
        jobs = GameEntry.DataTable.JobList.GetList();
        selectedJob = 0;
        list.SetListItemCount(jobs.Count, false);
        list.RefreshAllShownItem();
        if (jobs.Count > 0) SelectJob(jobs[0].Id);
    }

    private LoopListViewItem2 GetItem(LoopListView2 view, int index)
    {
        if (jobs == null || index < 0 || index >= jobs.Count) return null;
        var item = view.NewListViewItem("ItemPrefab");
        var job = jobs[index];
        item.transform.Find("txtName").GetComponent<Text>().text = GameEntry.Localization.GetString(job.Name);
        item.transform.Find("imgIconMask/imgIcon").GetComponent<YouYouImage>().LoadImage(job.HeadPic);
        var button = item.transform.Find("btnClick").GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => SelectJob(job.Id));
        return item;
    }

    private void SelectJob(int id)
    {
        if (selectedJob == id) return;
        selectedJob = id;
        DispatchInt(CommonEventId.OnSelectJobComplete, id);
    }

    protected override void OnClose() { list.SetListItemCount(0, false); jobs = null; selectedJob = 0; base.OnClose(); }
}
