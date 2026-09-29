using UnityEngine;
using UnityEngine.AI;
using YouYou;
/// <summary>
/// 角色状态 跑
/// </summary>
public class RoleFSMRun : RoleFSMBase
{
    /// <summary>
    /// 路经点
    /// </summary>
    private Vector3[] m_VectorPath;

    /// <summary>
    /// 当前路经点索引
    /// </summary>
    private int m_CurrPointIndex;

    /// <summary>
    /// 是否通过摇杆播放跑动画
    /// </summary>
    private bool m_IsPlayRunWithJoystick;

    /// <summary>
    /// 是否通过点击地面播放跑动画
    /// </summary>
    private bool m_IsPlayRunWithClick;

    /// <summary>
    /// 转身完毕
    /// </summary>
    private bool m_TurnComplete = false;

    private Vector3 endPos;
    private Vector3 beginPos;
    private Vector3 dir;
    private Vector3 rotation;
    private float dis;
    private float runSpeed = 10; //速度
    private float modifyRunSpeed = 10;//修正速度
    private float runNeedTime = 0; //跑需要的时间

    /// <summary>
    /// 服务器摇杆移动中
    /// </summary>
    private bool m_ServerJoystickMoveing = false;

    /// <summary>
    /// 服务器摇杆移动目标点
    /// </summary>
    private Vector3 m_ServerJoystickMoveTargetPos;

    /// <summary>
    /// 收到服务器待机后 自动移动
    /// </summary>
    private bool m_AutoRunAfterServerIdle;

    /// <summary>
    /// 收到服务器待机后 自动移动到的目标位置
    /// </summary>
    private Vector3 m_AutoRunTargerPosAfterServerIdle;

    /// <summary>
    /// 收到服务器待机后 自动移动到目标位置后自身的旋转
    /// </summary>
    private float m_AutoRunTargerRotationYAfterServerIdle;

    public override void OnEnter()
    {
        base.OnEnter();
        m_TurnComplete = false;
        m_IsPlayRunWithClick = false;
        GameEntry.Log(LogCategory.Normal, "RoleFSMRun OnEnter");
    }

    public override void OnUpdate()
    {
        base.OnUpdate();

        if (GameEntry.Data.RoleDataManager.CurrPlayer.ServerRoleId == CurrFsm.Owner.CurrRoleCtrl.ServerRoleId)
        {
            //摇杆拖拽中 禁止滑动摄像机 只针对当前玩家有效
            if (GameEntry.Input.Joystick != null && GameEntry.Input.Joystick.IsDraging)
            {
                return;
            }
        }

        if (m_ServerJoystickMoveing)
        {
            Vector3 direction = m_ServerJoystickMoveTargetPos - CurrFsm.Owner.CurrRoleCtrl.transform.position;
            //如果摇杆移动中
            direction.Normalize();//归一化，确保力度一致

            direction = direction * Time.deltaTime * modifyRunSpeed;
            CurrFsm.Owner.CurrRoleCtrl.transform.rotation = Quaternion.LookRotation(direction);
            CurrFsm.Owner.CurrRoleCtrl.Agent.Move(direction);
            return;
        }

        #region 编辑器模式 绘路线图
#if UNITY_EDITOR
        Color c = Color.white;
        switch (path.status)
        {
            case NavMeshPathStatus.PathComplete:
                c = Color.blue;
                break;
            case NavMeshPathStatus.PathInvalid:
                c = Color.red;
                break;
            case NavMeshPathStatus.PathPartial:
                c = Color.yellow;
                break;
        }

        for (int i = 1; i < path.corners.Length; ++i)
        {
            Debug.DrawLine(path.corners[i - 1], path.corners[i], c);
        }
#endif
        #endregion

        if (m_VectorPath == null)
        {
            return;
        }

        //如果整个路径走完了 切换待机
        if (m_CurrPointIndex >= m_VectorPath.Length)
        {
            m_VectorPath = null;
            m_IsPlayRunWithClick = false;
            //Debug.LogError("整个路径走完了 切换待机");

            if (m_AutoRunAfterServerIdle)
            {
                m_AutoRunAfterServerIdle = false;
                CurrFsm.Owner.CurrRoleCtrl.transform.localEulerAngles = new Vector3(0, m_AutoRunTargerRotationYAfterServerIdle, 0);
            }
            CurrFsm.Owner.ChangeState(MyCommonEnum.RoleFSMState.Idle);

            return;
        }

        if (!m_TurnComplete)
        {
            endPos = m_VectorPath[m_CurrPointIndex];
            beginPos = m_VectorPath[m_CurrPointIndex - 1];

            dir = (endPos - beginPos).normalized;

            rotation = dir;
            //立刻转身
            rotation.y = 0;
            CurrFsm.Owner.CurrRoleCtrl.transform.rotation = Quaternion.LookRotation(rotation);

            m_TurnComplete = true;
        }
        
        CurrFsm.Owner.CurrRoleCtrl.Agent.Move(dir * Time.deltaTime * modifyRunSpeed);

        //判断是否应该向下一个点移动
        float dis = Vector3.Distance(CurrFsm.Owner.CurrRoleCtrl.transform.position, beginPos);

        //当到达临时目标点了
        if (dis >= Vector3.Distance(endPos, beginPos))
        {
            CurrFsm.Owner.CurrRoleCtrl.Agent.enabled = false;
            CurrFsm.Owner.CurrRoleCtrl.transform.position = endPos; //位置修正
            CurrFsm.Owner.CurrRoleCtrl.Agent.enabled = true;

            m_TurnComplete = false;
            m_CurrPointIndex++;
        }
    }

