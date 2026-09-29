using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YouYou;

public class UIGlobalForm : UIFormBase
{
    public Transform HeadBarContainer;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        GameEntry.Data.GlobalManager.HeadBarContainer = HeadBarContainer;
    }

    protected override void OnBeforeDestroy()
    {
        base.OnBeforeDestroy();
        GameEntry.Data.GlobalManager.HeadBarContainer = null;
    }
}
