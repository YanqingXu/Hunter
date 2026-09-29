using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public class AOIManager : MonoBehaviour
{
    /// <summary>
    /// 区域预设
    /// </summary>
    public GameObject AreaPrefab;

    /// <summary>
    /// 区域宽度
    /// </summary>
    public int AreaWidth;

    /// <summary>
    /// 行数
    /// </summary>
    public int Rows;

    /// <summary>
    /// 列数
    /// </summary>
    public int Columns;

    /// <summary>
    /// 区域字典
    /// </summary>
    public static Dictionary<int, AOIArea> AOIAreaDic = new Dictionary<int, AOIArea>();

    [Button("创建AOI区域")]
    public void CreateArea()
    {
        Transform[] trans = transform.GetComponentsInChildren<Transform>();

        foreach (var item in trans)
        {
            if (item.gameObject.GetInstanceID() != transform.gameObject.GetInstanceID()
                && item.gameObject.GetInstanceID() != AreaPrefab.GetInstanceID())
            {
                DestroyImmediate(item.gameObject);
            }
        }
        AOIAreaDic.Clear();

        int num = 1;
        for (int i = 0; i < Rows; i++)
        {
            for (int j = 0; j < Columns; j++)
            {
                GameObject areaObj = Instantiate(AreaPrefab, transform, true);
                areaObj.transform.localScale = Vector3.one * AreaWidth;
                areaObj.transform.localPosition = new Vector3(j * AreaWidth, 0, i * AreaWidth * -1);
                areaObj.SetActive(true);
                areaObj.name = "area_" + num.ToString();

                AOIArea aoiArea = areaObj.GetComponent<AOIArea>();
                aoiArea.AreaId = num;
                aoiArea.CurrRow = i + 1;
                aoiArea.CurrColumn = j + 1;
                
                AOIAreaDic[aoiArea.AreaId] = aoiArea;
                
                num++;
            }
        }
        
        //计算关联
        foreach (var item in AOIAreaDic)
        {
            item.Value.SetConnectArea();
        }
    }

    [Button("显示可行走区域")]
    public void ShowCanRunArea()
    {
        //单元格 单行数量
        int cellCount = 0;

        //单元格宽度
        float cellWidth = 0.5f;
        cellCount = (int)(AreaWidth / cellWidth);


        for (int i = 0; i < Rows * cellCount; i++)
        {
            for (int j = 0; j < Columns * cellCount; j++)
            {
                NavMeshHit hit;

                //是否可行走
                bool canRun = false;

                //碰到了墙
                bool touchWall = false;

                Vector3 starPos = transform.position + new Vector3(j * cellWidth, -10, i * -1 * cellWidth);
                //发射射线检测 是否碰到了墙
                //Debug.DrawRay(starPos - new Vector3(0, 100, 0), Vector3.up * 100, Color.red, 5);
                if (Physics.Raycast(starPos - new Vector3(0, 100, 0), Vector3.up, 100, 1 << LayerMask.NameToLayer("Wall")))
                {
                    touchWall = true;
                }

                if (!touchWall)
                {
                    for (int k = -10; k < AreaWidth; ++k)
                    {
                        if (NavMesh.SamplePosition(starPos + new Vector3(0, k, 0), out hit, 0.5f, 1))
                        {
                            canRun = true;
                            break;
                        }
                    }
                }

                Debug.DrawRay(starPos, Vector3.up * 5, canRun ? Color.yellow : Color.red, 10);
            }
        }
    }

    [Button("创建AOI Json数据")]
    private void CreateAOIJsonData()
    {
        string path = string.Format("{0}/SceneAOIJsonData/{1}.json", Application.dataPath, SceneManager.GetActiveScene().name);

        List<AOIAreaData> lst = new List<AOIAreaData>();

        foreach (var item in AOIAreaDic)
        {
            lst.Add(item.Value.CreateAOIAreaData());
        }

        string json = LitJson.JsonMapper.ToJson(lst);
        IOUtil.CreateTextFile(path, json);
#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
        Debug.Log("创建场景AOI区域数据完毕=>" + path);
    }
}