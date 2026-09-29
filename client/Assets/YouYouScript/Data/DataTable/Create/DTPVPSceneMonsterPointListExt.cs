using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

/// <summary>
/// Create By 悠游课堂 http://www.u3dol.com 
/// </summary>
public static partial class DTPVPSceneMonsterPointListExt
{
    private static Dictionary<int, DTPVPSceneMonsterPoint?> m_Dic = new Dictionary<int, DTPVPSceneMonsterPoint?>();
    private static List<DTPVPSceneMonsterPoint> m_List = new List<DTPVPSceneMonsterPoint>();

    #region LoadData 加载数据表数据
    /// <summary>
    /// 加载数据表数据
    /// </summary>
    public static void LoadData(this DTPVPSceneMonsterPointList dtpvpscenemonsterpointList)
    {
        GameEntry.DataTable.RegisterTableLoad(DataTableDefine.DTPVPSceneMonsterPointName);

        //1.拿到这个表格的buffer
        GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTPVPSceneMonsterPointName, (byte[] buffer) =>
        {
            //2.加载数据 并 把数据初始化到字典
            Init(DTPVPSceneMonsterPointList.GetRootAsDTPVPSceneMonsterPointList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    /// <summary>
    /// 初始化到字典
    /// </summary>
    public static void Init(DTPVPSceneMonsterPointList dtpvpscenemonsterpointList)
    {
        GameEntry.DataTable.RunTableLoad(DataTableDefine.DTPVPSceneMonsterPointName, DataTableDefine.DTPVPSceneMonsterPointVersion, () =>
        {
            var nextDic = new Dictionary<int, DTPVPSceneMonsterPoint?>();
            var nextList = new List<DTPVPSceneMonsterPoint>();
            int len = dtpvpscenemonsterpointList.DTPVPSceneMonsterPointsLength;
            for (int j = 0; j < len; j++)
            {
                DTPVPSceneMonsterPoint ? dtpvpscenemonsterpoint = dtpvpscenemonsterpointList.DTPVPSceneMonsterPoints(j);
                if (dtpvpscenemonsterpoint != null)
                {
                    nextList.Add(dtpvpscenemonsterpoint.Value);
                    nextDic[dtpvpscenemonsterpoint.Value.Id] = dtpvpscenemonsterpoint;
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
    public static DTPVPSceneMonsterPoint? GetEntity(this DTPVPSceneMonsterPointList dtpvpscenemonsterpointList, int id)
    {
        DTPVPSceneMonsterPoint ? dtpvpscenemonsterpoint;
        m_Dic.TryGetValue(id, out dtpvpscenemonsterpoint);
        return dtpvpscenemonsterpoint;
    }

    /// <summary>
    /// 获取数据实体值
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTPVPSceneMonsterPoint GetEntityValue(this DTPVPSceneMonsterPointList dtpvpscenemonsterpointList, int id)
    {
        DTPVPSceneMonsterPoint ? dtpvpscenemonsterpoint = dtpvpscenemonsterpointList.GetEntity(id);
        if (dtpvpscenemonsterpoint != null)
        {
            return dtpvpscenemonsterpoint.Value;
        }
        return default;
    }

    /// <summary>
    /// 获取列表
    /// </summary>
    /// <returns></returns>
    public static List<DTPVPSceneMonsterPoint> GetList(this DTPVPSceneMonsterPointList dtpvpscenemonsterpointList)
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
