//===================================================
//作    者：边涯  http://www.u3dol.com
//创建时间：
//备    注：
//===================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YouYou.DataTable;

namespace YouYou
{
    /// <summary>
    /// 选人流程
    /// </summary>
    public class ProcedureSelectRole : ProcedureBase
    {
        /// <summary>
        /// 当前选择的角色
        /// </summary>
        private Transform m_CurrSelectRole;

        public override void OnEnter()
        {
            base.OnEnter();
            GameEntry.Log(LogCategory.Procedure, "OnEnter ProcedureSelectRole");
            GameEntry.Event.CommonEvent.AddEventListener(CommonEventId.OnRegClientComplete, OnRegClientComplete);
            GameEntry.Event.CommonEvent.AddEventListener(CommonEventId.OnSelectJobComplete, OnSelectJobComplete);
            //打开区服列表 这里暂时不做 大家自行实现

            //进入区服
            ConnectServer("192.168.0.135", 1304);
        }

        /// <summary>
        /// 连接到网关服务器
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="port"></param>
        private void ConnectServer(string ip, int port)
        {
            GameEntry.Socket.ConnectToMainSocket(ip, port, (bool result) =>
            {
                //Debug.LogError("resul==" + result);
                if (result)
                {
                    GameEntry.Data.UserDataManager.RegClient();
                }
                else
                {
                    //连接失败
                    GameEntry.UI.OpenDialogFormBySysCode(SysCode.Connect_TimeOut);
                }
            });
        }

        public override void OnLeave()
        {
            base.OnLeave();
            GameEntry.Log(LogCategory.Procedure, "OnLeave ProcedureSelectRole");
            GameEntry.Event.CommonEvent.RemoveEventListener(CommonEventId.OnRegClientComplete, OnRegClientComplete);
            GameEntry.Event.CommonEvent.RemoveEventListener(CommonEventId.OnSelectJobComplete, OnSelectJobComplete);

            if (Application.isPlaying && m_CurrSelectRole != null)
            {
                GameEntry.Pool.GameObjectDespawn(m_CurrSelectRole);
            }
            GameEntry.UI.CloseUIForm(UIFormId.UI_CreateRole);
        }

        private void OnRegClientComplete(object userData)
        {
            VarBool varBool = userData as VarBool;
            if (varBool)
            {
                //连接成功
                //加载选择角色场景
                GameEntry.Scene.LoadScene(SysScene.SelectRole, true, onComplete: () =>
                {
                    GameEntry.Instance.StartCoroutine(LoadSceneComplete());
                });
            }
        }

        /// <summary>
        /// 加载场景完毕
        /// </summary>
        /// <returns></returns>
        private IEnumerator LoadSceneComplete()
        {
            yield return new WaitUntil(() => SelectRoleSceneCtrl.Instance != null);
            GameEntry.UI.CloseUIForm(UIFormId.UI_LogonBG);
            GameEntry.Data.UserDataManager.GetRoleList();
        }

        private void OnSelectJobComplete(object userData)
        {
            VarInt varInt = userData as VarInt;
            DTJob dTJob = GameEntry.DataTable.JobList.GetEntityValue(varInt.Value);

            //回池旧的
            if (m_CurrSelectRole != null)
            {
                GameEntry.Pool.GameObjectDespawn(m_CurrSelectRole);
            }

            DTBaseRole baseRole = GameEntry.DataTable.BaseRoleList.GetEntityValue(dTJob.BaseRoleId);
            GameEntry.Pool.GameObjectSpawn(baseRole.PrefabId, (Transform trans, bool isNewInstance) =>
            {
                trans.SetParent(SelectRoleSceneCtrl.Instance.RoleContainer);
                trans.localPosition = Vector3.zero;
                trans.localScale = Vector3.one;
                trans.localEulerAngles = Vector3.zero;

                m_CurrSelectRole = trans;
            });
        }
    }
}