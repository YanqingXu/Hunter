using UnityEngine;
using UnityEngine.Playables;
using YouYou;

namespace YouYou
{
    public class PlayResourcePlayableBehaviour : BasePlayableBehaviour<PlayResourceEventArgs>
    {
        protected override void OnYouYouBehaviourPlay(Playable playable, FrameData info)
        {
            CurrTimelineCtrl.PlayResource(CurrArgs);
        }

        protected override void OnYouYouBehaviourStop(Playable playable, FrameData info)
        {

        }
    }
}