//===================================================
//作    者：边涯  http://www.u3dol.com
//创建时间：
//备    注：
//===================================================
using Google.Protobuf;
using UnityEngine;
using YouYou;
using YouYou.Proto;

public class TestSocket : MonoBehaviour
{
    void Start()
    {
        
    }

    void Update()
    {
        if (Input.GetKeyUp(KeyCode.A))
        {
            GameEntry.Socket.ConnectToMainSocket("192.168.0.109", 1304, null);
        }
        else if (Input.GetKeyUp(KeyCode.B))
        {
            C2GWS_RegClient proto = new C2GWS_RegClient();
            proto.AccountId = GameEntry.Data.UserDataManager.ShareUserData.AccountId;
            GameEntry.Socket.SendMainMsg(proto);
        }
        else if (Input.GetKeyUp(KeyCode.C))
        {
            C2WS_CreateRole proto = new C2WS_CreateRole();
            proto.JobId = 1;
            proto.Sex = 1;
            proto.NickName = "protoYouyou你好01";

            GameEntry.Socket.SendMainMsg(proto);
        }
    }

    void TestA()
    {

    }

    void TestB()
    {

    }
}