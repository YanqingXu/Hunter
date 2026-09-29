using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

/// <summary>
/// Create By 悠游课堂 http://www.u3dol.com 
/// </summary>
public static partial class DTSkillLevelListExt
{
    private static Dictionary<int, DTSkillLevel?> m_Dic = new Dictionary<int, DTSkillLevel?>();
    private static List<DTSkillLevel> m_List = new List<DTSkillLevel>();

    #region LoadData 加载数据表数据
    /// <summary>
    /// 加载数据表数据
    /// </summary>
    public static void LoadData(this DTSkillLevelList dtskilllevelList)
    {
        GameEntry.DataTable.RegisterTableLoad(DataTableDefine.DTSkillLevelName);

        //1.拿到这个表格的buffer
        GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTSkillLevelName, (byte[] buffer) =>
        {
            //2.加载数据 并 把数据初始化到字典
            Init(DTSkillLevelList.GetRootAsDTSkillLevelList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    /// <summary>
    /// 初始化到字典
    /// </summary>
    public static void Init(DTSkillLevelList dtskilllevelList)
    {
        GameEntry.DataTable.RunTableLoad(DataTableDefine.DTSkillLevelName, DataTableDefine.DTSkillLevelVersion, () =>
        {
            var nextDic = new Dictionary<int, DTSkillLevel?>();
            var nextList = new List<DTSkillLevel>();
            int len = dtskilllevelList.DTSkillLevelsLength;
            for (int j = 0; j < len; j++)
            {
                DTSkillLevel ? dtskilllevel = dtskilllevelList.DTSkillLevels(j);
                if (dtskilllevel != null)
                {
                    nextList.Add(dtskilllevel.Value);
                    nextDic[dtskilllevel.Value.Id] = dtskilllevel;
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
    public static DTSkillLevel? GetEntity(this DTSkillLevelList dtskilllevelList, int id)
    {
        DTSkillLevel ? dtskilllevel;
        m_Dic.TryGetValue(id, out dtskilllevel);
        return dtskilllevel;
    }

    /// <summary>
    /// 获取数据实体值
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTSkillLevel GetEntityValue(this DTSkillLevelList dtskilllevelList, int id)
    {
        DTSkillLevel ? dtskilllevel = dtskilllevelList.GetEntity(id);
        if (dtskilllevel != null)
        {
            return dtskilllevel.Value;
        }
        return default;
    }

    /// <summary>
    /// 获取列表
    /// </summary>
    /// <returns></returns>
    public static List<DTSkillLevel> GetList(this DTSkillLevelList dtskilllevelList)
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
