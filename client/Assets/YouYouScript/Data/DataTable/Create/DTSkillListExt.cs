using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

/// <summary>
/// Create By 悠游课堂 http://www.u3dol.com 
/// </summary>
public static partial class DTSkillListExt
{
    private static Dictionary<int, DTSkill?> m_Dic = new Dictionary<int, DTSkill?>();
    private static List<DTSkill> m_List = new List<DTSkill>();

    #region LoadData 加载数据表数据
    /// <summary>
    /// 加载数据表数据
    /// </summary>
    public static void LoadData(this DTSkillList dtskillList)
    {
        GameEntry.DataTable.RegisterTableLoad(DataTableDefine.DTSkillName);

        //1.拿到这个表格的buffer
        GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTSkillName, (byte[] buffer) =>
        {
            //2.加载数据 并 把数据初始化到字典
            Init(DTSkillList.GetRootAsDTSkillList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    /// <summary>
    /// 初始化到字典
    /// </summary>
    public static void Init(DTSkillList dtskillList)
    {
        GameEntry.DataTable.RunTableLoad(DataTableDefine.DTSkillName, DataTableDefine.DTSkillVersion, () =>
        {
            var nextDic = new Dictionary<int, DTSkill?>();
            var nextList = new List<DTSkill>();
            int len = dtskillList.DTSkillsLength;
            for (int j = 0; j < len; j++)
            {
                DTSkill ? dtskill = dtskillList.DTSkills(j);
                if (dtskill != null)
                {
                    nextList.Add(dtskill.Value);
                    nextDic[dtskill.Value.Id] = dtskill;
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
    public static DTSkill? GetEntity(this DTSkillList dtskillList, int id)
    {
        DTSkill ? dtskill;
        m_Dic.TryGetValue(id, out dtskill);
        return dtskill;
    }

    /// <summary>
    /// 获取数据实体值
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTSkill GetEntityValue(this DTSkillList dtskillList, int id)
    {
        DTSkill ? dtskill = dtskillList.GetEntity(id);
        if (dtskill != null)
        {
            return dtskill.Value;
        }
        return default;
    }

    /// <summary>
    /// 获取列表
    /// </summary>
    /// <returns></returns>
    public static List<DTSkill> GetList(this DTSkillList dtskillList)
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
