using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

/// <summary>
/// Create By 悠游课堂 http://www.u3dol.com 
/// </summary>
public static partial class DTSys_SceneDetailListExt
{
    private static Dictionary<int, DTSys_SceneDetail?> m_Dic = new Dictionary<int, DTSys_SceneDetail?>();
    private static List<DTSys_SceneDetail> m_List = new List<DTSys_SceneDetail>();

    #region LoadData 加载数据表数据
    /// <summary>
    /// 加载数据表数据
    /// </summary>
    public static void LoadData(this DTSys_SceneDetailList dtsys_scenedetailList)
    {
        GameEntry.DataTable.RegisterTableLoad(DataTableDefine.DTSys_SceneDetailName);

        //1.拿到这个表格的buffer
        GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTSys_SceneDetailName, (byte[] buffer) =>
        {
            //2.加载数据 并 把数据初始化到字典
            Init(DTSys_SceneDetailList.GetRootAsDTSys_SceneDetailList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    /// <summary>
    /// 初始化到字典
    /// </summary>
    public static void Init(DTSys_SceneDetailList dtsys_scenedetailList)
    {
        GameEntry.DataTable.RunTableLoad(DataTableDefine.DTSys_SceneDetailName, DataTableDefine.DTSys_SceneDetailVersion, () =>
        {
            var nextDic = new Dictionary<int, DTSys_SceneDetail?>();
            var nextList = new List<DTSys_SceneDetail>();
            int len = dtsys_scenedetailList.DTSysSceneDetailsLength;
            for (int j = 0; j < len; j++)
            {
                DTSys_SceneDetail ? dtsys_scenedetail = dtsys_scenedetailList.DTSysSceneDetails(j);
                if (dtsys_scenedetail != null)
                {
                    nextList.Add(dtsys_scenedetail.Value);
                    nextDic[dtsys_scenedetail.Value.Id] = dtsys_scenedetail;
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
    public static DTSys_SceneDetail? GetEntity(this DTSys_SceneDetailList dtsys_scenedetailList, int id)
    {
        DTSys_SceneDetail ? dtsys_scenedetail;
        m_Dic.TryGetValue(id, out dtsys_scenedetail);
        return dtsys_scenedetail;
    }

    /// <summary>
    /// 获取数据实体值
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTSys_SceneDetail GetEntityValue(this DTSys_SceneDetailList dtsys_scenedetailList, int id)
    {
        DTSys_SceneDetail ? dtsys_scenedetail = dtsys_scenedetailList.GetEntity(id);
        if (dtsys_scenedetail != null)
        {
            return dtsys_scenedetail.Value;
        }
        return default;
    }

    /// <summary>
    /// 获取列表
    /// </summary>
    /// <returns></returns>
    public static List<DTSys_SceneDetail> GetList(this DTSys_SceneDetailList dtsys_scenedetailList)
    {
        return m_List;
    }

    /// <summary>释放旧表的缓存与字节引用；后续 Init 使用同一原 FlatBuffers 格式重新加载。</summary>
    public static void ResetData()
    {
        m_Dic.Clear();
        m_List.Clear();
        ret.Clear();
    }
}
