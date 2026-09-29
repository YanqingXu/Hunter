using UnityEngine;
using UnityEngine.Playables;
using YouYou;

namespace YouYou
{
    public class PlaySoundPlayableBehaviour : BasePlayableBehaviour<PlaySoundEventArgs>
    {
        protected override void OnYouYouBehaviourPlay(Playable playable, FrameData info)
        {
            CurrTimelineCtrl.PlaySound(CurrArgs);
        }

        protected override void OnYouYouBehaviourStop(Playable playable, FrameData info)
        {

        }
    }
}