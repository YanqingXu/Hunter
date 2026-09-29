//===================================================
//作    者：边涯  http://www.u3dol.com
//创建时间：
//备    注：
//===================================================
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 缓存数据
/// </summary>
public class CacheDataManager : IDisposable
{
    /// <summary>
    /// 当前锁定的角色
    /// </summary>
    public RoleCtrl CurrLockRole;

    public CacheDataManager()
    {

    }

    /// <summary>
    /// 清空数据
    /// </summary>
    public void Clear()
    {
        CurrLockRole = null;
    }

    public void Dispose()
    {

    }
}