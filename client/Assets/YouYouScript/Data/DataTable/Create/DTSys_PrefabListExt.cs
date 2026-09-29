using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

/// <summary>
/// Create By 悠游课堂 http://www.u3dol.com 
/// </summary>
public static partial class DTSys_PrefabListExt
{
    private static Dictionary<int, DTSys_Prefab?> m_Dic = new Dictionary<int, DTSys_Prefab?>();
    private static List<DTSys_Prefab> m_List = new List<DTSys_Prefab>();

    #region LoadData 加载数据表数据
    /// <summary>
    /// 加载数据表数据
    /// </summary>
    public static void LoadData(this DTSys_PrefabList dtsys_prefabList)
    {
        GameEntry.DataTable.RegisterTableLoad(DataTableDefine.DTSys_PrefabName);

        //1.拿到这个表格的buffer
        GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTSys_PrefabName, (byte[] buffer) =>
        {
            //2.加载数据 并 把数据初始化到字典
            Init(DTSys_PrefabList.GetRootAsDTSys_PrefabList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    /// <summary>
    /// 初始化到字典
    /// </summary>
    public static void Init(DTSys_PrefabList dtsys_prefabList)
    {
        GameEntry.DataTable.RunTableLoad(DataTableDefine.DTSys_PrefabName, DataTableDefine.DTSys_PrefabVersion, () =>
        {
            var nextDic = new Dictionary<int, DTSys_Prefab?>();
            var nextList = new List<DTSys_Prefab>();
            int len = dtsys_prefabList.DTSysPrefabsLength;
            for (int j = 0; j < len; j++)
            {
                DTSys_Prefab ? dtsys_prefab = dtsys_prefabList.DTSysPrefabs(j);
                if (dtsys_prefab != null)
                {
                    nextList.Add(dtsys_prefab.Value);
                    nextDic[dtsys_prefab.Value.Id] = dtsys_prefab;
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
    public static DTSys_Prefab? GetEntity(this DTSys_PrefabList dtsys_prefabList, int id)
    {
        DTSys_Prefab ? dtsys_prefab;
        m_Dic.TryGetValue(id, out dtsys_prefab);
        return dtsys_prefab;
    }

    /// <summary>
    /// 获取数据实体值
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTSys_Prefab GetEntityValue(this DTSys_PrefabList dtsys_prefabList, int id)
    {
        DTSys_Prefab ? dtsys_prefab = dtsys_prefabList.GetEntity(id);
        if (dtsys_prefab != null)
        {
            return dtsys_prefab.Value;
        }
        return default;
    }

    /// <summary>
    /// 获取列表
    /// </summary>
    /// <returns></returns>
    public static List<DTSys_Prefab> GetList(this DTSys_PrefabList dtsys_prefabList)
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
