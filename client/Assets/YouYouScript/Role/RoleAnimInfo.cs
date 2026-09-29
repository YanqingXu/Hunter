using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using YouYou.DataTable;

/// <summary>
/// 角色动画信息
/// </summary>
public class RoleAnimInfo
{
    /// <summary>
    /// 索引号
    /// </summary>
    public int Index = 0;

    /// <summary>
    /// 当前动画剪辑
    /// </summary>
    public AnimationClipPlayable CurrPlayable;

    /// <summary>
    /// 当前的动画数据
    /// </summary>
    public DTRoleAnimation CurrRoleAnimationData;

    /// <summary>
    /// 最后使用时间
    /// </summary>
    public float LastUseTime;

    /// <summary>
    /// 是否已经加载
    /// </summary>
    public bool IsLoad;

    /// <summary>
    /// 是否正在播放
    /// </summary>
    public bool IsPlaying;

    /// <summary>
    /// 动画是否过期
    /// </summary>
    public bool IsExpire
    {
        get
        {
            if (!IsPlaying && //没有正在播放
                IsLoad && //已经加载
                CurrRoleAnimationData.InitLoad == 0 && //不是初始加载
                Time.time > LastUseTime + CurrRoleAnimationData.Expire //超期
                )
            {
                return true;
            }
            return false;
        }
    }
}