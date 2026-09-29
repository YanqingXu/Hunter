using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace YouYou
{
    /// <summary>
    /// 移动端输入控制器
    /// </summary>
    public class MobileInputCtrl : InputCtrlBase
    {
        public MobileInputCtrl(BaseAction<TouchEventData> onClick, BaseAction<TouchEventData> onBeginDrag, BaseAction<TouchEventData> onEndDrag, BaseAction<TouchDirection, TouchEventData> onDrag, BaseAction<ZoomType> onZoom) : base(onClick, onBeginDrag, onEndDrag, onDrag, onZoom)
        {
        }

        /// <summary>
        /// 上一操作
        /// </summary>
        private sbyte m_PrevFinger = -1;

        /// <summary>
        /// 上一次的触摸数量
        /// </summary>
        private byte m_PrevTouchCount = 0;

        /// <summary>
        /// 当前手指的Id
        /// </summary>
        private int m_CurrTouchFingerId = 1;

        /// <summary>
        /// 当前手指
        /// </summary>
        private Touch m_CurrTouch = new Touch();

        /// <summary>
        /// 第一个手指位置
        /// </summary>
        private Vector2 m_TempFinger1Pos;

        /// <summary>
        /// 第一个手指旧位置
        /// </summary>
        private Vector2 m_OldFinger1Pos;

        /// <summary>
        /// 第二个手指位置
        /// </summary>
        private Vector2 m_TempFinger2Pos;

        /// <summary>
        /// 第二个手指旧位置
        /// </summary>
        private Vector2 m_OldFinger2Pos;

        protected override bool BeginDrag()
        {
            if (Input.touchCount > 0)
            {
                m_CurrTouch = Input.GetTouch(0);
                m_CurrTouchFingerId = Input.GetTouch(0).fingerId;
                TouchEventData.PressPosition = Input.GetTouch(0).position;
                TouchEventData.StartPosition = Input.GetTouch(0).position;
                TouchEventData.LastPosition = Input.GetTouch(0).position;
                TouchEventData.TouchTime = Time.time;

                m_PrevFinger = 1;
                m_IsBeginDrag = true;
                m_IsEndDrag = false;

                OnBeginDrag?.Invoke(TouchEventData);
                return true;
            }
            return false;
        }

        protected override bool Click()
        {
            return false;
        }

        protected override bool Drag()
        {
            if (Input.touchCount == 0)
            {
                m_PrevTouchCount = 0;
                m_CurrTouchFingerId = -1;

                if (m_IsBeginDrag)
                {
                    EndDrag();
                }
                return false;
            }

            if (Input.touchCount == 1)
            {
                if (m_PrevTouchCount == 2)
                {
                    TouchEventData.LastPosition = Input.GetTouch(0).position;
                    m_CurrTouchFingerId = Input.GetTouch(0).fingerId;
                    m_PrevTouchCount = 1;
                }
            }
            else if (Input.touchCount == 2)
            {
                if (m_PrevTouchCount < 2)
                {
                    m_PrevTouchCount = 2;
                }
            }

            for (int i = 0; i < Input.touchCount; i++)
            {
                if (Input.GetTouch(i).fingerId == m_CurrTouchFingerId)
                {
                    m_CurrTouch = Input.GetTouch(i);
                    break;
                }
            }

            if (m_CurrTouch.phase != TouchPhase.Ended && m_CurrTouch.phase != TouchPhase.Canceled)
            {
                Vector2 touPos = m_CurrTouch.position;
                TouchEventData.Delta = touPos - TouchEventData.LastPosition;
                TouchEventData.TotalDelta = TouchEventData.LastPosition - new Vector2(TouchEventData.StartPosition.x, TouchEventData.StartPosition.y);

                if (TouchEventData.Delta.magnitude > 0f)
                {
                    m_IsDraging = true;
                    m_PrevFinger = 2;
                    //判断手势移动的方向
                    if (TouchEventData.Delta.y > TouchEventData.Delta.x && TouchEventData.Delta.y > -TouchEventData.Delta.x)
                    {
                        //向上
                        Move(TouchDirection.MoveUp, TouchEventData);
                    }
                    else if (TouchEventData.Delta.y < TouchEventData.Delta.x && TouchEventData.Delta.y < -TouchEventData.Delta.x)
                    {
                        //向下
                        Move(TouchDirection.MoveDown, TouchEventData);
                    }
                    else if (TouchEventData.Delta.y < TouchEventData.Delta.x && TouchEventData.Delta.y > -TouchEventData.Delta.x)
                    {
                        //向右
                        Move(TouchDirection.MoveRight, TouchEventData);
                    }
                    else if (TouchEventData.Delta.y > TouchEventData.Delta.x && TouchEventData.Delta.y < -TouchEventData.Delta.x)
                    {
                        //向左
                        Move(TouchDirection.MoveLeft, TouchEventData);
                    }
                    else
                    {
                        Move(TouchDirection.MoveNone, TouchEventData);
                    }
                }
                TouchEventData.LastPosition = touPos;
                return true;
            }

            return false;
        }

        protected override bool EndDrag()
        {
            if (m_IsBeginDrag)
            {
                TouchEventData.LastPosition = m_CurrTouch.position;
                TouchEventData.TouchTime = Time.time - TouchEventData.TouchTime;
                m_IsBeginDrag = false;
                m_IsDraging = false;
                m_IsEndDrag = true;
                OnEndDrag?.Invoke(TouchEventData);

                if (m_PrevFinger == 1)
                {
                    m_PrevFinger = 3;
                }
                return true;
            }
            return false;
        }

        protected override bool Move(TouchDirection touchDirection, TouchEventData touchEventData)
        {
            if (touchDirection != TouchDirection.MoveNone)
            {
                OnDrag?.Invoke(touchDirection, touchEventData);
                return true;
            }
            return false;
        }

        protected override bool Zoom()
        {
            if (Input.touchCount > 1)
            {
                if (Input.GetTouch(0).phase == TouchPhase.Moved || Input.GetTouch(1).phase == TouchPhase.Moved)
                {
                    m_TempFinger1Pos = Input.GetTouch(0).position;
                    m_TempFinger2Pos = Input.GetTouch(1).position;

                    if (Vector2.Distance(m_OldFinger1Pos, m_OldFinger2Pos) < Vector2.Distance(m_TempFinger1Pos, m_TempFinger2Pos))
                    {
                        //放大
                        OnZoom?.Invoke(ZoomType.In);
                    }
                    else
                    {
                        //缩小
                        OnZoom?.Invoke(ZoomType.Out);
                    }

                    m_OldFinger1Pos = m_TempFinger1Pos;
                    m_OldFinger2Pos = m_TempFinger2Pos;
                    return true;
                }
            }
            return false;
        }

        internal override void OnUpdate()
        {
            if (Input.touchCount == 0)
            {
                m_PrevTouchCount = 0;
                m_CurrTouchFingerId = -1;

                if (!m_IsBeginDrag && m_PrevFinger == 3)
                {
                    m_PrevFinger = -1;
                    OnClick?.Invoke(TouchEventData);
                }

                if (m_IsBeginDrag)
                {
                    EndDrag();
                }
                return;
            }

            if (!m_IsBeginDrag)
            {
                BeginDrag();
            }
            else
            {
                Drag();
            }

            Zoom();
        }
    }
}