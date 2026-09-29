using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;
using YouYou;
using YouYou.DataTable;
using static MyCommonEnum;

/// <summary>
/// 角色控制器
/// </summary>
public class RoleCtrl : BaseSprite, IUpdateComponent
{
    public bool IsPlayer = false;

    /// <summary>
    /// 皮肤编号
    /// </summary>
    private int m_SkinId = 0;

    /// <summary>
    /// 当前的皮肤
    /// </summary>
    private Transform m_CurrSkinTransform;

    /// <summary>
    /// 当前皮肤的MeshRenderer
    /// </summary>
    private SkinnedMeshRenderer m_CurrSkinnedMeshRenderer;

    /// <summary>
    /// 当前的皮肤组件
    /// </summary>
    private RoleSkinComponent m_CurrRoleSkinComponent;

    /// <summary>
    /// 血条跟随点
    /// </summary>
    public Transform HeadBarPoint;

    /// <summary>
    /// 当前血条
    /// </summary>
    public UIGlobalHeadBarView CurrHeadBar { get; private set; }

    /// <summary>
    /// 当前HUD
    /// </summary>
    private HUDText m_HUDText;


    #region 动画相关
    /// <summary>
    /// 动画剪辑字典
    /// </summary>
    private Dictionary<string, AnimationClip> m_AnimationClipDic;

    /// <summary>
    /// 动画画布
    /// </summary>
    private PlayableGraph m_PlayableGraph;

    /// <summary>
    /// 动画组件
    /// </summary>
    private Animator m_Animator;

    /// <summary>
    /// 动画输出
    /// </summary>
    private AnimationPlayableOutput m_AnimationPlayableOutput;

    /// <summary>
    /// 动画混合Playable
    /// </summary>
    private AnimationMixerPlayable m_AnimationMixerPlayable;

    /// <summary>
    /// 动画剪辑Playable字典
    /// </summary>
    private Dictionary<int, RoleAnimInfo> m_RoleAnimInfoDic = new Dictionary<int, RoleAnimInfo>(100);
    #endregion

    /// <summary>
    /// 当前角色对应表格数据
    /// </summary>
    private DTBaseRole m_CurrDTBaseRole;

    private DTSprite m_CurrDTSprite;

    /// <summary>
    /// 动画组编号
    /// </summary>
    private int m_AnimGroupId;

    /// <summary>
    /// 当前角色动画分类数据
    /// </summary>
    private DTRoleAnimCategory m_CurrDTRoleAnimCategory;

    /// <summary>
    /// 当前角色状态机管理器
    /// </summary>
    private RoleFSMManager m_CurrRoleFSMManager;

    /// <summary>
    /// 当前角色状态
    /// </summary>
    public RoleFSMState CurrState
    {
        get { return m_CurrRoleFSMManager.CurrRoleFSMState; }
    }

    /// <summary>
    /// 设置攻击状态动画长度
    /// </summary>
    /// <param name="animLen"></param>
    public void SetAttackAnimLen(float animLen)
    {
        if (m_CurrRoleFSMManager == null) return;
        m_CurrRoleFSMManager.RoleFSMAttack.SetAnimLen(animLen);
    }

    /// <summary>
    /// 寻路代理
    /// </summary>
    public NavMeshAgent Agent
    {
        get;
        private set;
    }

    /// <summary>
    /// 角色移动速度 这个值读表
    /// </summary>
    public float MoveSpeed = 10;

    /// <summary>
    /// 角色信息
    /// </summary>
    public RoleInfo RoleInfo;

    public override void OnAwake()
    {
        base.OnAwake();
        m_AnimationClipDic = new Dictionary<string, AnimationClip>();
        m_CurrRoleFSMManager = new RoleFSMManager(this);
        Agent = GetComponent<NavMeshAgent>();
        Agent.enabled = false;
        RoleInfo = new RoleInfo();
    }

    /// <summary>
    /// 初始化数据
    /// </summary>
    /// <param name="baseRoleId">角色编号</param>
    public void InitPlayerData(int baseRoleId)
    {
        m_CurrDTBaseRole = GameEntry.DataTable.BaseRoleList.GetEntityValue(baseRoleId);
        m_CurrDTRoleAnimCategory = GameEntry.DataTable.RoleAnimCategoryList.GetEntityValue(baseRoleId);
        m_SkinId = m_CurrDTBaseRole.PrefabId;
        m_AnimGroupId = m_CurrDTBaseRole.AnimGroupId;

        //Debug.LogError("InitPlayerData = " + baseRoleId);
        //初始化状态机
        m_CurrRoleFSMManager.Init();
    }

