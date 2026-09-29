using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace YouYou
{
    /// <summary>
    /// PC端输入控制器
    /// </summary>
    public class StandalonInputCtrl : InputCtrlBase
    {
        public StandalonInputCtrl(BaseAction<TouchEventData> onClick, BaseAction<TouchEventData> onBeginDrag, BaseAction<TouchEventData> onEndDrag, BaseAction<TouchDirection, TouchEventData> onDrag, BaseAction<ZoomType> onZoom) : base(onClick, onBeginDrag, onEndDrag, onDrag, onZoom)
        {
        }

        internal override void OnUpdate()
        {
            if (Click())
            {
                OnClick?.Invoke(TouchEventData);
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

        /// <summary>
        /// 滑动
        /// </summary>
        /// <param name="touchDirection">滑动方向</param>
        /// <param name="touchEventData">滑动数据</param>
        /// <returns></returns>
        protected override bool Move(TouchDirection touchDirection, TouchEventData touchEventData)
        {
            if (touchDirection != TouchDirection.MoveNone)
            {
                OnDrag?.Invoke(touchDirection, touchEventData);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 点击
        /// </summary>
        /// <returns></returns>
        protected override bool Click()
        {
            if (Input.GetMouseButtonUp(0))
            {
                if (m_IsDraging)
                {
                    return false;
                }
                TouchEventData.PressPosition = Input.mousePosition;
                TouchEventData.LastPosition = Input.mousePosition;
                TouchEventData.TouchTime = Time.time;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 开始拖拽
        /// </summary>
        /// <returns></returns>
        protected override bool BeginDrag()
        {
            if (Input.GetMouseButtonDown(0))
            {
                TouchEventData.PressPosition = Input.mousePosition;
                TouchEventData.StartPosition = Input.mousePosition;
                TouchEventData.LastPosition = Input.mousePosition;
                TouchEventData.TouchTime = Time.time;

                m_IsBeginDrag = true;
                m_IsEndDrag = false;

                OnBeginDrag?.Invoke(TouchEventData);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 拖拽中
        /// </summary>
        /// <returns></returns>
        protected override bool Drag()
        {
            if (m_IsBeginDrag)
            {
                if (Input.GetMouseButton(0))
                {
                    TouchEventData.Delta = new Vector2(Input.mousePosition.x, Input.mousePosition.y) - TouchEventData.LastPosition;
                    TouchEventData.TotalDelta = TouchEventData.LastPosition - new Vector2(TouchEventData.StartPosition.x, TouchEventData.StartPosition.y);

                    if (TouchEventData.Delta.magnitude > 0f)
                    {
                        m_IsDraging = true;
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
                    TouchEventData.LastPosition = Input.mousePosition;

                    return true;
                }

                if (Input.GetMouseButtonUp(0))
                {
                    EndDrag();
                }
            }
            return false;
        }

        /// <summary>
        /// 结束拖拽
        /// </summary>
        /// <returns></returns>
        protected override bool EndDrag()
        {
            if (m_IsBeginDrag)
            {
                TouchEventData.LastPosition = Input.mousePosition;
                TouchEventData.TouchTime = Time.time - TouchEventData.TouchTime;
                m_IsBeginDrag = false;
                m_IsDraging = false;
                m_IsEndDrag = true;

                OnEndDrag?.Invoke(TouchEventData);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 缩放
        /// </summary>
        /// <returns></returns>
        protected override bool Zoom()
        {
            if (Input.GetAxis("Mouse ScrollWheel") < 0)
            {
                OnZoom?.Invoke(ZoomType.Out);
                return true;
            }
            else if (Input.GetAxis("Mouse ScrollWheel") > 0)
            {
                OnZoom?.Invoke(ZoomType.In);
                return true;
            }
            return false;
        }
    }
}