using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine;

[CreateAssetMenu]
public class MacroSettings : ScriptableObject
{
    private string m_Macor;

    [BoxGroup("MacroSettings")]
    [TableList(ShowIndexLabels = true, AlwaysExpanded = true)]
    [HideLabel]
    public MacroData[] Settings;

    //ButtonSizes.Medium 这个属性表示按钮的高度
    //ResponsiveButtonGroup("DefaultButtonSize") 有这句话 按钮会排序
    //ResponsiveButtonGroup 表示按钮的分组 如果分组名字一样 横着排序 如果不一样 竖着排序
    //PropertyOrder 表示当前绘制的所有组件的顺序

    [Button(ButtonSizes.Medium), ResponsiveButtonGroup("DefaultButtonSize"), PropertyOrder(1)]
    public void SavaMacro()
    {
#if UNITY_EDITOR
        m_Macor = string.Empty;
        foreach (var item in Settings ?? Array.Empty<MacroData>())
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Macro)) continue;
            if (item.Enabled)
            {
                m_Macor += string.Format("{0};", item.Macro);
            }

            if (item.Macro.Equals("DISABLE_ASSETBUNDLE", System.StringComparison.CurrentCultureIgnoreCase))
            {
                EditorBuildSettingsScene[] arrScene = EditorBuildSettings.scenes;
                for (int i = 0; i < arrScene.Length; i++)
                {
                    if (arrScene[i].path.IndexOf("download", System.StringComparison.CurrentCultureIgnoreCase) > -1)
                    {
                        arrScene[i].enabled = item.Enabled;
                    }
                }

                EditorBuildSettings.scenes = arrScene;
            }
        }
        SaveForGroup(BuildTargetGroup.Android);
        SaveForGroup(BuildTargetGroup.iOS);
        SaveForGroup(BuildTargetGroup.Standalone);
        Debug.Log("Sava Macro Success");
#endif
    }

#if UNITY_EDITOR
    private void SaveForGroup(BuildTargetGroup group)
    {
        // This asset owns only the listed framework symbols; preserve the host project's other symbols.
        var symbols = new HashSet<string>(PlayerSettings.GetScriptingDefineSymbolsForGroup(group).Split(';'), StringComparer.Ordinal);
        symbols.Remove(string.Empty);
        foreach (var item in Settings ?? Array.Empty<MacroData>())
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Macro)) continue;
            if (item.Enabled) symbols.Add(item.Macro); else symbols.Remove(item.Macro);
        }
        var ordered = new List<string>(symbols);
        ordered.Sort(StringComparer.Ordinal);
        PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", ordered));
    }
#endif

    private void OnEnable()
    {
#if UNITY_EDITOR
        m_Macor = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
        var activeSymbols = new HashSet<string>(m_Macor.Split(';'), StringComparer.Ordinal);

        for (int i = 0; Settings != null && i < Settings.Length; i++)
        {
            if (Settings[i] == null) continue;
            if (activeSymbols.Contains(Settings[i].Macro))
            {
                Settings[i].Enabled = true;
            }
            else
            {
                Settings[i].Enabled = false;
            }
        }
#endif
    }

    //必须加上可序列化标记
    [Serializable]
    public class MacroData
    {
        [TableColumnWidth(80, Resizable = false)]
        /// <summary>
        /// 启用
        /// </summary>
        public bool Enabled;

        /// <summary>
        /// 宏名称
        /// </summary>
        public string Name;

        /// <summary>
        /// 宏
        /// </summary>
        public string Macro;
    }
}
