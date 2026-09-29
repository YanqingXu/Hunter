using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YouYou.DataTable;

public static partial class DTSkillLevelListExt
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="lst"></param>
    /// <param name="skillId"></param>
    /// <param name="level"></param>
    /// <returns></returns>
    public static DTSkillLevel? GetSkillLevel(this DTSkillLevelList lst, int skillId, int level)
    {
        foreach (var item in m_List)
        {
            if (item.SkillId == skillId && item.Level == level)
            {
                return item;
            }
        }

        return null;
    }
}