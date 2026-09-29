using System;
using System.Collections.Generic;
using System.Globalization;
using LitJson;
using UnityEngine.UI;
using YouYou;

/// <summary>登录和注册共用账号请求、校验与会话更新。</summary>
public abstract class UIAccountForm : BoundUIForm
{
    protected abstract bool IsRegistration { get; }
    private int requestVersion;
    private bool busy;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        Get<Button>(IsRegistration ? "btnReg" : "btnLogin").onClick.AddListener(Submit);
        Get<Button>(IsRegistration ? "btnLogin" : "btnReg").onClick.AddListener(() =>
        {
            Close();
            GameEntry.UI.OpenUIForm(IsRegistration ? global::UIFormId.UI_Login : global::UIFormId.UI_Reg);
        });
    }

    private void Submit()
    {
        if (busy) return;
        string userName = Get<InputField>("inputUserName").text;
        string password = Get<InputField>("inputPassword").text;
        if (string.IsNullOrEmpty(userName)) { GameEntry.UI.OpenDialogFormBySysCode(SysCode.Input_UserNameEmpty); return; }
        if (string.IsNullOrEmpty(password)) { GameEntry.UI.OpenDialogFormBySysCode(SysCode.Input_PwdEmpty); return; }
        var payload = new Dictionary<string, object>
        {
            ["ChannelId"] = GameEntry.Data.SysDataManager.CurrChannelConfig.ChannelId,
            ["Type"] = IsRegistration ? 0 : 1, ["UserName"] = userName, ["Password"] = password
        };
        busy = true;
        int version = ++requestVersion;
        var owner = GameEntry.Instance;
        GameEntry.Http.SendData(GameEntry.Http.RealWebAccountUrl + "/account", args =>
        {
            // A closed/reused window or retired framework must not consume an old response.
            if (!this || version != requestVersion || !ReferenceEquals(owner, GameEntry.Instance)) return;
            busy = false;
            if (args.HasError) { GameEntry.UI.OpenDialogFormBySysCode(SysCode.Connect_TimeOut); return; }
            RetValue result;
            long accountId;
            try
            {
                result = JsonMapper.ToObject<RetValue>(args.Value);
                if (result.HasError) { GameEntry.UI.OpenDialogFormBySysCode(result.ErrorCode); return; }
                accountId = long.Parse(JsonMapper.ToObject(result.Value.ToString())["YFId"].ToString(), CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                GameEntry.LogError("账号服务返回的数据格式不正确。");
                GameEntry.UI.OpenDialogFormBySysCode(SysCode.Connect_TimeOut);
                return;
            }
            GameEntry.Data.UserDataManager.ShareUserData.AccountId = accountId;
            Close();
            GameEntry.Procedure.ChangeState(ProcedureState.SelectRole);
        }, true, false, payload);
    }

    protected override void OnClose() { requestVersion++; busy = false; base.OnClose(); }
    protected override void OnBeforeDestroy() { requestVersion++; busy = false; base.OnBeforeDestroy(); }
}
