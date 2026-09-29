using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace YouYou
{
    public class GameEntry : MonoBehaviour
    {
        [FoldoutGroup("ParamsSettings")]
        [SerializeField]
        private ParamsSettings.DeviceGrade m_CurrDeviceGrade;

        [FoldoutGroup("ParamsSettings")]
        [SerializeField]
        private ParamsSettings m_ParamsSettings;

        [FoldoutGroup("ParamsSettings")]
        [SerializeField]
        private YouYouLanguage m_CurrLanguage;

        [FoldoutGroup("ParamsSettings")]
        [Tooltip("保持课程原启动流程。独立宿主可关闭后自行调用原管理器。")]
        public bool AutoLaunchProcedure = true;

        public bool IsInitialized { get; private set; }
        private bool m_IsInitializing;
        private bool m_IsShuttingDown;
        private bool m_ShutdownCallbacksReleased;
        private System.Threading.Tasks.Task m_ShutdownTask;
        private bool m_RuntimeSettingsApplied;
        private bool m_SpriteRequestSubscribed;

        [FoldoutGroup("ResourceGroup")]
        [Header("游戏物体对象池父物体")]
        public Transform PoolParent;

        [FoldoutGroup("ResourceGroup")]
        /// <summary>
        /// 游戏物体对象池分组
        /// </summary>
        [SerializeField]
        public GameObjectPoolEntity[] GameObjectPoolGroups = Array.Empty<GameObjectPoolEntity>();

        [FoldoutGroup("ResourceGroup")]
        [Header("锁定的资源包")]
        /// <summary>
        /// 锁定的资源包（不会释放）
        /// </summary>
        public string[] LockedAssetBundle = Array.Empty<string>();

        [FoldoutGroup("UIGroup")]
        [Header("标准分辨率宽度")]
        [SerializeField]
        public int StandardWidth = 1280;

        [FoldoutGroup("UIGroup")]
        [Header("标准分辨率高度")]
        [SerializeField]
        public int StandardHeight = 720;

        [FoldoutGroup("UIGroup")]
        [Header("UI摄像机")]
        public Camera UICamera;

        [FoldoutGroup("UIGroup")]
        [Header("根画布")]
        [SerializeField]
        public Canvas UIRootCanvas;

        [FoldoutGroup("UIGroup")]
        [Header("根UI节点")]
        [SerializeField]
        public RectTransform UIRootRectTransform;

        [FoldoutGroup("UIGroup")]
        [Header("根画布的缩放")]
        [SerializeField]
        public CanvasScaler UIRootCanvasScaler;

        [FoldoutGroup("UIGroup")]
        [Header("UI分组")]
        [SerializeField]
        public UIGroup[] UIGroups = Array.Empty<UIGroup>();

        #region 时间缩放
        [CustomValueDrawer("SetTimeScale")]
        public float timeScale;

#if UNITY_EDITOR
        [ButtonGroup]
        [LabelText("0")]
        private void timeScale0()
        {
            timeScale = 0;
        }

        [ButtonGroup]
        [LabelText("0.5")]
        private void timeScale05()
        {
            timeScale = 0.5f;
        }

        [ButtonGroup]
        [LabelText("1")]
        private void timeScale1()
        {
            timeScale = 1;
        }

        [ButtonGroup]
        [LabelText("2")]
        private void timeScale2()
        {
            timeScale = 2;
        }

        [ButtonGroup]
        [LabelText("3")]
        private void timeScale3()
        {
            timeScale = 3;
        }

        private float SetTimeScale(float value, GUIContent label)
        {
            float ret = UnityEditor.EditorGUILayout.Slider(label, value, 0f, 3);
            UnityEngine.Time.timeScale = ret;
            return ret;
        }
#endif
        #endregion

        #region 管理器属性
        /// <summary>
        /// 日志管理器
        /// </summary>
        public static LoggerManager Logger
        {
            get;
            private set;
        }

        /// <summary>
        /// 事件管理器
        /// </summary>
        public static EventManager Event
        {
            get;
            private set;
        }

        /// <summary>
        /// 时间管理器
        /// </summary>
        public static TimeManager Time
        {
            get;
            private set;
        }

        /// <summary>
        /// 状态机管理器
        /// </summary>
        public static FsmManager Fsm
        {
            get;
            private set;
        }

        /// <summary>
        /// 流程管理器
        /// </summary>
        public static ProcedureManager Procedure
        {
            get;
            private set;
        }

        /// <summary>
        /// 数据表管理器
        /// </summary>
        public static DataTableManager DataTable
        {
            get;
            private set;
        }

        /// <summary>
        /// Socket管理器
        /// </summary>
        public static SocketManager Socket
        {
            get;
            private set;
        }

        /// <summary>
        /// Http管理器
        /// </summary>
        public static HttpManager Http
        {
            get;
            private set;
        }

        /// <summary>
        /// 数据管理器
        /// </summary>
        public static DataManager Data
        {
            get;
            private set;
        }

        /// <summary>
        /// 本地化管理器
        /// </summary>
        public static LocalizationManager Localization
        {
            get;
            private set;
        }

        /// <summary>
        /// 对象池管理器
        /// </summary>
        public static PoolManager Pool
        {
            get;
            private set;
        }

        /// <summary>
        /// 场景管理器
        /// </summary>
        public static YouYouSceneManager Scene
        {
            get;
            private set;
        }

        /// <summary>
        /// 可寻址资源管理器
        /// </summary>
        public static AddressableManager Resource
        {
            get;
            private set;
        }

        /// <summary>
        /// 下载管理器
        /// </summary>
        public static DownloadManager Download
        {
            get;
            private set;
        }

        /// <summary>
        /// UI管理器
        /// </summary>
        public static YouYouUIManager UI
        {
            get;
            private set;
        }


        /// <summary>
        /// 声音管理器
        /// </summary>
        public static AudioManager Audio
        {
            get;
            private set;
        }

        public static InputManager Input
        {
            get;
            private set;
        }

        /// <summary>
        /// 任务管理器
        /// </summary>
        public static TaskManager Task
        {
            get;
            private set;
        }

        #endregion

        #region InitManagers 初始化管理器
        /// <summary>
        /// 初始化管理器
        /// </summary>
        private static void InitManagers()
        {
            Logger = new LoggerManager();
            Event = new EventManager();
            Time = new TimeManager();
            Fsm = new FsmManager();
            Procedure = new ProcedureManager();
            DataTable = new DataTableManager();
            Socket = new SocketManager();
            Http = new HttpManager();
            Data = new DataManager();
            Localization = new LocalizationManager();
            Pool = new PoolManager();
            Scene = new YouYouSceneManager();
            Resource = new AddressableManager();
            Download = new DownloadManager();
            UI = new YouYouUIManager();
            Audio = new AudioManager();
            Input = new InputManager();
            Task = new TaskManager();

            Logger.Init();
            Event.Init();
            Time.Init();
            Fsm.Init();
            Procedure.Init();
            DataTable.Init();
            Socket.Init();
            Http.Init();
            Data.Init();
            Localization.Init();
            Pool.Init();
            Scene.Init();
            Resource.Init();
            Download.Init();
            UI.Init();
            Audio.Init();
            Input.Init();
            Task.Init();

            // 是否进入课程 Launch 流程由入口 Initialize 决定。
        }
        #endregion

        #region 更新组件管理
        /// <summary>
        /// 更新组件列表
        /// </summary>
        private static readonly LinkedList<IUpdateComponent> m_UpdateComponentList = new LinkedList<IUpdateComponent>();

        #region RegisterUpdateComponent 注册更新组件
        /// <summary>
        /// 注册更新组件
        /// </summary>
        /// <param name="component"></param>
        public static void RegisterUpdateComponent(IUpdateComponent component)
        {
            m_UpdateComponentList.AddLast(component);
        }
        #endregion

        #region RemoveUpdateComponent 移除更新组件
        /// <summary>
        /// 移除更新组件
        /// </summary>
        /// <param name="component"></param>
        public static void RemoveUpdateComponent(IUpdateComponent component)
        {
            m_UpdateComponentList.Remove(component);
        }
        #endregion


        #endregion

        /// <summary>
        /// 单例
        /// </summary>
        public static GameEntry Instance;

        /// <summary>
        /// 全局参数设置
        /// </summary>
        public static ParamsSettings ParamsSettings
        {
            get;
            private set;
        }

        /// <summary>
        /// 当前设备等级
        /// </summary>
        public static ParamsSettings.DeviceGrade CurrDeviceGrade
        {
            get;
            private set;
        }

        /// <summary>
        /// 当前语言（要和本地化表的语言字段 一致）
        /// </summary>
        public static YouYouLanguage CurrLanguage
        {
            get;
            set;
        }

        /// <summary>
        /// 摄像机控制器
        /// </summary>
        public static CameraCtrl CameraCtrl;

        private void Awake()
        {
            Initialize();
        }

        /// <summary>在禁用的入口物体上配置；场景中的原序列化字段仍然有效。</summary>
        public void Configure(ParamsSettings settings, ParamsSettings.DeviceGrade deviceGrade,
            YouYouLanguage language, bool autoLaunchProcedure = true)
        {
            if (IsInitialized || m_IsInitializing || m_IsShuttingDown)
                throw new InvalidOperationException("GameEntry 已初始化，请先 Shutdown 再修改入口配置。");
            if (!settings) throw new ArgumentNullException(nameof(settings));
            m_ParamsSettings = settings;
            m_CurrDeviceGrade = deviceGrade;
            m_CurrLanguage = language;
            AutoLaunchProcedure = autoLaunchProcedure;
        }

        /// <summary>独立宿主的简便配置；沿用入口的设备等级与语言。</summary>
        public void Configure(ParamsSettings settings, bool startOriginalProcedure = false)
        {
            Configure(settings, m_CurrDeviceGrade, m_CurrLanguage, startOriginalProcedure);
        }

        /// <summary>初始化全部原管理器。重复调用不重复创建服务。</summary>
        public void Initialize()
        {
            if (IsInitialized || m_IsInitializing || m_IsShuttingDown) return;
            if (!ReferenceEquals(Instance, null) && !ReferenceEquals(Instance, this))
            {
                enabled = false;
                Debug.LogWarning("场景已有 GameEntry，本重复入口不会创建或释放管理器。", this);
                if (Application.isPlaying) Destroy(this);
                return;
            }
            ValidateConfiguration();
            m_ShutdownTask = null;
            Instance = this;
            m_IsInitializing = true;

            try
            {
                // 此处仍由原配置指定等级；设备自动分级可由宿主决定。
                CurrDeviceGrade = m_CurrDeviceGrade;
                ParamsSettings = m_ParamsSettings;
                CurrLanguage = m_CurrLanguage;
                InitManagers();
                IsInitialized = true;
                ApplyRuntimeSettings();
                if (AutoLaunchProcedure) Procedure.ChangeState(ProcedureState.Launch);
            }
            catch
            {
                Shutdown();
                throw;
            }
            finally { m_IsInitializing = false; }
        }

        private void ValidateConfiguration()
        {
            if (!m_ParamsSettings)
                throw new InvalidOperationException("GameEntry 需要 ParamsSettings。请绑定原配置资产或先调用 Configure。");
            if (!UIRootCanvas || !UIRootRectTransform || !UIRootCanvasScaler)
                throw new InvalidOperationException("GameEntry 需要 UIRootCanvas、UIRootRectTransform 和 UIRootCanvasScaler。");
            if (StandardWidth <= 0 || StandardHeight <= 0)
                throw new InvalidOperationException("GameEntry 标准 UI 分辨率必须大于零。");
            if (UIGroups == null) UIGroups = Array.Empty<UIGroup>();
            var groupIds = new HashSet<byte>();
            foreach (var group in UIGroups)
                if (group == null || !groupIds.Add(group.Id))
                    throw new InvalidOperationException("GameEntry 的 UI 分组不能为空或使用重复编号。");
            if (GameObjectPoolGroups == null) GameObjectPoolGroups = Array.Empty<GameObjectPoolEntity>();
            if (LockedAssetBundle == null) LockedAssetBundle = Array.Empty<string>();
        }

        private void ApplyRuntimeSettings()
        {
            if (m_RuntimeSettingsApplied || !IsInitialized || Instance != this) return;
            UnityEngine.Time.timeScale = timeScale = 1;
            Application.targetFrameRate = ParamsSettings.GetGradeParamData(ConstDefine.targetFrameRate, CurrDeviceGrade);
            TMPro.TMP_Text.OnSpriteAssetRequest += OnSpriteAssetRequest;
            m_SpriteRequestSubscribed = true;
            m_RuntimeSettingsApplied = true;
        }

        void Start()
        {
            ApplyRuntimeSettings();
        }

        private TMP_SpriteAsset OnSpriteAssetRequest(int hashCode, string path)
        {
#if DISABLE_ASSETBUNDLE && UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(string.Format("Assets/Download/UI/UIRes/Texture/Face/{0}.asset", path));
#else
            return null;
#endif
        }

        void Update()
        {
            if (!IsInitialized || Instance != this || m_IsShuttingDown) return;
            //循环更新组件
            for (LinkedListNode<IUpdateComponent> curr = m_UpdateComponentList.First; curr != null;)
            {
                var next = curr.Next;
                curr.Value.OnUpdate();
                if (!IsInitialized) return;
                curr = next;
            }

            Procedure?.OnUpdate();
            if (!IsInitialized) return;
            DataTable?.OnUpdate();
            if (!IsInitialized) return;
            Socket?.OnUpdate();
            if (!IsInitialized) return;
            Pool?.OnUpdate();
            if (!IsInitialized) return;
            Scene?.OnUpdate();
            if (!IsInitialized) return;
            Resource?.OnUpdate();
            if (!IsInitialized) return;
            Download?.OnUpdate();
            if (!IsInitialized) return;
            UI?.OnUpdate();
            if (!IsInitialized) return;
            if (!IsInitialized) return;
            Audio?.OnUpdate();
            if (!IsInitialized) return;
            Data?.OnUpdate();
            if (!IsInitialized) return;
            Input?.OnUpdate();
            if (!IsInitialized) return;
            Task?.OnUpdate();
            if (!IsInitialized) return;
        }

        private void FixedUpdate()
        {
            if (IsInitialized && Instance == this && !m_IsShuttingDown) Time?.OnUpdate();
        }

        /// <summary>
        /// 销毁
        /// </summary>
        private void OnDestroy()
        {
            Shutdown();
        }

        /// <summary>停止驱动并释放全部原管理器，之后允许同一宿主重新初始化。</summary>
        public void Shutdown()
        {
            if (!ReferenceEquals(Instance, this)) return;
            if (m_ShutdownTask != null && !m_ShutdownTask.IsCompleted) return;
            if (m_IsShuttingDown && !m_ShutdownCallbacksReleased) return;
            BeginShutdown();
            CompleteShutdown();
        }

        /// <summary>
        /// 先停止驱动并释放回调，再退出当前 Unity 调用栈后完成资源释放。
        /// 活动请求共享同一 Task；失败后保留错误信息供调用方处理。
        /// </summary>
        public System.Threading.Tasks.Task ShutdownAsync()
        {
            if (!ReferenceEquals(Instance, this)) return System.Threading.Tasks.Task.CompletedTask;
            if (m_ShutdownTask != null && !m_ShutdownTask.IsFaulted && !m_ShutdownTask.IsCanceled)
                return m_ShutdownTask;
            var completion = new System.Threading.Tasks.TaskCompletionSource<object>(
                System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            m_ShutdownTask = completion.Task;
            _ = ShutdownInPhasesAsync(completion);
            return completion.Task;
        }

        private async System.Threading.Tasks.Task ShutdownInPhasesAsync(System.Threading.Tasks.TaskCompletionSource<object> completion)
        {
            try
            {
                BeginShutdown();
                // Owner teardown may run inside nested Unity callbacks/task continuations.
                // Allow queued Unity destruction callbacks to return before releasing pools.
                int releaseFrame = UnityEngine.Time.frameCount;
                do { await System.Threading.Tasks.Task.Yield(); }
                while (Application.isPlaying && UnityEngine.Time.frameCount == releaseFrame);
                CompleteShutdown();
                completion.TrySetResult(null);
            }
            catch (Exception exception)
            {
                // Keep the
                // stopped owner and remaining services available for diagnosis/explicit retry.
                completion.TrySetException(exception);
            }
        }

        private void BeginShutdown()
        {
            if (m_IsShuttingDown || !ReferenceEquals(Instance, this)) return;
            m_IsShuttingDown = true;
            IsInitialized = false;
            if (m_SpriteRequestSubscribed)
            {
                TMPro.TMP_Text.OnSpriteAssetRequest -= OnSpriteAssetRequest;
                m_SpriteRequestSubscribed = false;
            }
            StopAllCoroutines();
            m_UpdateComponentList.Clear();

            // 回调和窗体先退出；事件、资源与对象池保留到消费者释放完毕。
            Cleanup(() => Input?.Dispose());
            Cleanup(() => Task?.Dispose());
            Cleanup(() => Procedure?.Dispose());
            Cleanup(() => Fsm?.Dispose());
            Cleanup(() => Scene?.Dispose());
            Cleanup(() => UI?.Dispose());
            Cleanup(() => Audio?.Dispose());
            Cleanup(() => Download?.Dispose());
            Cleanup(() => Socket?.Dispose());
            Cleanup(() => Http?.Dispose());
            Cleanup(() => DataTable?.Dispose());
            Cleanup(() => Data?.Dispose());
            Cleanup(() => Localization?.Dispose());
            Cleanup(() => Time?.Dispose());
            Cleanup(() => Resource?.Dispose());
            Cleanup(() => Event?.Dispose());
            m_ShutdownCallbacksReleased = true;
        }

        private void CompleteShutdown()
        {
            if (!ReferenceEquals(Instance, this)) return;
            if (!m_ShutdownCallbacksReleased)
                throw new InvalidOperationException("GameEntry must release callbacks before completing shutdown.");
            Cleanup(() => Pool?.Dispose());
            Cleanup(() => Logger?.SyncLog());
            Cleanup(() => Logger?.Dispose());
            m_UpdateComponentList.Clear();

            Logger = null; Event = null; Time = null; Fsm = null; Procedure = null;
            DataTable = null; Socket = null; Http = null; Data = null; Localization = null;
            Pool = null; Scene = null; Resource = null; Download = null; UI = null;
            Audio = null; Input = null; Task = null;
            ParamsSettings = null; CameraCtrl = null;
            CurrDeviceGrade = default(ParamsSettings.DeviceGrade);
            CurrLanguage = default(YouYouLanguage);
            Instance = null;
            m_RuntimeSettingsApplied = false;
            m_ShutdownCallbacksReleased = false;
            m_IsShuttingDown = false;
        }

        private static void Cleanup(Action release)
        {
            try { release(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        /// <summary>
        /// 打印日志
        /// </summary>
        /// <param name="message"></param>
        public static void Log(LogCategory catetory, string message, params object[] args)
        {
            switch (catetory)
            {
                default:
                case LogCategory.Normal:
#if DEBUG_LOG_NORMAL && DEBUG_MODEL
                    {
                        StringBuilder sbr = StringHelper.PoolNew();
                        Debug.Log("[youyou]" + (args.Length == 0 ? message : sbr.AppendFormatNoGC(message, args).ToString()));
                        StringHelper.PoolDel(ref sbr);
                    }

#endif
                    break;
                case LogCategory.Procedure:
#if DEBUG_LOG_PROCEDURE && DEBUG_MODEL
                    {
                        StringBuilder sbr = StringHelper.PoolNew();
                        Debug.Log("[youyou]" + string.Format("<color=#ffffff>{0}</color>", args.Length == 0 ? message : sbr.AppendFormatNoGC(message, args).ToString()));
                        StringHelper.PoolDel(ref sbr);
                    }
#endif
                    break;
                case LogCategory.Resource:
#if DEBUG_LOG_RESOURCE && DEBUG_MODEL
                    {
                        StringBuilder sbr = StringHelper.PoolNew();
                        Debug.Log("[youyou]" + string.Format("<color=#ace44a>{0}</color>", args.Length == 0 ? message : sbr.AppendFormatNoGC(message, args).ToString()));
                        StringHelper.PoolDel(ref sbr);
                    }
#endif
                    break;
                case LogCategory.Proto:
#if DEBUG_LOG_PROTO && DEBUG_MODEL
                    {
                        StringBuilder sbr = StringHelper.PoolNew();
                        Debug.Log("[youyou]" + string.Format("<color=#c5e1dc>{0}</color>", args.Length == 0 ? message : sbr.AppendFormatNoGC(message, args).ToString()));
                        StringHelper.PoolDel(ref sbr);
                    }
#endif
                    break;
            }
        }

        /// <summary>
        /// 打印错误日志
        /// </summary>
        /// <param name="message"></param>
        /// <param name="args"></param>
        public static void LogError(string message, params object[] args)
        {
#if DEBUG_LOG_ERROR && DEBUG_MODEL
            StringBuilder sbr = StringHelper.PoolNew();
            Debug.LogError("[youyou]" + (args.Length == 0 ? message : sbr.AppendFormatNoGC(message, args).ToString()));
            StringHelper.PoolDel(ref sbr);
#endif
        }
    }
}