    public override void OnLeave()
    {
        base.OnLeave();
        GameEntry.Log(LogCategory.Normal, "RoleFSMRun OnLeave");
    }

    private NavMeshPath path = new NavMeshPath();

    public void ClickMove(Vector3 targetPos)
    {
        m_CurrPointIndex = 1;
        m_TurnComplete = false;
        runSpeed = 10;
        modifyRunSpeed = runSpeed;

        //计算路径
        CurrFsm.Owner.CurrRoleCtrl.Agent.CalculatePath(targetPos, path);

        if (path.status == NavMeshPathStatus.PathComplete)
        {
            m_VectorPath = path.corners;

            if (!m_IsPlayRunWithClick)
            {
                m_IsPlayRunWithClick = true;
                CurrFsm.Owner.CurrRoleCtrl.PlayAnimByAnimCategory(MyCommonEnum.RoleAnimCategory.Run);
            }
        }
    }

    /// <summary>
    /// 服务器角色移动
    /// </summary>
    /// <param name="runSpeed"></param>
    /// <param name="targetPos"></param>
    public void ServerRun(float runSpeed, Vector3 targetPos)
    {
        m_CurrPointIndex = 1;
        m_TurnComplete = false;
        //计算路径
        CurrFsm.Owner.CurrRoleCtrl.Agent.CalculatePath(targetPos, path);

        if (path.status == NavMeshPathStatus.PathComplete)
        {
            m_VectorPath = path.corners;

            //算出距离
            dis = GameUtil.GetPathLen(m_VectorPath);

            //距离/速度=到达所需时间（秒）
            runNeedTime = dis / runSpeed;
            runNeedTime -= GameEntry.Socket.PingValue * 0.001f;

            //修正速度
            modifyRunSpeed = dis / runNeedTime;

            if (!m_IsPlayRunWithClick)
            {
                m_IsPlayRunWithClick = true;
                CurrFsm.Owner.CurrRoleCtrl.PlayAnimByAnimCategory(MyCommonEnum.RoleAnimCategory.Run);
            }
        }
    }

