using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YouYou;

public class TestTime : MonoBehaviour
{
    void Start()
    {
        Application.targetFrameRate = 50;

        string str = "";

        string text = IOUtil.GetFileText("E:/zmx/Lession/Web/index_CourseFour.html");
        string[] arr = text.Split('\n');
        foreach (var item in arr)
        {
            if (item.IndexOf("悠游课堂_Unity视频教程") > -1)
            {
                str += item.Replace("<div class=\"yy_cont1_t_bt_t yy_grey\">", "").Replace("</div>", "") + "\n";
            }
        }
        IOUtil.CreateTextFile("E:/zmx/Lession/Web/000.txt", str);
        Debug.LogError(str);
    }

    TimeAction action;

    void Update()
    {

        //Debug.LogError(Time.deltaTime);
    }
}