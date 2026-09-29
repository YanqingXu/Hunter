using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[CreateAssetMenu]
public class ShareDataSettings : ScriptableObject
{
    public enum ShareDataModule
    {
        ShareUserData,
        ShareOtherData
    }

    public enum ShareDataType
    {
        Int,
        Long,
        Double
    }

    [EnumToggleButtons]
    public ShareDataModule Modules;

    [ShowIf("Modules", ShareDataModule.ShareUserData)]
    public ShareData ShareUserData;

    [ShowIf("Modules", ShareDataModule.ShareOtherData)]
    public ShareData ShareOtherData;

    [Serializable]
    /// <summary>
    /// 共享数据
    /// </summary>
    public class ShareData
    {
        public string ClassName;

        public string ManagerName;

        [FolderPath(ParentFolder = "Assets")]
        /// <summary>
        /// CSharp路径
        /// </summary>
        public string CSharpScriptPath;


        public ShareDataField[] Fields;

        [Button(ButtonSizes.Medium)]
        [LabelText("生成数据脚本")]
        public void CreateShareDataScript()
        {
            #region 生成c#脚本
            StringBuilder sbrCSharp = new StringBuilder();
            sbrCSharp.AppendFormat("using System;\r\n");
            sbrCSharp.AppendFormat("\r\n");
            sbrCSharp.AppendFormat("public class {0} : IDisposable\r\n", ClassName);
            sbrCSharp.Append("{\r\n");
            int index = 0;
            foreach (ShareDataField shareDataField in Fields)
            {
                index++;
                sbrCSharp.AppendFormat("    /// <summary>\r\n");
                sbrCSharp.AppendFormat("    /// {0}\r\n", shareDataField.FieldDesc);
                sbrCSharp.AppendFormat("    /// </summary>\r\n");
                sbrCSharp.AppendFormat("    public {0} {1} {{ get; set; }}\r\n", shareDataField.Type.ToString().ToLower(), shareDataField.FieldName);
                sbrCSharp.AppendFormat("\r\n");
            }
            sbrCSharp.AppendFormat("    public void Dispose()\r\n");
            sbrCSharp.Append("    {\r\n");

            foreach (ShareDataField shareDataField in Fields)
            {
                sbrCSharp.AppendFormat("        {0} = 0;\r\n", shareDataField.FieldName);
            }
            sbrCSharp.Append("    }\r\n");
            sbrCSharp.Append("}");

            IOUtil.CreateTextFile(Application.dataPath + "/" + CSharpScriptPath + "/" + ClassName + ".cs", sbrCSharp.ToString());
            #endregion

            Debug.Log("生成=" + ClassName + "完毕");
        }
    }

    [Serializable]
    /// <summary>
    /// 共享数据字段
    /// </summary>
    public class ShareDataField
    {
        public ShareDataType Type;
        public string FieldDesc;
        public string FieldName;
    }
}