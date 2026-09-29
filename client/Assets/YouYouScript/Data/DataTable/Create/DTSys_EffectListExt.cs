using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

/// <summary>
/// Create By 悠游课堂 http://www.u3dol.com 
/// </summary>
public static partial class DTSys_EffectListExt
{
    private static Dictionary<int, DTSys_Effect?> m_Dic = new Dictionary<int, DTSys_Effect?>();
    private static List<DTSys_Effect> m_List = new List<DTSys_Effect>();

    #region LoadData 加载数据表数据
    /// <summary>
    /// 加载数据表数据
    /// </summary>
    public static void LoadData(this DTSys_EffectList dtsys_effectList)
    {
        GameEntry.DataTable.RegisterTableLoad(DataTableDefine.DTSys_EffectName);

        //1.拿到这个表格的buffer
        GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTSys_EffectName, (byte[] buffer) =>
        {
            //2.加载数据 并 把数据初始化到字典
            Init(DTSys_EffectList.GetRootAsDTSys_EffectList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    /// <summary>
    /// 初始化到字典
    /// </summary>
    public static void Init(DTSys_EffectList dtsys_effectList)
    {
        GameEntry.DataTable.RunTableLoad(DataTableDefine.DTSys_EffectName, DataTableDefine.DTSys_EffectVersion, () =>
        {
            var nextDic = new Dictionary<int, DTSys_Effect?>();
            var nextList = new List<DTSys_Effect>();
            int len = dtsys_effectList.DTSysEffectsLength;
            for (int j = 0; j < len; j++)
            {
                DTSys_Effect ? dtsys_effect = dtsys_effectList.DTSysEffects(j);
                if (dtsys_effect != null)
                {
                    nextList.Add(dtsys_effect.Value);
                    nextDic[dtsys_effect.Value.Id] = dtsys_effect;
                }
            }

            //3.派发单个表加载完毕事件
            return () =>
            {
                ResetData();
                m_List.AddRange(nextList);
                foreach (var entry in nextDic) m_Dic.Add(entry.Key, entry.Value);
            };
        });
    }

    /// <summary>
    /// 获取数据实体
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTSys_Effect? GetEntity(this DTSys_EffectList dtsys_effectList, int id)
    {
        DTSys_Effect ? dtsys_effect;
        m_Dic.TryGetValue(id, out dtsys_effect);
        return dtsys_effect;
    }

    /// <summary>
    /// 获取数据实体值
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTSys_Effect GetEntityValue(this DTSys_EffectList dtsys_effectList, int id)
    {
        DTSys_Effect ? dtsys_effect = dtsys_effectList.GetEntity(id);
        if (dtsys_effect != null)
        {
            return dtsys_effect.Value;
        }
        return default;
    }

    /// <summary>
    /// 获取列表
    /// </summary>
    /// <returns></returns>
    public static List<DTSys_Effect> GetList(this DTSys_EffectList dtsys_effectList)
    {
        return m_List;
    }

    /// <summary>释放旧表的缓存与字节引用；后续 Init 使用同一原 FlatBuffers 格式重新加载。</summary>
    public static void ResetData()
    {
        m_Dic.Clear();
        m_List.Clear();
    }
}