    /// <summary>
    /// 摇杆移动
    /// </summary>
    /// <param name="dir"></param>
    /// <param name="clientAction">客户端行为</param>
    public void JoystickMove(float runSpeed, Vector3 dir, bool clientAction)
    {
        if (!m_IsPlayRunWithJoystick)
        {
            //确保动画只播放一次
            CurrFsm.Owner.CurrRoleCtrl.PlayAnimByAnimCategory(MyCommonEnum.RoleAnimCategory.Run);
            m_IsPlayRunWithJoystick = true;
        }

        Vector3 direction;
        if (clientAction)
        {
            //1.设置辅助器位置
            GameEntry.Data.RoleDataManager.CurrPlayerMoveHelper.transform.position =
                new Vector3(
        CurrFsm.Owner.CurrRoleCtrl.transform.position.x + dir.x,
        CurrFsm.Owner.CurrRoleCtrl.transform.position.y,
        CurrFsm.Owner.CurrRoleCtrl.transform.position.z + dir.y);

            //2.让辅助器进行旋转
            GameEntry.Data.RoleDataManager.CurrPlayerMoveHelper.transform.RotateAround(CurrFsm.Owner.CurrRoleCtrl.transform.position, Vector3.up, GameEntry.CameraCtrl.transform.localEulerAngles.y - 90);

            //3.得到真正要移动的方向
            direction = GameEntry.Data.RoleDataManager.CurrPlayerMoveHelper.transform.position - CurrFsm.Owner.CurrRoleCtrl.transform.position;
            GameEntry.Data.RoleDataManager.JoystickMove(CurrFsm.Owner.CurrRoleCtrl.transform.position, direction);

            modifyRunSpeed = CurrFsm.Owner.CurrRoleCtrl.MoveSpeed;
        }
        else
        {
            //如果是服务器摇杆移动，这里的dir是移动的目标点
            SetServerJoystickMoveTarget(dir);
            return;
        }

        direction.Normalize();//归一化，确保力度一致
       
        direction = direction * Time.deltaTime * modifyRunSpeed;
        CurrFsm.Owner.CurrRoleCtrl.transform.rotation = Quaternion.LookRotation(direction);
        CurrFsm.Owner.CurrRoleCtrl.Agent.Move(direction);

    }

    /// <summary>
    /// 设置服务器摇杆移动的目标点
    /// </summary>
    /// <param name="targetPos"></param>
    private void SetServerJoystickMoveTarget(Vector3 targetPos)
    {
        m_ServerJoystickMoveing = true;
        m_ServerJoystickMoveTargetPos = targetPos;

        //算出距离
        dis = (m_ServerJoystickMoveTargetPos - CurrFsm.Owner.CurrRoleCtrl.transform.position).magnitude;

        //距离/速度=到达所需时间（秒）
        runNeedTime = dis / runSpeed;
        runNeedTime -= GameEntry.Socket.PingValue * 0.001f;

        //修正速度
        modifyRunSpeed = dis / runNeedTime;
        modifyRunSpeed = Mathf.Clamp(modifyRunSpeed, CurrFsm.Owner.CurrRoleCtrl.MoveSpeed, 15);

        //Debug.LogError("modifyRunSpeed=" + modifyRunSpeed);

        m_AutoRunAfterServerIdle = false;
        m_VectorPath = null;

    }

    /// <summary>
    /// 摇杆抬起
    /// </summary>
    public void JoystickStop(bool clientAction, Vector3 currPos, float rotationY)
    {
        if (clientAction)
        {
            GameEntry.Data.RoleDataManager.JoystickStop(CurrFsm.Owner.CurrRoleCtrl.transform.position, CurrFsm.Owner.CurrRoleCtrl.transform.rotation.eulerAngles.y);
            CurrFsm.Owner.ChangeState(MyCommonEnum.RoleFSMState.Idle);
        }
        else
        {
            m_ServerJoystickMoveing = false;
            m_AutoRunAfterServerIdle = true;
            m_AutoRunTargerPosAfterServerIdle = currPos;
            m_AutoRunTargerRotationYAfterServerIdle = rotationY;

            m_CurrPointIndex = 1;
            m_VectorPath = new Vector3[2];
            m_VectorPath[0] = CurrFsm.Owner.CurrRoleCtrl.transform.position;
            m_VectorPath[1] = m_AutoRunTargerPosAfterServerIdle;
            m_TurnComplete = false;

            //算出距离
            dis = GameUtil.GetPathLen(m_VectorPath);

            //距离/速度=到达所需时间（秒）
            runNeedTime = dis / runSpeed;
            runNeedTime -= GameEntry.Socket.PingValue * 0.001f;

            //修正速度
            modifyRunSpeed = dis / runNeedTime;
        }
        m_IsPlayRunWithJoystick = false;
    }
}