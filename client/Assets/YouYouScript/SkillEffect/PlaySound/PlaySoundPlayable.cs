using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Playables;
using YouYou;
using static MyCommonEnum;

namespace YouYou
{
    public class PlaySoundPlayable : BasePlayableAsset<PlaySoundPlayableBehaviour, PlaySoundEventArgs>
    {
        [Header("目标点")]
        public DynamicTarget Target;

        [Header("声音名称")]
        public string SoundName;

        protected override void OnYouYouCreatePlayable(ScriptPlayable<PlaySoundPlayableBehaviour> playable)
        {
            base.OnYouYouCreatePlayable(playable);

            CurrPlayableBehavior.CurrArgs.Target = Target;
            CurrPlayableBehavior.CurrArgs.SoundName = SoundName;
        }
    }
}