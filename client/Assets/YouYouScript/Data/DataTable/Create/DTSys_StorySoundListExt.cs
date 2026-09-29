using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

/// <summary>
/// Create By 悠游课堂 http://www.u3dol.com 
/// </summary>
public static partial class DTSys_StorySoundListExt
{
    private static Dictionary<int, DTSys_StorySound?> m_Dic = new Dictionary<int, DTSys_StorySound?>();
    private static List<DTSys_StorySound> m_List = new List<DTSys_StorySound>();

    #region LoadData 加载数据表数据
    /// <summary>
    /// 加载数据表数据
    /// </summary>
    public static void LoadData(this DTSys_StorySoundList dtsys_storysoundList)
    {
        GameEntry.DataTable.RegisterTableLoad(DataTableDefine.DTSys_StorySoundName);

        //1.拿到这个表格的buffer
        GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTSys_StorySoundName, (byte[] buffer) =>
        {
            //2.加载数据 并 把数据初始化到字典
            Init(DTSys_StorySoundList.GetRootAsDTSys_StorySoundList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    /// <summary>
    /// 初始化到字典
    /// </summary>
    public static void Init(DTSys_StorySoundList dtsys_storysoundList)
    {
        GameEntry.DataTable.RunTableLoad(DataTableDefine.DTSys_StorySoundName, DataTableDefine.DTSys_StorySoundVersion, () =>
        {
            var nextDic = new Dictionary<int, DTSys_StorySound?>();
            var nextList = new List<DTSys_StorySound>();
            int len = dtsys_storysoundList.DTSysStorySoundsLength;
            for (int j = 0; j < len; j++)
            {
                DTSys_StorySound ? dtsys_storysound = dtsys_storysoundList.DTSysStorySounds(j);
                if (dtsys_storysound != null)
                {
                    nextList.Add(dtsys_storysound.Value);
                    nextDic[dtsys_storysound.Value.Id] = dtsys_storysound;
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
    public static DTSys_StorySound? GetEntity(this DTSys_StorySoundList dtsys_storysoundList, int id)
    {
        DTSys_StorySound ? dtsys_storysound;
        m_Dic.TryGetValue(id, out dtsys_storysound);
        return dtsys_storysound;
    }

    /// <summary>
    /// 获取数据实体值
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTSys_StorySound GetEntityValue(this DTSys_StorySoundList dtsys_storysoundList, int id)
    {
        DTSys_StorySound ? dtsys_storysound = dtsys_storysoundList.GetEntity(id);
        if (dtsys_storysound != null)
        {
            return dtsys_storysound.Value;
        }
        return default;
    }

    /// <summary>
    /// 获取列表
    /// </summary>
    /// <returns></returns>
    public static List<DTSys_StorySound> GetList(this DTSys_StorySoundList dtsys_storysoundList)
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
