//===================================================
//作    者：边涯  http://www.u3dol.com
//创建时间：
//备    注：
//===================================================
using FMOD.Studio;
using FMODUnity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using YouYou.DataTable;

namespace YouYou
{
    /// <summary>
    /// 声音管理器
    /// </summary>
    public class AudioManager : ManagerBase, IDisposable
    {
        public bool BanksLoaded { get; private set; }
        private bool m_Disposed;
        private readonly HashSet<string> m_LoadedBankNames = new HashSet<string>();
        public AudioManager()
        {
            m_NextReleaseTime = Time.time;
        }

        /// <summary>
        /// 初始化
        /// </summary>
        public override void Init()
        {
            m_ReleaseInterval = GameEntry.ParamsSettings.GetGradeParamData(ConstDefine.Audio_ReleaseInterval, GameEntry.CurrDeviceGrade);
        }

        #region LoadBanks 加载LoadBanks
        /// <summary>
        /// 加载Banks
        /// </summary>
        /// <param name="name"></param>
        public void LoadBanks(BaseAction onComplete)
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(AudioManager));
            if (BanksLoaded) { onComplete?.Invoke(); return; }
#if DISABLE_ASSETBUNDLE && UNITY_EDITOR

            string[] arr = Directory.GetFiles(Application.dataPath + "/Download/Audio/", "*.bytes");
            int len = arr.Length;
            for (int i = 0; i < len; i++)
            {
                FileInfo file = new FileInfo(arr[i]);
                TextAsset asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Download/Audio/" + file.Name);
                LoadOwnedBank(asset);
            }

            BanksLoaded = true;

