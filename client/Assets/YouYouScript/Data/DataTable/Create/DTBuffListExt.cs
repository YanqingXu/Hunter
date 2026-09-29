using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

/// <summary>
/// Create By 悠游课堂 http://www.u3dol.com 
/// </summary>
public static partial class DTBuffListExt
{
    private static Dictionary<int, DTBuff?> m_Dic = new Dictionary<int, DTBuff?>();
    private static List<DTBuff> m_List = new List<DTBuff>();

    #region LoadData 加载数据表数据
    /// <summary>
    /// 加载数据表数据
    /// </summary>
    public static void LoadData(this DTBuffList dtbuffList)
    {
        GameEntry.DataTable.RegisterTableLoad(DataTableDefine.DTBuffName);

        //1.拿到这个表格的buffer
        GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTBuffName, (byte[] buffer) =>
        {
            //2.加载数据 并 把数据初始化到字典
            Init(DTBuffList.GetRootAsDTBuffList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    /// <summary>
    /// 初始化到字典
    /// </summary>
    public static void Init(DTBuffList dtbuffList)
    {
        GameEntry.DataTable.RunTableLoad(DataTableDefine.DTBuffName, DataTableDefine.DTBuffVersion, () =>
        {
            var nextDic = new Dictionary<int, DTBuff?>();
            var nextList = new List<DTBuff>();
            int len = dtbuffList.DTBuffsLength;
            for (int j = 0; j < len; j++)
            {
                DTBuff ? dtbuff = dtbuffList.DTBuffs(j);
                if (dtbuff != null)
                {
                    nextList.Add(dtbuff.Value);
                    nextDic[dtbuff.Value.Id] = dtbuff;
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
    public static DTBuff? GetEntity(this DTBuffList dtbuffList, int id)
    {
        DTBuff ? dtbuff;
        m_Dic.TryGetValue(id, out dtbuff);
        return dtbuff;
    }

    /// <summary>
    /// 获取数据实体值
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTBuff GetEntityValue(this DTBuffList dtbuffList, int id)
    {
        DTBuff ? dtbuff = dtbuffList.GetEntity(id);
        if (dtbuff != null)
        {
            return dtbuff.Value;
        }
        return default;
    }

    /// <summary>
    /// 获取列表
    /// </summary>
    /// <returns></returns>
    public static List<DTBuff> GetList(this DTBuffList dtbuffList)
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