    public void InitSpriteData(int spriteId)
    {
        m_CurrDTSprite = GameEntry.DataTable.SpriteList.GetEntityValue(spriteId);
        m_CurrDTRoleAnimCategory = GameEntry.DataTable.RoleAnimCategoryList.GetEntityValue(spriteId);
        m_SkinId = m_CurrDTSprite.PrefabId;
        m_AnimGroupId = m_CurrDTSprite.AnimGroupId;

        //Debug.LogError("InitSpriteData = " + spriteId);
        //初始化状态机
        m_CurrRoleFSMManager.Init();
    }

    public override void OnOpen()
    {
        base.OnOpen();

        //注册更新组件
        GameEntry.RegisterUpdateComponent(this);
        LoadSkin();
    }

    /// <summary>
    /// 打开寻路代理
    /// </summary>
    public void OpenAgent()
    {
        Agent.enabled = true;
    }

    public override void OnClose()
    {
        base.OnClose();
        IsPlayer = false;
        Agent.enabled = false;

        if (RoleInfo != null)
        {
            RoleInfo.Reset();
        }
        //移除更新组件
        GameEntry.RemoveUpdateComponent(this);
        Despawn();
    }

    protected override void OnBeforDestroy()
    {
        base.OnBeforDestroy();

        //销毁画布
        if (m_PlayableGraph.IsValid())
        {
            m_PlayableGraph.Destroy();
        }
        RoleInfo = null;
    }

    /// <summary>
    /// 切换皮肤
    /// </summary>
    /// <param name="skinId"></param>
    private void ChangeSkin(int skinId)
    {
        if (m_SkinId == skinId)
        {
            return;
        }
        m_SkinId = skinId;
        LoadSkin();
    }

    /// <summary>
    /// 加载皮肤
    /// </summary>
    private void LoadSkin()
    {
        //先把当前皮肤卸载
        UnLoadSkin();

        GameEntry.Data.GlobalManager.CreateHeadBarView((UIGlobalHeadBarView view) =>
        {
            view.Init(RoleInfo.NickName, RoleInfo.MaxHP, RoleInfo.CurrHP);
            CurrHeadBar = view;
        });

        GameEntry.Data.GlobalManager.CreateHudText((HUDText hudText) =>
        {
            m_HUDText = hudText;
        });

        //加载皮肤
        GameEntry.Pool.GameObjectSpawn(m_SkinId, (Transform trans, bool isNewInstance) =>
        {
            m_CurrSkinTransform = trans;
            m_CurrSkinTransform.SetParent(transform);
            m_CurrSkinTransform.localPosition = Vector3.zero;
            m_CurrSkinTransform.localScale = Vector3.one;
            m_CurrSkinTransform.localEulerAngles = Vector3.zero;

            m_CurrRoleSkinComponent = m_CurrSkinTransform.GetComponent<RoleSkinComponent>();

            m_Animator = m_CurrSkinTransform.GetComponent<Animator>();

            //第一步 创建画布
            if (m_PlayableGraph.IsValid())
            {
                m_PlayableGraph.Destroy();
            }
            m_PlayableGraph = PlayableGraph.Create("PlayableGraph_" + m_SkinId);

            //创建输出节点
            m_AnimationPlayableOutput = AnimationPlayableOutput.Create(m_PlayableGraph, "output", m_Animator);
            CreateMixerPlayable();
            if (m_CurrRoleSkinComponent == null)
            {
                //角色根节点上的SkinnedMeshRenderer
                m_CurrSkinnedMeshRenderer = m_CurrSkinTransform.GetComponentInChildren<SkinnedMeshRenderer>();
            }

            LoadInitRoleAnimations(m_AnimGroupId);

            ////默认进入待机
            //m_CurrRoleFSMManager.ChangeState(MyCommonEnum.RoleFSMState.Idle);
            PlayAnimAfterLoadSkin();
        });
    }

    /// <summary>
    /// 加载皮肤后 根据状态加载动画
    /// </summary>
    private void PlayAnimAfterLoadSkin()
    {
        switch (m_CurrRoleFSMManager.CurrRoleFSMState)
        {
            default:
            case RoleFSMState.Idle:
                PlayAnimByAnimCategory(RoleAnimCategory.IdleNormal);
                break;
            case RoleFSMState.Run:
                PlayAnimByAnimCategory(RoleAnimCategory.Run);
                break;
        }
    }

    /// <summary>
    /// 创建混合Playable
    /// </summary>
    private void CreateMixerPlayable()
    {
        //创建动画混合Playable
        m_AnimationMixerPlayable = AnimationMixerPlayable.Create(m_PlayableGraph, 100);

        //设置Output的源
        m_AnimationPlayableOutput.SetSourcePlayable(m_AnimationMixerPlayable, 0);

        //这个时候 是没有任何动画的
    }

