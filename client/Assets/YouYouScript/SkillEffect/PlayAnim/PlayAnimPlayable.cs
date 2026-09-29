using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Playables;
using YouYou;
using static MyCommonEnum;

namespace YouYou
{
    public class PlayAnimPlayable : BasePlayableAsset<PlayAnimPlayableBehaviour, PlayAnimEventArgs>
    {
        [Header("目标点")]
        public DynamicTarget Target;

        [Header("动画类型")]
        public RoleAnimCategory Category;

        [Header("动画参数")]
        public int Param = 0;

        protected override void OnYouYouCreatePlayable(ScriptPlayable<PlayAnimPlayableBehaviour> playable)
        {
            base.OnYouYouCreatePlayable(playable);

            CurrPlayableBehavior.CurrArgs.Target = Target;
            CurrPlayableBehavior.CurrArgs.Category = Category;
            CurrPlayableBehavior.CurrArgs.Param = Param;
        }
    }
}