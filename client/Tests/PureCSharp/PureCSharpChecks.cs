using System;
using System.Collections;
using System.Linq;
using Google.Protobuf;
using UnityEngine;
using UnityEngine.UI;
using YouYou;
using YouYou.Proto;

/// <summary>Shared by editor content tests and the real IL2CPP hot-patch gameplay tests.</summary>
public static class PureCSharpChecks
{
    public static IEnumerator Run(Action<bool, string> check)
    {
        check(typeof(GameEntry).Assembly.GetType("XLua.LuaEnv") == null &&
              typeof(GameEntry).GetProperty("Lua") == null,
            "Hot gameplay assembly has no embedded xLua runtime or LuaManager entry");
        check(GameEntry.DataTable.JobList.GetList().Count > 0 && GameEntry.DataTable.JobLevelList.GetList().Count > 0,
            "Both former script-side job tables are loaded through the C# FlatBuffers pipeline");
        var data = GameEntry.Data.UserDataManager.ShareUserData;
        data.AccountId = long.MaxValue - 7; data.CurrRoleId = long.MaxValue - 31; data.CurrJobId = 2;
        check(data.AccountId == long.MaxValue - 7 && data.CurrRoleId == long.MaxValue - 31 && data.CurrJobId == 2,
            "C# session data retains full 64-bit account and role IDs without a script bridge");
        data.Dispose();
        check(data.AccountId == 0 && data.CurrRoleId == 0 && data.CurrJobId == 0, "C# session disposal resets all shared fields");

        int[] ids = { UIFormId.UI_Loading, UIFormId.UI_Login, UIFormId.UI_Reg, UIFormId.UI_LogonBG,
                      UIFormId.UI_CreateRole, UIFormId.UI_Task, UIFormId.UI_TaskDetail, UIFormId.UI_MainCity };
        Type[] types = { typeof(UILoadingForm), typeof(UILoginForm), typeof(UIRegisterForm), typeof(UILogonBackgroundForm),
                        typeof(UICreateRoleForm), typeof(UITaskForm), typeof(UITaskDetailForm), typeof(UIMainCityForm) };
        for (int i = 0; i < ids.Length; i++)
        {
            UIFormBase form = null;
            GameEntry.UI.OpenUIForm(ids[i], 1, opened => form = opened);
            yield return Until(() => form, "C# form " + ids[i]);
            yield return null;
            check(form.GetType() == types[i], "Original prefab opens with migrated C# controller: " + types[i].Name);
            if (form is UILoadingForm)
            {
                var arg = new BaseParams { IntParam1 = 1, FloatParam1 = .37f };
                GameEntry.Event.CommonEvent.Dispatch(SysEventId.LoadingProgressChange, arg);
                check(Mathf.Abs(form.GetComponentInChildren<Scrollbar>(true).size - .37f) < .001f,
                    "C# loading form consumes original progress event and updates the bound scrollbar");
            }
            if (form is UIMainCityForm)
            {
                int skill = 0;
                CommonEvent.OnActionHandler handler = arg => skill = ((VarInt)arg).Value;
                GameEntry.Event.CommonEvent.AddEventListener(CommonEventId.OnPlaySkill, handler);
                try
                {
                    Button(form, "btnSkill_Normal").onClick.Invoke();
                    check(skill == 1, "C# skill button dispatches the original pooled VarInt event");
                }
                finally { GameEntry.Event.CommonEvent.RemoveEventListener(CommonEventId.OnPlaySkill, handler); }
            }
            if (form is UILoginForm)
            {
                Button(form, "btnReg").onClick.Invoke();
                yield return Until(() => UnityEngine.Object.FindObjectOfType<UIRegisterForm>(), "Register navigation");
                check(UnityEngine.Object.FindObjectOfType<UIRegisterForm>(), "C# login button navigates to registration without a network request");
                UnityEngine.Object.FindObjectOfType<UIRegisterForm>().Close();
            }
            else form.Close();
            if (ids[i] == UIFormId.UI_LogonBG)
            {
                yield return Until(() => UnityEngine.Object.FindObjectOfType<UILoginForm>(), "Login background navigation");
                UnityEngine.Object.FindObjectOfType<UILoginForm>().Close();
            }
            yield return null;
        }

        var proto = new WS2C_ReturnRoleList();
        proto.RoleList.Add(new WS2C_ReturnRoleList.Types.WS2C_ReturnRoleList_Item
        {
            RoleId = long.MaxValue - 5, JobId = GameEntry.DataTable.JobList.GetList()[0].Id, NickName = "CSharp migration", Level = 1
        });
        int selectedJob = 0;
        CommonEvent.OnActionHandler onSelected = arg => selectedJob = ((VarInt)arg).Value;
        GameEntry.Event.CommonEvent.AddEventListener(CommonEventId.OnSelectJobComplete, onSelected);
        GameEntry.Event.SocketEvent.Dispatch(ProtoIdDefine.Proto_WS2C_ReturnRoleList, proto.ToByteArray());
        yield return Until(() => UnityEngine.Object.FindObjectOfType<UISelectRoleForm>(), "C# role-list protocol route");
        yield return null;
        check(GameEntry.Data.UserDataManager.ReturnRoleListData.RoleList[0].RoleId == long.MaxValue - 5 &&
              selectedJob == proto.RoleList[0].JobId,
            "C# protobuf listener decodes 64-bit role IDs, opens select-role UI and dispatches job selection");
        GameEntry.Event.CommonEvent.RemoveEventListener(CommonEventId.OnSelectJobComplete, onSelected);
        UnityEngine.Object.FindObjectOfType<UISelectRoleForm>().Close();
        GameEntry.Event.SocketEvent.Dispatch(ProtoIdDefine.Proto_WS2C_ReturnRoleList, new WS2C_ReturnRoleList().ToByteArray());
        yield return Until(() => UnityEngine.Object.FindObjectOfType<UICreateRoleForm>(), "Empty role list");
        yield return null;
        var create = UnityEngine.Object.FindObjectOfType<UICreateRoleForm>();
        check(!Button(create, "btnClose").gameObject.activeSelf, "Empty role list opens creation and hides the return button");
        create.Close();
        UIFormBase reopened = null;
        GameEntry.UI.OpenUIForm(UIFormId.UI_CreateRole, 1, form => reopened = form);
        yield return Until(() => reopened, "Reopen creation");
        yield return null;
        check(Button(reopened, "btnClose").gameObject.activeSelf,
            "Reusing the C# create-role form restores the return button for an existing account");
        reopened.Close();
        GameEntry.Data.UserDataManager.Clear();
    }

    private static Button Button(UIFormBase form, string name) => form.GetComponentsInChildren<Button>(true).Single(item => item.name == name);
    private static IEnumerator Until(Func<bool> predicate, string label)
    {
        float end = Time.realtimeSinceStartup + 20;
        while (!predicate()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(label); yield return null; }
    }
}
