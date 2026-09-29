using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MyCommonEnum
{
    public enum Sex
    {
        /// <summary>
        /// 女
        /// </summary>
        Woman = 0,

        /// <summary>
        /// 男
        /// </summary>
        Male = 1
    }

    /// <summary>
    /// 角色类型
    /// </summary>
    public enum RoleType
    {
        CurrPlayer = 0,
        OtherPlayer = 1,
        Monster = 2
    }

    /// <summary>
    /// 角色状态机状态
    /// </summary>
    public enum RoleFSMState : sbyte
    {
        /// <summary>
        /// 未设置
        /// </summary>
        None = 0,

        /// <summary>
        /// 待机
        /// </summary>
        Idle = 1,

        /// <summary>
        /// 跑
        /// </summary>
        Run = 2,

        /// <summary>
        /// 攻击
        /// </summary>
        Attack = 3,

        /// <summary>
        /// 受伤
        /// </summary>
        Hurt = 4,

        /// <summary>
        /// 死亡
        /// </summary>
        Die = 5
    }

    /// <summary>
    /// 角色动画分类
    /// </summary>
    public enum RoleAnimCategory
    {
        /// <summary>
        /// 普通待机
        /// </summary>
        IdleNormal,

        /// <summary>
        /// 跑
        /// </summary>
        Run,

        /// <summary>
        /// 攻击
        /// </summary>
        Attack,

        /// <summary>
        /// 受伤
        /// </summary>
        Hurt,
    }

    /// <summary>
    /// 动态目标点
    /// </summary>
    public enum DynamicTarget
    {
        /// <summary>
        /// 我方单体
        /// </summary>
        OurOne,
        /// <summary>
        /// 我方全体
        /// </summary>
        OurAll,
        /// <summary>
        /// 我方队友
        /// </summary>
        OuTeammate,

        /// <summary>
        /// 敌人单体
        /// </summary>
        EnemyOne,
        /// <summary>
        /// 敌人全体
        /// </summary>
        EnemyAll,
        /// <summary>
        /// 敌人队友
        /// </summary>
        EnemyTeammate
    }
}