    /// <summary>
    /// 加载初始角色动画
    /// </summary>
    /// <param name="animGroupId">动画分组编号</param>
    private void LoadInitRoleAnimations(int animGroupId)
    {
        m_RoleAnimInfoDic.Clear();

        //根据动画组编号，加载动画
        List<DTRoleAnimation> roleAnimations = GameEntry.DataTable.RoleAnimationList.GetListByGroupId(animGroupId);
        int lenRoleAnimations = roleAnimations.Count;
        for (int i = 0; i < lenRoleAnimations; i++)
        {
            DTRoleAnimation roleAnimation = roleAnimations[i];

            m_RoleAnimInfoDic.Add(roleAnimation.Id, new RoleAnimInfo()
            {
                CurrRoleAnimationData = roleAnimation,
                IsLoad = false,
                LastUseTime = 0,
                Index = i
            });

            if (roleAnimation.InitLoad == 1)
            {
                LoadRoleAnimation(roleAnimation);
            }
        }
    }

    /// <summary>
    /// 加载角色动画
    /// </summary>
    /// <param name="roleAnimation"></param>
    public void LoadRoleAnimation(DTRoleAnimation roleAnimation, BaseAction<RoleAnimInfo> onComplete = null)
    {
        GameEntry.Resource.ResourceLoaderManager.LoadMainAsset(AssetCategory.RoleSources, GameUtil.GetRoleAnimationPath(roleAnimation.AnimPath), (ResourceEntity entity) =>
        {
            AnimationClip animationClip = entity.Target as AnimationClip;
            m_AnimationClipDic[roleAnimation.AnimPath] = animationClip;


            //创建AnimationClipPlayable
            AnimationClipPlayable animationClipPlayable = AnimationClipPlayable.Create(m_PlayableGraph, animationClip);

            //把AnimationClipPlayable加入字典
            RoleAnimInfo roleAnimInfo = null;
            if (m_RoleAnimInfoDic.TryGetValue(roleAnimation.Id, out roleAnimInfo))
            {
                roleAnimInfo.CurrPlayable = animationClipPlayable;
                roleAnimInfo.IsLoad = true; //当前动画已经加载
                roleAnimInfo.LastUseTime = 0;

                //连接到MixerPlayable
                m_PlayableGraph.Connect(animationClipPlayable, 0, m_AnimationMixerPlayable, roleAnimInfo.Index);

                //一定要把权重设置为0
                m_AnimationMixerPlayable.SetInputWeight(roleAnimInfo.Index, 0);

                onComplete?.Invoke(roleAnimInfo);
            }
        });
    }

    /// <summary>
    /// 根据动画编号播放动画
    /// </summary>
    /// <param name="animId"></param>
    public void PlayAnimByAnimId(int animId, ref RoleAnimInfo roleAnimInfo)
    {
        //将动画正在播放中 属性 设置为false
        var enumerator = m_RoleAnimInfoDic.GetEnumerator();
        while (enumerator.MoveNext())
        {
            enumerator.Current.Value.IsPlaying = false;
        }

        if (m_RoleAnimInfoDic.TryGetValue(animId, out roleAnimInfo))
        {
            roleAnimInfo.LastUseTime = Time.time; //设置最后使用时间
            roleAnimInfo.IsPlaying = true; //正在播放中

            if (roleAnimInfo.IsLoad)
            {
                PlayAnim(roleAnimInfo);
            }
            else
            {
                //动画不存在 先加载动画
                LoadRoleAnimation(roleAnimInfo.CurrRoleAnimationData, (RoleAnimInfo retRoleAnimInfo) =>
                {
                    PlayAnim(retRoleAnimInfo);
                });
            }
        }
    }

    /// <summary>
    /// 根据动画分类播放动画
    /// </summary>
    public RoleAnimInfo PlayAnimByAnimCategory(RoleAnimCategory roleAnimCategory, int param = 0)
    {
        int animId = -1;
        switch (roleAnimCategory)
        {
            default:
            case RoleAnimCategory.IdleNormal:
                animId = m_CurrDTRoleAnimCategory.IdleNormalAnimId;
                break;
            case RoleAnimCategory.Run:
                animId = m_CurrDTRoleAnimCategory.RunAnimId;
                break;
            case RoleAnimCategory.Attack:
                animId = m_CurrDTRoleAnimCategory.Attack(param);
                break;
            case RoleAnimCategory.Hurt:
                animId = m_CurrDTRoleAnimCategory.HurtAnimId;
                break;
        }

        RoleAnimInfo roleAnimInfo = null;
        PlayAnimByAnimId(animId, ref roleAnimInfo);
        return roleAnimInfo;
    }

