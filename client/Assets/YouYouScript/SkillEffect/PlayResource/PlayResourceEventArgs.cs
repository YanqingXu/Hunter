using System.Collections.Generic;
using UnityEngine;
using YouYou;
using static MyCommonEnum;

namespace YouYou
{
    public class PlayResourceEventArgs : IPlayableBehaviourArgs
    {
        /// <summary>
        /// 目标点
        /// </summary>
        public DynamicTarget Target;

        /// <summary>
        /// 预设路径
        /// </summary>
        public string PrefabPath;

        /// <summary>
        /// 旋转
        /// </summary>
        public Vector3 Rotation;

        /// <summary>
        /// 缩放
        /// </summary>
        public Vector3 Scale;

        /// <summary>
        /// 偏移
        /// </summary>
        public Vector3 Offset;

        public void Reset()
        {

        }
    }
}