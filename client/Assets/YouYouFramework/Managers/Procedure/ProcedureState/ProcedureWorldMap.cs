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
using YouYou.Proto;

namespace YouYou
{
    /// <summary>
    /// 世界地图流程
    /// </summary>
    public class ProcedureWorldMap : ProcedureBase
    {
        public override void OnEnter()
        {
            base.OnEnter();
            GameEntry.Log(LogCategory.Procedure, "OnEnter ProcedureWorldMap");

            GameEntry.Event.CommonEvent.AddEventListener(CommonEventId.OnPlaySkill, OnPlaySkill);

            //加载场景
            GameEntry.Scene.LoadScene(GameEntry.Data.UserDataManager.CurrSceneId, false, onComplete: () =>
            {
                GameEntry.Event.CommonEvent.Dispatch(SysEventId.CloseCheckVersionUI);

                LoadWorldMapComplete();
            });

            GameEntry.Input.OnClick += Input_OnClick;
            GameEntry.Input.OnBeginDrag += Input_OnBeginDrag;
            GameEntry.Input.OnEndDrag += Input_OnEndDrag;
            GameEntry.Input.OnDrag += Input_OnDrag;
            GameEntry.Input.OnZoom += Input_OnZoom;

            //加载主界面
            GameEntry.UI.OpenUIForm(UIFormId.UI_MainCity);
            GameEntry.UI.OpenUIForm(UIFormId.UI_Chat);

            //加载摇杆
            GameEntry.UI.OpenUIForm(UIFormId.UI_Joystick, null, (UIFormBase form) =>
            {
                GameEntry.Input.Joystick.OnChanged = (Vector2 v) =>
                {
                    GameEntry.Data.RoleDataManager.CurrPlayer.JoystickMove(v);
                };
                GameEntry.Input.Joystick.OnUp = (Vector2 v) =>
                {
                    GameEntry.Data.RoleDataManager.CurrPlayer.JoystickStop(v);
                };
            });
        }

        private void OnPlaySkill(object userData)
        {
            if (GameEntry.Data.RoleDataManager.CurrPlayer.CurrState == MyCommonEnum.RoleFSMState.Attack
                || GameEntry.Data.RoleDataManager.CurrPlayer.CurrState == MyCommonEnum.RoleFSMState.Die
                )
            {

                return;
            }

            VarInt varInt = userData as VarInt;

            DTSkillLevel dTSkillLevel = GameEntry.DataTable.SkillLevelList.GetEntityValue(varInt.Value);

            GameEntry.Data.RoleDataManager.PlaySkill(dTSkillLevel.SkillId);
        }

        /// <summary>
        /// 加载PVP场景完毕
        /// </summary>
        private void LoadWorldMapComplete()
        {
            GameEntry.Data.RoleDataManager.CreatePlayerByJobId(GameEntry.Data.UserDataManager.ShareUserData.CurrJobId, (RoleCtrl roleCtrl) =>
            {
                GameEntry.Data.RoleDataManager.CurrPlayer = roleCtrl;
                GameEntry.Data.RoleDataManager.CurrPlayer.IsPlayer = true;
                GameEntry.Data.RoleDataManager.CurrPlayer.ServerRoleId = GameEntry.Data.UserDataManager.ShareUserData.CurrRoleId;
                GameEntry.Data.RoleDataManager.CurrPlayer.tag = MyConstDefine.PlayerTag;

                roleCtrl.RoleInfo.InitCurrPlayerInfo(roleCtrl);

                roleCtrl.CurrRoleType = MyCommonEnum.RoleType.CurrPlayer;

                //设置角色坐标位置
                roleCtrl.transform.position = GameEntry.Data.UserDataManager.CurrPos;
                roleCtrl.transform.rotation = Quaternion.Euler(0, GameEntry.Data.UserDataManager.RotationY, 0);
                roleCtrl.OpenAgent();

                GameEntry.Data.RoleDataManager.EnterScene(GameEntry.Data.UserDataManager.CurrSceneId);

                //打开摄像机
                GameEntry.CameraCtrl.transform.position = roleCtrl.transform.position;
                GameEntry.CameraCtrl.AutoLookAt(roleCtrl.transform.position);

                ////var lst = GameEntry.DataTable.SkillLevelList.GetList();
                ////foreach (var item in lst)
                ////{
                ////    Debug.LogError("id ==" + item.Id);
                ////    Debug.LogError("id ArgsLength ==" + item.ArgsLength);
                ////    for (int j = 0; j < item.ArgsLength; j++)
                ////    {
                ////        Debug.LogError("args==" + item.Args(j));
                ////    }
                ////}

                //var lst2 = GameEntry.DataTable.BuffList.GetList();
                //foreach (var item in lst2)
                //{
                //    Debug.LogError("id ==" + item.Id);
                //    Debug.LogError("id Name ==" + item.BuffName);
                //}
            });
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
        }

