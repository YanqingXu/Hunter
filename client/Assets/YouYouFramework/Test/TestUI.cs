//===================================================
//作    者：边涯  http://www.u3dol.com
//创建时间：
//备    注：
//===================================================
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YouYou;
using YouYou.DataTable;

public class TestUI : MonoBehaviour
{
    //SafeInteger money = 0; //必须先赋值

    public TMPro.TMP_FontAsset asset;

    public Font m_Font;

    void Start()
    {
#if UNITY_EDITOR
        TMP_FontAsset newAsset = ScriptableObject.CreateInstance<TMP_FontAsset>();

        //TMP_FontAsset newAsset=
        //TMP_FontAsset.CreateFontAsset(m_Font, 40, 10, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);


        UnityEditor.AssetDatabase.CreateAsset(newAsset, "Assets/123/newAsset.asset");
#endif

        //string str = Application.systemLanguage.ToString();

        //Debug.LogError("str=" + str);

        //int a = 100;

        //GameEntry.Localization.GetString("");


        //money++;

        //money += 20;

        //Debug.LogError("money=" + money);

        //ChapterEntity chapterEntity = GameEntry.DataTable.ChapterDBModel.Get(1);


        //Chapter chapter1 = chapter.Value;
        //int a = chapter1.Id;
    }

    void Update()
    {
        if (Input.GetKeyUp(KeyCode.B))
        {
            GameEntry.UI.OpenUIForm(UIFormId.UI_Login);

        }
        else if (Input.GetKeyUp(KeyCode.C))
        {
            GameEntry.UI.OpenUIForm(UIFormId.UI_Reg);
        }
        else if (Input.GetKeyUp(KeyCode.T))
        {
            GameEntry.Event.CommonEvent.Dispatch(1);

            Button btn = null;
            btn.gameObject.SetActive(false);
            //DTChapter chapter = GameEntry.DataTable.ChapterList.GetEntityValue(1);
            //string chapterName = chapter.ChapterName;
            //Debug.LogError("ChapterName==" + chapterName);
            //int len = chapter.BranchLevelIdLength;
            //for (int i = 0; i < len; i++)
            //{
            //    Debug.LogError("BranchLevelId==" + chapter.BranchLevelId(i));
            //}

            //len = chapter.BranchLevelNameLength;
            //for (int i = 0; i < len; i++)
            //{
            //    Debug.LogError("BranchLevelId==" + chapter.BranchLevelName(i));
            //}

            //Sys_Prefab prefab = GameEntry.DataTable.Sys_PrefabList.GetEntityValue(1);
            //string aa = prefab.AssetPath;
            //Debug.LogError("AssetPath==" + aa);
        }

    }
}
