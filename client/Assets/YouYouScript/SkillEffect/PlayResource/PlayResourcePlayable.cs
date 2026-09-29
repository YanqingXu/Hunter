using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Playables;
using YouYou;
using static MyCommonEnum;

namespace YouYou
{
    public class PlayResourcePlayable : BasePlayableAsset<PlayResourcePlayableBehaviour, PlayResourceEventArgs>
    {
#if UNITY_EDITOR
        [OnValueChanged("OnCurrResourceChanged")]
        public GameObject CurrResource;
#endif

#if UNITY_EDITOR
        private void OnCurrResourceChanged()
        {
            string path = UnityEditor.AssetDatabase.GetAssetPath(CurrResource);
            PrefabPath = path;
        }
#endif

        [Header("目标点")]
        public DynamicTarget Target;

        [Header("预设路径")]
        public string PrefabPath;

        [Header("旋转")]
        public Vector3 Rotation;

        [Header("缩放")]
        public Vector3 Scale = Vector3.one;

        [Header("偏移")]
        public Vector3 Offset;

        protected override void OnYouYouCreatePlayable(ScriptPlayable<PlayResourcePlayableBehaviour> playable)
        {
            base.OnYouYouCreatePlayable(playable);

            CurrPlayableBehavior.CurrArgs.Target = Target;
            CurrPlayableBehavior.CurrArgs.PrefabPath = PrefabPath;
            CurrPlayableBehavior.CurrArgs.Rotation = Rotation;
            CurrPlayableBehavior.CurrArgs.Scale = Scale;
            CurrPlayableBehavior.CurrArgs.Offset = Offset;
        }
    }
}