    /// <summary>
    /// 播放动画
    /// </summary>
    /// <param name="roleAnimInfo"></param>
    private void PlayAnim(RoleAnimInfo roleAnimInfo)
    {
        m_PlayableGraph.Play();

        //根据索引拿到Playable
        Playable playable = m_AnimationMixerPlayable.GetInput(roleAnimInfo.Index);
        playable.SetTime(0);
        playable.Play();

        int len = m_RoleAnimInfoDic.Count;
        for (int i = 0; i < len; i++)
        {
            if (i == roleAnimInfo.Index)
            {
                //动画的长度
                //AnimationClipPlayable animationClipPlayable = lst[i];
                //float animLen = animationClipPlayable.GetAnimationClip().length;
                //需要播放的权重设置为1
                m_AnimationMixerPlayable.SetInputWeight(i, 1);
            }
            else
            {
                //不播放的 设置权重为0
                m_AnimationMixerPlayable.SetInputWeight(i, 0);
            }
        }
    }

    /// <summary>
    /// 检查卸载动画
    /// </summary>
    public void CheckUnloadRoleAnimation()
    {
        var enumerator = m_RoleAnimInfoDic.GetEnumerator();
        while (enumerator.MoveNext())
        {
            RoleAnimInfo roleAnimInfo = enumerator.Current.Value;
            if (roleAnimInfo.IsExpire)
            {
                roleAnimInfo.IsLoad = false;
                roleAnimInfo.CurrPlayable.Destroy();
            }
        }
    }

    /// <summary>
    /// 加载部件
    /// </summary>
    /// <param name="parts"></param>
    public void LoadPart(List<int> parts)
    {
        if (m_CurrRoleSkinComponent == null) return;
        m_CurrRoleSkinComponent.LoadPart(parts);
    }

    /// <summary>
    /// 加载皮肤材质
    /// </summary>
    /// <param name="materialName">皮肤材质名称</param>
    public void LoadSkinMaterial(string materialName)
    {
        if (m_CurrSkinnedMeshRenderer == null) return;

        GameEntry.Resource.ResourceLoaderManager.LoadMainAsset(AssetCategory.RoleSources, materialName, (ResourceEntity entity) =>
        {
            UnityEngine.Material material = entity.Target as UnityEngine.Material;

#if UNITY_EDITOR
            m_CurrSkinnedMeshRenderer.material = material;
#else
            m_CurrSkinnedMeshRenderer.sharedMaterial = material;
#endif
        });
    }

    /// <summary>
    /// 卸载皮肤
    /// </summary>
    private void UnLoadSkin()
    {
        if (m_CurrSkinTransform != null)
        {
            GameEntry.Pool.GameObjectDespawn(m_CurrSkinTransform);
            m_CurrSkinTransform = null;
        }

        m_CurrSkinnedMeshRenderer = null;

        if (CurrHeadBar != null)
        {
            GameEntry.Data.GlobalManager.ReleaseHeadBarView(CurrHeadBar);
            CurrHeadBar = null;
        }

        if (m_HUDText != null)
        {
            GameEntry.Data.GlobalManager.ReleaseHudText(m_HUDText);
            m_HUDText = null;
        }
    }

    /// <summary>
    /// 角色回池
    /// </summary>
    private void Despawn()
    {
        UnLoadSkin();
        ClearAllBuff();
        //角色控制器回池
        GameEntry.Pool.GameObjectDespawn(transform);
    }

    public void OnUpdate()
    {
        if (m_CurrRoleFSMManager == null)
        {
            return;
        }

        m_CurrRoleFSMManager.OnUpdate();

        if (IsPlayer)
        {
            //摄像机跟随
            GameEntry.CameraCtrl.transform.position = transform.position;
        }

        if (CurrHeadBar != null)
        {

            //得到屏幕坐标
            Vector2 screenPos = GameEntry.CameraCtrl.MainCamera.WorldToScreenPoint(HeadBarPoint.position);

            //接收的UI世界坐标
            Vector3 pos;

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(GameEntry.Instance.UIRootRectTransform, screenPos, GameEntry.Instance.UICamera, out pos))
            {
                CurrHeadBar.transform.position = pos;
            }
        }