            if (onComplete != null)
            {
                onComplete();
            }
#else
            GameEntry.Resource.ResourceLoaderManager.LoadAssetBundle(ConstDefine.AudioAssetBundlePath, onComplete: (AssetBundle bundle) =>
            {
                if (m_Disposed) return;
                TextAsset[] arr = bundle.LoadAllAssets<TextAsset>();
                int len = arr.Length;
                for (int i = 0; i < len; i++)
                {
                    LoadOwnedBank(arr[i]);
                }
                BanksLoaded = true;
                if (onComplete != null)
                {
                    onComplete();
                }
            });
#endif

        }

        private void LoadOwnedBank(TextAsset asset)
        {
            if (!asset) throw new InvalidOperationException("FMOD bank asset is missing.");
            if (m_LoadedBankNames.Contains(asset.name)) return;
            RuntimeManager.LoadBank(asset);
            m_LoadedBankNames.Add(asset.name);
        }
        #endregion

        #region BGM
        /// <summary>
        /// 当前的BGM名字
        /// </summary>
        private string m_CurrBGMAudio;

        /// <summary>
        /// 当前BGM音量
        /// </summary>
        private float m_CurrBGMVolume;

        /// <summary>
        /// 当前BGM最大音量
        /// </summary>
        private float m_CurrBGMMaxVolume;

        /// <summary>
        /// 当前的BGM的Instance
        /// </summary>
        private EventInstance BGMEvent;

        /// <summary>
        /// 当前BGM的定时器 用来控制音量
        /// </summary>
        private TimeAction m_CurrBGMTimeAction;

        /// <summary>
        /// 播放BGM
        /// </summary>
        /// <param name="bgmPath"></param>
        public void PlayBGM(string bgmPath, float volume = 1)
        {
            m_CurrBGMAudio = bgmPath;
            m_CurrBGMMaxVolume = volume;
            CheckBGMEventInstance();
        }

        /// <summary>
        /// BGM切换参数
        /// </summary>
        /// <param name="switchName"></param>
        /// <param name="value"></param>
        public void BGMSwitch(string switchName, float value)
        {
            BGMEvent.setParameterByName(switchName, value);
        }

        /// <summary>
        /// 设置BGM音量
        /// </summary>
        /// <param name="value"></param>
        public void SetBGMVolume(float value)
        {
            BGMEvent.setVolume(value);
        }

        /// <summary>
        /// 暂停BGM
        /// </summary>
        /// <param name="pause"></param>
        public void PauseBGM(bool pause)
        {
            if (!BGMEvent.isValid())
                CheckBGMEventInstance();
            if (BGMEvent.isValid())
                BGMEvent.setPaused(pause);
        }

        /// <summary>
        /// 检查BGM实例把之前的释放
        /// </summary>
        private void CheckBGMEventInstance()
        {
            if (!string.IsNullOrEmpty(m_CurrBGMAudio))
            {
                if (BGMEvent.isValid())
                {
                    BGMEvent.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                    BGMEvent.release();
                }

                BGMEvent = RuntimeManager.CreateInstance(m_CurrBGMAudio);

                m_CurrBGMVolume = 0;
                SetBGMVolume(m_CurrBGMVolume);

                BGMEvent.start();

                //把音量逐渐变成Max
                m_CurrBGMTimeAction = GameEntry.Time.CreateTimeAction();
                m_CurrBGMTimeAction.Init(null, 0, 0.05f, 100, null, (int loop) =>
                 {
                     m_CurrBGMVolume += 0.1f;
                     m_CurrBGMVolume = Mathf.Min(m_CurrBGMVolume, m_CurrBGMMaxVolume);
                     SetBGMVolume(m_CurrBGMVolume);
                     if (m_CurrBGMVolume == m_CurrBGMMaxVolume)
                     {
                         m_CurrBGMTimeAction.Stop();
                     }
                 }, null).Run();

            }
        }

        /// <summary>
        /// 停止播放BGM
        /// </summary>
        public void StopBGM()
        {
            if (BGMEvent.isValid())
            {
                //把音量逐渐变成0 再停止
                m_CurrBGMTimeAction = GameEntry.Time.CreateTimeAction();
                m_CurrBGMTimeAction.Init(null, 0, 0.05f, 100, null, (int loop) =>
                 {
                     m_CurrBGMVolume -= 0.1f;
                     m_CurrBGMVolume = Mathf.Max(m_CurrBGMVolume, 0);
                     SetBGMVolume(m_CurrBGMVolume);

                     if (m_CurrBGMVolume == 0)
                     {
                         m_CurrBGMTimeAction.Stop();
                         BGMEvent.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                     }
                 }, () =>
                 {
                     BGMEvent.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                 }).Run();
            }
        }

        /// <summary>
        /// 开始播放BGM
        /// </summary>
        public void StartBGM()
        {
            BGMEvent.start();
        }
        #endregion


        /// <summary>
        /// OnUpdate
        /// </summary>
        public void OnUpdate()
        {
            if (Time.time > m_NextReleaseTime + m_ReleaseInterval)
            {
                m_NextReleaseTime = Time.time;
                Release();
            }
        }

        #region 音效

        /// <summary>
        /// 释放间隔
        /// </summary>
        private int m_ReleaseInterval = 120;

        /// <summary>
        /// 下次释放时间
        /// </summary>
        private float m_NextReleaseTime = 0f;

        /// <summary>
        /// 序号
        /// </summary>
        private int m_Serial = 0;

        /// <summary>
        /// 音效字典
        /// </summary>
        private Dictionary<int, EventInstance> m_CurrAudioEventsDic = new Dictionary<int, EventInstance>();

        /// <summary>
        /// 需要释放的音效编号
        /// </summary>
        private LinkedList<int> m_NeedRemoveList = new LinkedList<int>();

        /// <summary>
        /// 播放音效
        /// </summary>
        /// <param name="eventPath">路径</param>
        /// <param name="volume">音量</param>
        /// <param name="parameterName">参数名称</param>
        /// <param name="value">参数值</param>
        /// <param name="is3D">是否3D</param>
        /// <param name="pos3D">3D位置</param>
        /// <returns>音效实例编号</returns>
        public int PlayAudio(string eventPath, float volume = 1, string parameterName = null, float value = 0, bool is3D = false, Vector3 pos3D = default(Vector3))
        {
            if (string.IsNullOrEmpty(eventPath))
            {
                return -1;
            }

            EventInstance eventInstance = RuntimeManager.CreateInstance(eventPath);
            eventInstance.setVolume(volume);
            if (!string.IsNullOrEmpty(parameterName))
            {
                eventInstance.setParameterByName(parameterName, value);
            }

            if (is3D)
            {
                eventInstance.set3DAttributes(pos3D.To3DAttributes());
            }

            eventInstance.start();
            int serialId = m_Serial++;
            m_CurrAudioEventsDic[serialId] = eventInstance;
            return serialId;
        }

        /// <summary>
        /// 设置音效参数
        /// </summary>
        /// <param name="serialId">音效编号</param>
        /// <param name="parameterName">参数名称</param>
        /// <param name="value">参数值</param>
        public void SetParameterForAudio(int serialId, string parameterName, float value)
        {
            EventInstance eventInstance;
            if (m_CurrAudioEventsDic.TryGetValue(serialId, out eventInstance))
            {
                if (eventInstance.isValid())
                {
                    eventInstance.setParameterByName(parameterName, value);
                }
            }
        }

        /// <summary>
        /// 暂停某个音效
        /// </summary>
        /// <param name="serialId">音效实例编号</param>
        /// <param name="paused">是否暂停</param>
        /// <returns></returns>
        public bool PausedAudio(int serialId, bool paused = true)
        {
            EventInstance eventInstance;
            if (m_CurrAudioEventsDic.TryGetValue(serialId, out eventInstance))
            {
                if (eventInstance.isValid())
                {
                    return eventInstance.setPaused(paused) == FMOD.RESULT.OK;
                }
            }
            return false;
        }

        /// <summary>
        /// 停止某个音效
        /// </summary>
        /// <param name="serialId">音效实例编号</param>
        /// <param name="mode"></param>
        /// <returns></returns>
        public bool StopAudio(int serialId, FMOD.Studio.STOP_MODE mode = FMOD.Studio.STOP_MODE.IMMEDIATE)
        {
            EventInstance eventInstance;
            if (m_CurrAudioEventsDic.TryGetValue(serialId, out eventInstance))
            {
                if (eventInstance.isValid())
                {
                    var result = eventInstance.stop(mode);
                    eventInstance.release();
                    m_CurrAudioEventsDic.Remove(serialId);
                    return result == FMOD.RESULT.OK;
                }
            }
            return false;
        }

        /// <summary>
        /// 停止所有音效
        /// </summary>
        public void StopAllAudio()
        {
            var enumerator = m_CurrAudioEventsDic.GetEnumerator();
            while (enumerator.MoveNext())
            {
                EventInstance eventInstance = enumerator.Current.Value;
                if (eventInstance.isValid())
                {
                    var result = eventInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                    eventInstance.release();
                }
            }
            m_CurrAudioEventsDic.Clear();
        }

        /// <summary>
        /// 释放可释放的音效
        /// </summary>
        private void Release()
        {
            var lst = m_CurrAudioEventsDic.GetEnumerator();
            while (lst.MoveNext())
            {
                EventInstance eventInstance = lst.Current.Value;
                if (!eventInstance.isValid())
                {
                    continue;
                }

                PLAYBACK_STATE state;
                eventInstance.getPlaybackState(out state);
                if (state == PLAYBACK_STATE.STOPPED)
                {
                    m_NeedRemoveList.AddLast(lst.Current.Key);
                    eventInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                    eventInstance.release();
                }
            }

            //
            LinkedListNode<int> currNode = m_NeedRemoveList.First;
            while (currNode != null)
            {
                LinkedListNode<int> next = currNode.Next;
                int serialId = currNode.Value;
                m_CurrAudioEventsDic.Remove(serialId);
                m_NeedRemoveList.Remove(currNode);
                currNode = next;
            }
        }
        #endregion

        /// <summary>
        /// 播放BGM
        /// </summary>
        /// <param name="audioId">声音编号</param>
        public void PlayBGM(int audioId)
        {
            if (audioId == -1) return;

            DTSys_Audio? entity = GameEntry.DataTable.Sys_AudioList.GetEntity(audioId);
            if (entity != null)
            {
                DTSys_Audio sys_Audio = entity.Value;
                PlayBGM(sys_Audio.AssetPath, sys_Audio.Volume);
            }
            else
            {
                GameEntry.LogError("BGM不存在Id={0}", audioId);
            }
        }

        /// <summary>
        /// 播放音效
        /// </summary>
        /// <param name="audioId">声音Id</param>
        /// <param name="parameterName">参数名称</param>
        /// <param name="value">参数值</param>
        /// <param name="pos3D">3d音效位置</param>
        /// <returns>音效实例编号</returns>
        public int PlayAudio(int audioId, string parameterName = null, float value = 0, Vector3 pos3D = default(Vector3))
        {
            // Original launch/preload behavior is unchanged; an idle offline FSM may play after real banks are ready.
            if (m_Disposed || (GameEntry.Procedure != null && (int)GameEntry.Procedure.CurrProcedureState <= 2 &&
                ((int)GameEntry.Procedure.CurrProcedureState >= 0 || !BanksLoaded)))
            {
                return -1;
            }
            DTSys_Audio? entity = GameEntry.DataTable.Sys_AudioList.GetEntity(audioId);
            if (entity != null)
            {
                DTSys_Audio sys_Audio = entity.Value;
                return PlayAudio(sys_Audio.AssetPath, sys_Audio.Volume, parameterName, value, sys_Audio.Is3D == 1, pos3D);
            }
            else
            {
                GameEntry.LogError("Audio不存在Id={0}", audioId);
                return -1;
            }
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            m_CurrBGMTimeAction?.Stop();
            m_CurrBGMTimeAction = null;
            if (BGMEvent.isValid())
            {
                BGMEvent.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                BGMEvent.release();
                BGMEvent.clearHandle();
            }
            StopAllAudio();
            foreach (string bank in m_LoadedBankNames) RuntimeManager.UnloadBank(bank);
            m_LoadedBankNames.Clear();
            m_NeedRemoveList.Clear();
            BanksLoaded = false;
            m_CurrBGMAudio = null;
        }
    }
}
