using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

/// <summary>
/// Create By 悠游课堂 http://www.u3dol.com 
/// </summary>
public static partial class DTSys_CodeListExt
{
    private static Dictionary<int, DTSys_Code?> m_Dic = new Dictionary<int, DTSys_Code?>();
    private static List<DTSys_Code> m_List = new List<DTSys_Code>();

    #region LoadData 加载数据表数据
    /// <summary>
    /// 加载数据表数据
    /// </summary>
    public static void LoadData(this DTSys_CodeList dtsys_codeList)
    {
        GameEntry.DataTable.RegisterTableLoad(DataTableDefine.DTSys_CodeName);

        //1.拿到这个表格的buffer
        GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTSys_CodeName, (byte[] buffer) =>
        {
            //2.加载数据 并 把数据初始化到字典
            Init(DTSys_CodeList.GetRootAsDTSys_CodeList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    /// <summary>
    /// 初始化到字典
    /// </summary>
    public static void Init(DTSys_CodeList dtsys_codeList)
    {
        GameEntry.DataTable.RunTableLoad(DataTableDefine.DTSys_CodeName, DataTableDefine.DTSys_CodeVersion, () =>
        {
            var nextDic = new Dictionary<int, DTSys_Code?>();
            var nextList = new List<DTSys_Code>();
            int len = dtsys_codeList.DTSysCodesLength;
            for (int j = 0; j < len; j++)
            {
                DTSys_Code ? dtsys_code = dtsys_codeList.DTSysCodes(j);
                if (dtsys_code != null)
                {
                    nextList.Add(dtsys_code.Value);
                    nextDic[dtsys_code.Value.Id] = dtsys_code;
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
    public static DTSys_Code? GetEntity(this DTSys_CodeList dtsys_codeList, int id)
    {
        DTSys_Code ? dtsys_code;
        m_Dic.TryGetValue(id, out dtsys_code);
        return dtsys_code;
    }

    /// <summary>
    /// 获取数据实体值
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTSys_Code GetEntityValue(this DTSys_CodeList dtsys_codeList, int id)
    {
        DTSys_Code ? dtsys_code = dtsys_codeList.GetEntity(id);
        if (dtsys_code != null)
        {
            return dtsys_code.Value;
        }
        return default;
    }

    /// <summary>
    /// 获取列表
    /// </summary>
    /// <returns></returns>
    public static List<DTSys_Code> GetList(this DTSys_CodeList dtsys_codeList)
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
