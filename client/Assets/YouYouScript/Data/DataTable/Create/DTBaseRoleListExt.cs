using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

/// <summary>
/// Create By 悠游课堂 http://www.u3dol.com 
/// </summary>
public static partial class DTBaseRoleListExt
{
    private static Dictionary<int, DTBaseRole?> m_Dic = new Dictionary<int, DTBaseRole?>();
    private static List<DTBaseRole> m_List = new List<DTBaseRole>();

    #region LoadData 加载数据表数据
    /// <summary>
    /// 加载数据表数据
    /// </summary>
    public static void LoadData(this DTBaseRoleList dtbaseroleList)
    {
        GameEntry.DataTable.RegisterTableLoad(DataTableDefine.DTBaseRoleName);

        //1.拿到这个表格的buffer
        GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTBaseRoleName, (byte[] buffer) =>
        {
            //2.加载数据 并 把数据初始化到字典
            Init(DTBaseRoleList.GetRootAsDTBaseRoleList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    /// <summary>
    /// 初始化到字典
    /// </summary>
    public static void Init(DTBaseRoleList dtbaseroleList)
    {
        GameEntry.DataTable.RunTableLoad(DataTableDefine.DTBaseRoleName, DataTableDefine.DTBaseRoleVersion, () =>
        {
            var nextDic = new Dictionary<int, DTBaseRole?>();
            var nextList = new List<DTBaseRole>();
            int len = dtbaseroleList.DTBaseRolesLength;
            for (int j = 0; j < len; j++)
            {
                DTBaseRole ? dtbaserole = dtbaseroleList.DTBaseRoles(j);
                if (dtbaserole != null)
                {
                    nextList.Add(dtbaserole.Value);
                    nextDic[dtbaserole.Value.Id] = dtbaserole;
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
    public static DTBaseRole? GetEntity(this DTBaseRoleList dtbaseroleList, int id)
    {
        DTBaseRole ? dtbaserole;
        m_Dic.TryGetValue(id, out dtbaserole);
        return dtbaserole;
    }

    /// <summary>
    /// 获取数据实体值
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTBaseRole GetEntityValue(this DTBaseRoleList dtbaseroleList, int id)
    {
        DTBaseRole ? dtbaserole = dtbaseroleList.GetEntity(id);
        if (dtbaserole != null)
        {
            return dtbaserole.Value;
        }
        return default;
    }

    /// <summary>
    /// 获取列表
    /// </summary>
    /// <returns></returns>
    public static List<DTBaseRole> GetList(this DTBaseRoleList dtbaseroleList)
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
