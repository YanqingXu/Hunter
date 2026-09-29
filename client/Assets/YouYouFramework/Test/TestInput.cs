using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YouYou;

public class TestInput : MonoBehaviour
{
    public CameraCtrl CameraCtrl;

    public Transform Role;

    // Start is called before the first frame update
    void Start()
    {
        GameEntry.Input.OnClick += Input_OnClick;
        GameEntry.Input.OnBeginDrag += Input_OnBeginDrag;
        GameEntry.Input.OnEndDrag += Input_OnEndDrag;
        GameEntry.Input.OnDrag += Input_OnDrag;
        GameEntry.Input.OnZoom += Input_OnZoom;

        if (GameEntry.Input.Joystick != null)
        {
            GameEntry.Input.Joystick.OnDown = (Vector2 v) =>
            {
                Debug.LogError("OnDown==" + v);
            };
            GameEntry.Input.Joystick.OnChanged = (Vector2 v) =>
            {
                Debug.LogError("OnChanged==" + v);
            };
            GameEntry.Input.Joystick.OnUp = (Vector2 v) =>
            {
                Debug.LogError("OnUp==" + v);
            };
        }
    }

    private void Input_OnZoom(ZoomType obj)
    {
        //Debug.LogError("缩放=" + t1);
        switch (obj)
        {
            case ZoomType.In:
                CameraCtrl.SetCameraZoom(0);
                break;
            case ZoomType.Out:
                CameraCtrl.SetCameraZoom(1);
                break;
        }
    }

    private void Input_OnBeginDrag(TouchEventData t1)
    {
        //Debug.LogError("开始拖拽");
    }

    private void Input_OnEndDrag(TouchEventData t1)
    {
        //Debug.LogError("结束拖拽");
        CameraCtrl.IsOnDrag = false;
        CameraCtrl.OnDragEndDistance = t1.Delta.x;
    }

    private void Input_OnDrag(TouchDirection t1, TouchEventData t2)
    {
        //Debug.LogError("拖拽中=" + t1);
        CameraCtrl.IsOnDrag = true;
        switch (t1)
        {
            case TouchDirection.MoveLeft:
                CameraCtrl.SetCameraRotate(0);
                break;
            case TouchDirection.MoveRight:
                CameraCtrl.SetCameraRotate(1);
                break;
            case TouchDirection.MoveUp:
                CameraCtrl.SetCameraUpAndDown(1);
                break;
            case TouchDirection.MoveDown:
                CameraCtrl.SetCameraUpAndDown(0);
                break;
        }
    }

    private void Input_OnClick(TouchEventData t1)
    {
        //Debug.LogError("点击" + t1.PressPosition);
    }

    private void OnClick(TouchEventData t1)
    {

    }

    // Update is called once per frame
    void Update()
    {
        CameraCtrl.AutoLookAt(Role.position);
    }

    private void OnDestroy()
    {

    }
}
