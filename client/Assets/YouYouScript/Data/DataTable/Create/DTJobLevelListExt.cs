using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

/// <summary>
/// Create By 悠游课堂 http://www.u3dol.com 
/// </summary>
public static partial class DTJobLevelListExt
{
    private static Dictionary<int, DTJobLevel?> m_Dic = new Dictionary<int, DTJobLevel?>();
    private static List<DTJobLevel> m_List = new List<DTJobLevel>();

    #region LoadData 加载数据表数据
    /// <summary>
    /// 加载数据表数据
    /// </summary>
    public static void LoadData(this DTJobLevelList dtjoblevelList)
    {
        GameEntry.DataTable.RegisterTableLoad(DataTableDefine.DTJobLevelName);

        //1.拿到这个表格的buffer
        GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTJobLevelName, (byte[] buffer) =>
        {
            //2.加载数据 并 把数据初始化到字典
            Init(DTJobLevelList.GetRootAsDTJobLevelList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    /// <summary>
    /// 初始化到字典
    /// </summary>
    public static void Init(DTJobLevelList dtjoblevelList)
    {
        GameEntry.DataTable.RunTableLoad(DataTableDefine.DTJobLevelName, DataTableDefine.DTJobLevelVersion, () =>
        {
            var nextDic = new Dictionary<int, DTJobLevel?>();
            var nextList = new List<DTJobLevel>();
            int len = dtjoblevelList.DTJobLevelsLength;
            for (int j = 0; j < len; j++)
            {
                DTJobLevel ? dtjoblevel = dtjoblevelList.DTJobLevels(j);
                if (dtjoblevel != null)
                {
                    nextList.Add(dtjoblevel.Value);
                    nextDic[dtjoblevel.Value.Id] = dtjoblevel;
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
    public static DTJobLevel? GetEntity(this DTJobLevelList dtjoblevelList, int id)
    {
        DTJobLevel ? dtjoblevel;
        m_Dic.TryGetValue(id, out dtjoblevel);
        return dtjoblevel;
    }

    /// <summary>
    /// 获取数据实体值
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTJobLevel GetEntityValue(this DTJobLevelList dtjoblevelList, int id)
    {
        DTJobLevel ? dtjoblevel = dtjoblevelList.GetEntity(id);
        if (dtjoblevel != null)
        {
            return dtjoblevel.Value;
        }
        return default;
    }

    /// <summary>
    /// 获取列表
    /// </summary>
    /// <returns></returns>
    public static List<DTJobLevel> GetList(this DTJobLevelList dtjoblevelList)
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
