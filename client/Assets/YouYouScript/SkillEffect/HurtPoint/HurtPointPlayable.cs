using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

namespace YouYou
{
    public class HurtPointPlayable : BasePlayableAsset<HurtPointPlayableBehaviour, HurtPointEventArgs>
    {
        protected override void OnYouYouCreatePlayable(ScriptPlayable<HurtPointPlayableBehaviour> playable)
        {
            base.OnYouYouCreatePlayable(playable);
        }
    }
}