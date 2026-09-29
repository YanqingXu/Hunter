using UnityEngine;
using UnityEngine.Playables;
using YouYou;

namespace YouYou
{
    public class PlayAnimPlayableBehaviour : BasePlayableBehaviour<PlayAnimEventArgs>
    {
        protected override void OnYouYouBehaviourPlay(Playable playable, FrameData info)
        {
            CurrTimelineCtrl.PlayAnim(CurrArgs);
        }

        protected override void OnYouYouBehaviourStop(Playable playable, FrameData info)
        {

        }
    }
}