using System.Collections.Generic;
using UnityEngine;
using YouYou;
using static MyCommonEnum;

namespace YouYou
{
    public class PlaySoundEventArgs : IPlayableBehaviourArgs
    {
        /// <summary>
        /// 目标点
        /// </summary>
        public DynamicTarget Target;

        /// <summary>
        /// 声音名称
        /// </summary>
        public string SoundName;

        public void Reset()
        {
            SoundName = null;
        }
    }
}