using System.Collections.Generic;
using UnityEngine;
using YouYou;
using static MyCommonEnum;

namespace YouYou
{
    public class PlayAnimEventArgs : IPlayableBehaviourArgs
    {
        /// <summary>
        /// 目标点
        /// </summary>
        public DynamicTarget Target;

        /// <summary>
        /// 动画类型
        /// </summary>
        public RoleAnimCategory Category;

        /// <summary>
        /// 参数
        /// </summary>
        public int Param = 0;

        public void Reset()
        {

        }
    }
}