        public override void OnLeave()
        {
            base.OnLeave();

            GameEntry.Event.CommonEvent.RemoveEventListener(CommonEventId.OnPlaySkill, OnPlaySkill);

            GameEntry.Log(LogCategory.Procedure, "OnEnter ProcedureWorldMap");
            GameEntry.Input.OnClick -= Input_OnClick;
            GameEntry.Input.OnBeginDrag -= Input_OnBeginDrag;
            GameEntry.Input.OnEndDrag -= Input_OnEndDrag;
            GameEntry.Input.OnDrag -= Input_OnDrag;
            GameEntry.Input.OnZoom -= Input_OnZoom;

            //关闭摇杆
            GameEntry.UI.CloseUIForm(UIFormId.UI_Joystick);
            GameEntry.UI.CloseUIForm(UIFormId.UI_MainCity);
            GameEntry.UI.CloseUIForm(UIFormId.UI_Chat);
        }

        private void Input_OnClick(TouchEventData t1)
        {
            //防止UI穿透
            if (GameEntry.Input.IsPointerOverGameObject(Input.mousePosition))
            {
                return;
            }

            if (GameEntry.Data.RoleDataManager.CurrPlayer == null) return;

            Ray ray = GameEntry.CameraCtrl.MainCamera.ScreenPointToRay(Input.mousePosition);
            RaycastHit hitInfo;

            if (Physics.Raycast(ray, out hitInfo, 1000f, 1 << LayerMask.NameToLayer("Role")))
            {
                GameObject role = hitInfo.collider.gameObject;
                RoleCtrl roleCtrl = role.GetComponent<RoleCtrl>();

                //设置当前锁定的角色
                GameEntry.Data.CacheDataManager.CurrLockRole = roleCtrl;

                Debug.LogError(roleCtrl.ServerRoleId);
                Debug.LogError(roleCtrl.CurrRoleType);

                //GameEntry.Data.RoleDataManager.ClickMove(GameEntry.Data.RoleDataManager.CurrPlayer.transform.position, hitInfo.point);
                //GameEntry.Data.RoleDataManager.CurrPlayer.ClickMove(hitInfo.point);
            }
            else if (Physics.Raycast(ray, out hitInfo, 1000f, 1 << LayerMask.NameToLayer("Ground")))
            {
                GameEntry.Data.RoleDataManager.ClickMove(GameEntry.Data.RoleDataManager.CurrPlayer.transform.position, hitInfo.point);
                GameEntry.Data.RoleDataManager.CurrPlayer.ClickMove(hitInfo.point);

                GameEntry.Data.CacheDataManager.CurrLockRole = null;
            }
        }

        private void Input_OnBeginDrag(TouchEventData t1)
        {

        }

        private void Input_OnDrag(TouchDirection t1, TouchEventData t2)
        {
            //防止UI穿透
            if (GameEntry.Input.IsPointerOverGameObject(Input.mousePosition))
            {
                return;
            }

            //摇杆拖拽中 禁止滑动摄像机
            if (GameEntry.Input.Joystick != null && GameEntry.Input.Joystick.IsDraging)
            {
                return;
            }

            GameEntry.CameraCtrl.IsOnDrag = true;
            switch (t1)
            {
                case TouchDirection.MoveLeft:
                    GameEntry.CameraCtrl.SetCameraRotate(0);
                    break;
                case TouchDirection.MoveRight:
                    GameEntry.CameraCtrl.SetCameraRotate(1);
                    break;
                case TouchDirection.MoveUp:
                    GameEntry.CameraCtrl.SetCameraUpAndDown(1);
                    break;
                case TouchDirection.MoveDown:
                    GameEntry.CameraCtrl.SetCameraUpAndDown(0);
                    break;
            }
        }

        private void Input_OnEndDrag(TouchEventData t1)
        {
            GameEntry.CameraCtrl.IsOnDrag = false;
            GameEntry.CameraCtrl.OnDragEndDistance = t1.Delta.x;
        }

        private void Input_OnZoom(ZoomType t1)
        {
            //防止UI穿透
            if (GameEntry.Input.IsPointerOverGameObject(Input.mousePosition))
            {
                return;
            }

            switch (t1)
            {
                case ZoomType.In:
                    GameEntry.CameraCtrl.SetCameraZoom(0);
                    break;
                case ZoomType.Out:
                    GameEntry.CameraCtrl.SetCameraZoom(1);
                    break;
            }
        }
    }
}