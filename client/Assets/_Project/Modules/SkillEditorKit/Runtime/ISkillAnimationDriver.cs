using System;
using UnityEngine;

namespace SkillEditorKit
{
    /// <summary>Use SkillAnimationPlayer, or implement this interface with the host project's animator.</summary>
    public interface ISkillAnimationDriver
    {
        Action<double> PlaySkillAnimation(SkillAnimationEvent data, int frameRate,
            SkillAnimationEvent outgoing = null, double outgoingElapsedAtStart = 0, double overlapSeconds = 0);
        void StopSkillAnimation();
        void SetRootMotionAction(Action<Vector3, Quaternion> action);
        void ClearRootMotionAction();
    }
}
