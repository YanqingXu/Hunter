using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace YouYou
{
    /// <summary>
    /// 技能控制器
    /// </summary>
    public class TimelineCtrl : MonoBehaviour
    {
        private PlayableDirector m_CurrPlayableDirector;

        /// <summary>
        /// 当前放技能的角色
        /// </summary>
        public RoleCtrl CurrRole;

        /// <summary>
        /// 停止播放委托
        /// </summary>
        public BaseAction OnStopped;

        private LinkedList<Transform> m_GameObjectList;

        /// <summary>
        /// 攻击结束时间
        /// </summary>
        public float AttackEndTime { get; private set; }

        private void Awake()
        {
            m_GameObjectList = new LinkedList<Transform>();
            m_CurrPlayableDirector = GetComponent<PlayableDirector>();
            m_CurrPlayableDirector.stopped += OnPlayableDirectorStopped;
        }

        private void OnPlayableDirectorStopped(PlayableDirector playableDirector)
        {
            ClearGameObject();
            OnStopped?.Invoke();
        }

        private void OnDestroy()
        {
            m_CurrPlayableDirector.stopped -= OnPlayableDirectorStopped;
            m_CurrPlayableDirector = null;
        }



        private void OnEnable()
        {
            var lst = m_CurrPlayableDirector.playableAsset.outputs.GetEnumerator();
            while (lst.MoveNext())
            {
                PlayableBinding data = lst.Current;
                if (data.sourceObject != null)
                {
                    TrackAsset trackAsset = data.sourceObject as TrackAsset;
                    var trackList = trackAsset.GetClips().GetEnumerator();

                    if (trackAsset is PlaySoundTrack)
                    {
                        while (trackList.MoveNext())
                        {
                            TimelineClip clip = trackList.Current;
                            PlaySoundPlayable playableAsset = clip.asset as PlaySoundPlayable;

                            if (playableAsset != null && playableAsset.CurrPlayableBehavior != null)
                            {
                                playableAsset.CurrPlayableBehavior.CurrTimelineCtrl = this;
                                playableAsset.CurrPlayableBehavior.Start = clip.start;
                                playableAsset.CurrPlayableBehavior.End = clip.end;
                                playableAsset.CurrPlayableBehavior.Reset();
                            }
                        }
                    }
                    else if (trackAsset is PlayAnimTrack)
                    {
                        while (trackList.MoveNext())
                        {
                            TimelineClip clip = trackList.Current;
                            PlayAnimPlayable playableAsset = clip.asset as PlayAnimPlayable;

                            if (playableAsset != null && playableAsset.CurrPlayableBehavior != null)
                            {
                                playableAsset.CurrPlayableBehavior.CurrTimelineCtrl = this;
                                playableAsset.CurrPlayableBehavior.Start = clip.start;
                                playableAsset.CurrPlayableBehavior.End = clip.end;
                                playableAsset.CurrPlayableBehavior.Reset();

                                AttackEndTime = (float)clip.end;
                            }
                        }
                    }
                    else if (trackAsset is PlayResourceTrack)
                    {
                        while (trackList.MoveNext())
                        {
                            TimelineClip clip = trackList.Current;
                            PlayResourcePlayable playableAsset = clip.asset as PlayResourcePlayable;

                            if (playableAsset != null && playableAsset.CurrPlayableBehavior != null)
                            {
                                playableAsset.CurrPlayableBehavior.CurrTimelineCtrl = this;
                                playableAsset.CurrPlayableBehavior.Start = clip.start;
                                playableAsset.CurrPlayableBehavior.End = clip.end;
                                playableAsset.CurrPlayableBehavior.Reset();
                            }
                        }
                    }
                    else if (trackAsset is HurtPointTrack)
                    {
                        while (trackList.MoveNext())
                        {
                            TimelineClip clip = trackList.Current;
                            HurtPointPlayable playableAsset = clip.asset as HurtPointPlayable;

                            if (playableAsset != null && playableAsset.CurrPlayableBehavior != null)
                            {
                                playableAsset.CurrPlayableBehavior.CurrTimelineCtrl = this;
                                playableAsset.CurrPlayableBehavior.Start = clip.start;
                                playableAsset.CurrPlayableBehavior.End = clip.end;
                                playableAsset.CurrPlayableBehavior.Reset();
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 播放声音
        /// </summary>
        public void PlaySound(PlaySoundEventArgs args)
        {
            if (args.Target == MyCommonEnum.DynamicTarget.OurOne)
            {
                GameEntry.Audio.PlayAudio(args.SoundName, volume: 1, parameterName: null, is3D: true, pos3D: CurrRole.transform.position);
            }
        }

        /// <summary>
        /// 播放动画
        /// </summary>
        /// <param name="args"></param>
        public void PlayAnim(PlayAnimEventArgs args)
        {
            if (args.Target == MyCommonEnum.DynamicTarget.OurOne)
            {
                CurrRole.PlayAnimByAnimCategory(args.Category, args.Param);
            }
        }

        /// <summary>
        /// 播放伤害点
        /// </summary>
        public void PlayHurtPoint()
        {
            GameEntry.Data.RoleDataManager.CheckHurt(CurrRole.ServerRoleId);
        }

        public void PlayResource(PlayResourceEventArgs args)
        {
            if (args.Target == MyCommonEnum.DynamicTarget.OurOne)
            {
                GameEntry.Pool.GameObjectSpawn(args.PrefabPath, onComplete: (Transform trans, bool isNewInstance) =>
                {
                    //设置到角色身下
                    trans.SetParent(CurrRole.transform);
                    trans.localPosition = args.Offset;
                    trans.localEulerAngles = args.Rotation;
                    trans.localScale = args.Scale;

                    m_GameObjectList.AddLast(trans);
                });
            }
        }

        private void ClearGameObject()
        {
            LinkedListNode<Transform> node = m_GameObjectList.First;
            while (node != null)
            {
                LinkedListNode<Transform> next = node.Next;

                GameEntry.Pool.GameObjectDespawn(node.Value);
                m_GameObjectList.Remove(node);

                node = next;
            }
        }
    }
}