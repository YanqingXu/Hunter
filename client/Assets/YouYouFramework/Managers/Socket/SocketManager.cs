using System;
using System.Collections.Generic;
using UnityEngine;
using YouYou.Proto;

namespace YouYou
{
    public class SocketManager : ManagerBase, IDisposable
    {
        [Header("每帧最大发送数量")]
        public int MaxSendCount = 5;

        [Header("每次发包最大的字节")]
        public int MaxSendByteCount = 1024;

        [Header("每帧最大处理包数量")]
        public int MaxReceiveCount = 5;

        [Header("心跳间隔")]
        public int HeartbeatInterval = 10;

        /// <summary>
        /// 上次心跳时间
        /// </summary>
        private float m_PrevHeartbeatTime = 0;

        /// <summary>
        /// PING值(毫秒)
        /// </summary>
        public int PingValue;

        /// <summary>
        /// 游戏服务器的时间
        /// </summary>
        public long LastServerTime;


        /// <summary>
        /// 是否已经连接到主Socket
        /// </summary>
        private bool m_IsConnectToMainSocket = false;

        /// <summary>
        /// 发送用的MS
        /// </summary>
        public MMO_MemoryStream SocketSendMS
        {
            get;
            private set;
        }

        /// <summary>
        /// 接收用的MS
        /// </summary>
        public MMO_MemoryStream SocketReceiveMS
        {
            get;
            private set;
        }

        /// <summary>
        /// SocketTcp访问器链表
        /// </summary>
        private LinkedList<SocketTcpRoutine> m_SocketTcpRoutineList;

        public SocketManager()
        {
            m_SocketTcpRoutineList = new LinkedList<SocketTcpRoutine>();
            SocketSendMS = new MMO_MemoryStream();
            SocketReceiveMS = new MMO_MemoryStream();
        }

        /// <summary>
        /// 初始化
        /// </summary>
        public override void Init()
        {
            HeartbeatInterval = GameEntry.ParamsSettings.GetGradeParamData(ConstDefine.HeartbeatInterval, GameEntry.CurrDeviceGrade);

            m_MainSocket = CreateSocketTcpRoutine();
            SocketProtoListener.AddProtoListener();
        }

        /// <summary>
        /// 创建SocketTcp访问器
        /// </summary>
        /// <returns></returns>
        public SocketTcpRoutine CreateSocketTcpRoutine()
        {
            //从池中获取（什么时候回池）
            return GameEntry.Pool.DequeueClassObject<SocketTcpRoutine>();
        }

        /// <summary>
        /// 注册SocketTcp访问器
        /// </summary>
        /// <param name="routine"></param>
        internal void RegisterSocketTcpRoutine(SocketTcpRoutine routine)
        {
            if (!m_SocketTcpRoutineList.Contains(routine)) m_SocketTcpRoutineList.AddFirst(routine);
        }

        /// <summary>
        /// 移除SocketTcp访问器
        /// </summary>
        /// <param name="routine"></param>
        internal void RemoveSocketTcpRoutine(SocketTcpRoutine routine)
        {
            m_SocketTcpRoutineList.Remove(routine);
        }

        internal void OnUpdate()
        {
            for (LinkedListNode<SocketTcpRoutine> curr = m_SocketTcpRoutineList.First; curr != null;)
            {
                var next = curr.Next;
                curr.Value.OnUpdate();
                curr = next;
            }

            if (m_IsConnectToMainSocket && m_MainSocket.IsConnected)
            {
                if (Time.realtimeSinceStartup > m_PrevHeartbeatTime + HeartbeatInterval)
                {
                    //发送心跳
                    m_PrevHeartbeatTime = Time.realtimeSinceStartup;

                    C2GWS_Heartbeat proto = new C2GWS_Heartbeat();
                    proto.Time = DateTime.UtcNow.Ticks;
                    proto.Ping = PingValue;
                    SendMainMsg(proto);
                }
            }
        }

        /// <summary>
        /// 主Socket
        /// </summary>
        private SocketTcpRoutine m_MainSocket;

        /// <summary>
        /// 连接主Socket
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="port"></param>
        public void ConnectToMainSocket(string ip, int port, BaseAction<bool> onConnectComplete)
        {
            m_MainSocket.Connect(ip, port, (bool result) =>
            {
                m_IsConnectToMainSocket = result;
                onConnectComplete?.Invoke(result);
            });
        }

        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="buffer"></param>
        public void SendMainMsg(IProto proto)
        {
#if DEBUG_LOG_PROTO
            if (proto.ProtoId != 10004)
            {
                Debug.Log("<color=#ffa200>发送消息:</color><color=#FFFB80>" + proto.ProtoEnName + " " + proto.ProtoId + "</color>");
                Debug.Log("<color=#ffdeb3>==>>" + proto.ToString() + "</color>");
            }
#endif
            m_MainSocket.SendMsg(proto);
        }

        /// <summary>
        /// 发送已经序列化的主连接协议数据
        /// </summary>
        /// <param name="protoId">消息编号</param>
        /// <param name="category">分类</param>
        /// <param name="buffer">消息体</param>
        public void SendRawMainMsg(ushort protoId, byte category, byte[] buffer)
        {
            m_MainSocket.SendMsg(protoId, category, buffer);
        }

        public void Dispose()
        {
            while (m_SocketTcpRoutineList.First != null)
                m_SocketTcpRoutineList.First.Value.DisConnect();

            m_IsConnectToMainSocket = false;

            if (m_MainSocket != null)
            {
                m_MainSocket.DisConnect();
                GameEntry.Pool?.EnqueueClassObject(m_MainSocket);
                m_MainSocket = null;
            }
            SocketProtoListener.RemoveProtoListener();

            SocketSendMS.Dispose();
            SocketReceiveMS.Dispose();

            SocketSendMS.Close();
            SocketReceiveMS.Close();
        }
    }
}
