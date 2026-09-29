using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace YouYou
{
    /// <summary>
    /// 触屏事件数据
    /// </summary>
    public class TouchEventData
    {
        /// <summary>
        /// 当前位置
        /// </summary>
        public Vector2 PressPosition;

        /// <summary>
        /// 距离上一次变更的距离
        /// </summary>
        public Vector2 Delta;

        /// <summary>
        /// 从开始到现在变更的距离
        /// </summary>
        public Vector2 TotalDelta;

        /// <summary>
        /// 开始位置
        /// </summary>
        public Vector2 StartPosition;

        /// <summary>
        /// 最后位置
        /// </summary>
        public Vector2 LastPosition;

        /// <summary>
        /// 触屏时间
        /// </summary>
        public float TouchTime;
    }
}