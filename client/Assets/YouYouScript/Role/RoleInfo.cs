using System;
using System.Collections.Generic;
using YouYou;
using YouYou.Proto;

/// <summary>
/// 角色信息
/// </summary>
public class RoleInfo
{
    public int Sex;
    public string NickName;
    public int Level;
    public int CurrHP;
    public int MaxHP;
    public int CurrMP;
    public int MaxMP;
    public int CurrFury;

    public RoleCtrl CurrRole { get; private set; }

    public Dictionary<long, int> HurtValueDic = new Dictionary<long, int>();

    public void Reset()
    {
        HurtValueDic.Clear();
    }

    /// <summary>
    /// 初始化角色信息
    /// </summary>
    /// <param name="data"></param>
    public void InitRoleInfo(WS2C_SceneLineRole_DATA data, RoleCtrl roleCtrl)
    {
        CurrRole = roleCtrl;

        Sex = data.Sex;
        NickName = data.NickName;
        Level = data.Level;
        CurrHP = data.CurrHP;
        MaxHP = data.MaxHP;
        CurrMP = data.CurrMP;
        MaxMP = data.MaxMP;
        CurrFury = data.CurrFury;
    }

    /// <summary>
    /// 初始化当前角色信息
    /// </summary>
    public void InitCurrPlayerInfo(RoleCtrl roleCtrl)
    {
        CurrRole = roleCtrl;

        Sex = (int)GameEntry.Data.UserDataManager.Sex;
        NickName = GameEntry.Data.UserDataManager.NickName;
        Level = GameEntry.Data.UserDataManager.Level;

        CurrHP = GameEntry.Data.UserDataManager.CurrHP;
        MaxHP = GameEntry.Data.UserDataManager.MaxHP;
        CurrMP = GameEntry.Data.UserDataManager.CurrMP;
        MaxMP = GameEntry.Data.UserDataManager.MaxMP;
        CurrFury = GameEntry.Data.UserDataManager.CurrFury;
    }

    /// <summary>
    /// 修改角色战斗数据
    /// </summary>
    /// <param name="data"></param>
    public void ChangeRoleBattleData(GS2C_ReturnRoleBattleDataChange data)
    {
        CurrHP = data.CurrHp;
        MaxHP = data.MaxHp;
        CurrMP = data.CurrMp;
        MaxMP = data.MaxMp;
        CurrFury = data.CurrFury;

        if (data.AttackRoleId > 0 && data.AttackRoleId != data.RoleId)
        {
            HurtValueDic[data.AttackRoleId] = data.HurtValue;
        }

        //处理添加的buff和移除的buff
        if (data.AddBuffs != null)
        {
            foreach (var item in data.AddBuffs)
            {
                CurrRole.AddBuff(item.BuffId);
            }
        }

        if (data.RemoveBuffs != null)
        {
            foreach (var item in data.RemoveBuffs)
            {
                CurrRole.RemoveBuff(item);
            }
        }
    }

    public void BuffContinueHurt(int hurtValue)
    {
        CurrHP -= hurtValue;
    }
}