        if (m_HUDText != null)
        {

            //得到屏幕坐标
            Vector2 screenPos = GameEntry.CameraCtrl.MainCamera.WorldToScreenPoint(HeadBarPoint.position);

            //接收的UI世界坐标
            Vector3 pos;

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(GameEntry.Instance.UIRootRectTransform, screenPos, GameEntry.Instance.UICamera, out pos))
            {
                m_HUDText.transform.position = pos;
            }
        }
    }

    public void RefreshHeadBar()
    {
        if (CurrHeadBar != null && RoleInfo != null)
        {
            CurrHeadBar.ChangeHp(RoleInfo.MaxHP, RoleInfo.CurrHP);
        }
    }

    /// <summary>
    /// 角色点击移动
    /// </summary>
    /// <param name="targetPos"></param>
    public void ClickMove(Vector3 targetPos)
    {
        m_CurrRoleFSMManager.ClickMove(targetPos);
    }

    public void ServerRun(float runSpeed, Vector3 targetPos)
    {
        m_CurrRoleFSMManager.ServerRun(runSpeed, targetPos);
    }

    public void ServerJoystickMove(float runSpeed, Vector3 dir)
    {
        m_CurrRoleFSMManager.JoystickMove(runSpeed, dir, false);
    }

    /// <summary>
    /// 服务器摇杆抬起
    /// </summary>
    /// <param name="currPos"></param>
    /// <param name="rotationY"></param>
    public void ServerJoystickStop(Vector3 currPos, float rotationY)
    {
        m_CurrRoleFSMManager.JoystickStop(false, currPos, rotationY);
    }

    /// <summary>
    /// 切换状态
    /// </summary>
    /// <param name="roleFSMState"></param>
    public void ChangeState(RoleFSMState roleFSMState )
    {
        if (roleFSMState == RoleFSMState.None)
        {
            return;
        }
        m_CurrRoleFSMManager.ChangeState(roleFSMState);
    }

    /// <summary>
    /// 角色摇杆移动
    /// </summary>
    /// <param name="dir"></param>
    public void JoystickMove(Vector2 dir)
    {
        m_CurrRoleFSMManager.JoystickMove(0, dir, true);
    }

    public void JoystickStop(Vector2 dir)
    {
        m_CurrRoleFSMManager.JoystickStop(true, Vector3.zero, 0);
    }

    public void CheckHurt(long attackRoleId)
    {
        if (RoleInfo.HurtValueDic.TryGetValue(attackRoleId, out int hurtValue))
        {
            if (m_HUDText != null)
            {
                m_HUDText.ShowHUDTip(hurtValue.ToString(), new Vector2(0, 0), Color.red);
            }
            ChangeState(RoleFSMState.Hurt);
            RefreshHeadBar();

            RoleInfo.HurtValueDic.Remove(attackRoleId);
        }
    }

    private Dictionary<int, Transform> m_BuffEffectDic = new Dictionary<int, Transform>();

    public void AddBuff(int buffId)
    {
        DTBuff buffConfig = GameEntry.DataTable.BuffList.GetEntityValue(buffId);
        if (buffConfig.PrefabId > 0)
        {
            if (!m_BuffEffectDic.TryGetValue(buffId, out Transform trans))
            {
                GameEntry.Pool.GameObjectSpawn(buffConfig.PrefabId, (Transform newtrans, bool isNewInstance) =>
                {
                    newtrans.SetParent(transform);
                    newtrans.localPosition = Vector3.zero;
                    m_BuffEffectDic.Add(buffId, newtrans);
                });
            }
        }

        CurrHeadBar.AddBuff(buffId);
    }

    public void RemoveBuff(int buffId)
    {
        DTBuff buffConfig = GameEntry.DataTable.BuffList.GetEntityValue(buffId);
        if (buffConfig.PrefabId > 0)
        {
            if (m_BuffEffectDic.TryGetValue(buffId, out Transform trans))
            {
                GameEntry.Pool.GameObjectDespawn(trans);
                m_BuffEffectDic.Remove(buffId);
            }
        }
        CurrHeadBar.RemoveBuff(buffId);
    }

    public void ClearAllBuff()
    {
        foreach (var item in m_BuffEffectDic)
        {
            GameEntry.Pool.GameObjectDespawn(item.Value);
        }
        m_BuffEffectDic.Clear();

    }

    public void BuffContinueHurt(int hurtValue)
    {
        if (hurtValue > 0)
        {
            RoleInfo.BuffContinueHurt(hurtValue);
            if (m_HUDText != null)
            {
                m_HUDText.ShowHUDTip(hurtValue.ToString(), new Vector2(0, 0), Color.red);
            }
            ChangeState(RoleFSMState.Hurt);
            RefreshHeadBar();
        }
    }
}