using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

/// <summary>
/// Create By 悠游课堂 http://www.u3dol.com 
/// </summary>
public static partial class DTJobListExt
{
    private static Dictionary<int, DTJob?> m_Dic = new Dictionary<int, DTJob?>();
    private static List<DTJob> m_List = new List<DTJob>();

    #region LoadData 加载数据表数据
    /// <summary>
    /// 加载数据表数据
    /// </summary>
    public static void LoadData(this DTJobList dtjobList)
    {
        GameEntry.DataTable.RegisterTableLoad(DataTableDefine.DTJobName);

        //1.拿到这个表格的buffer
        GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTJobName, (byte[] buffer) =>
        {
            //2.加载数据 并 把数据初始化到字典
            Init(DTJobList.GetRootAsDTJobList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    /// <summary>
    /// 初始化到字典
    /// </summary>
    public static void Init(DTJobList dtjobList)
    {
        GameEntry.DataTable.RunTableLoad(DataTableDefine.DTJobName, DataTableDefine.DTJobVersion, () =>
        {
            var nextDic = new Dictionary<int, DTJob?>();
            var nextList = new List<DTJob>();
            int len = dtjobList.DTJobsLength;
            for (int j = 0; j < len; j++)
            {
                DTJob ? dtjob = dtjobList.DTJobs(j);
                if (dtjob != null)
                {
                    nextList.Add(dtjob.Value);
                    nextDic[dtjob.Value.Id] = dtjob;
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
    public static DTJob? GetEntity(this DTJobList dtjobList, int id)
    {
        DTJob ? dtjob;
        m_Dic.TryGetValue(id, out dtjob);
        return dtjob;
    }

    /// <summary>
    /// 获取数据实体值
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTJob GetEntityValue(this DTJobList dtjobList, int id)
    {
        DTJob ? dtjob = dtjobList.GetEntity(id);
        if (dtjob != null)
        {
            return dtjob.Value;
        }
        return default;
    }

    /// <summary>
    /// 获取列表
    /// </summary>
    /// <returns></returns>
    public static List<DTJob> GetList(this DTJobList dtjobList)
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
