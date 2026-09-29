//===================================================
//作    者：边涯  http://www.u3dol.com
//创建时间：
//备    注：
//===================================================
using Google.Protobuf;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace YouYou
{
    /// <summary>
    /// SocketTcp访问器
    /// </summary>
    public class SocketTcpRoutine
    {
        #region 发送消息所需变量
        //发送消息队列
        private Queue<byte[]> m_SendQueue = new Queue<byte[]>();

        //压缩数组的长度界限
        private const int m_CompressLen = 512;
        #endregion

        //是否连接成功
        private volatile bool m_IsDoConnected;
        private volatile bool m_IsConnectedSuccess;
        private volatile bool m_IsSending;
        private volatile bool m_IsConnecting;
        private readonly object m_ConnectionLock = new object();
        internal bool IsConnected { get { return m_IsConnectedSuccess; } }

        #region 发送接收消息所需变量
        //接收数据包的字节数组缓冲区
        private byte[] m_ReceiveBuffer = new byte[1024];

        //接收数据包的缓冲数据流
        private MMO_MemoryStream m_ReceiveMS = new MMO_MemoryStream();

        /// <summary>
        /// 发送用的MS
        /// </summary>
        private MMO_MemoryStream m_SocketSendMS = new MMO_MemoryStream();

        /// <summary>
        /// 接收用的MS
        /// </summary>
        private MMO_MemoryStream m_SocketReceiveMS = new MMO_MemoryStream();

        //接收消息的队列
        private Queue<byte[]> m_ReceiveQueue = new Queue<byte[]>();

        private int m_ReceiveCount = 0;

        /// <summary>
        /// 这一帧发送了多少
        /// </summary>
        private int m_SendCount = 0;

        /// <summary>
        /// 是否有未处理的字节
        /// </summary>
        private bool m_IsHasUnDealBytes = false;

        /// <summary>
        /// 未处理的字节
        /// </summary>
        private byte[] m_UnDealBytes = null;
        #endregion

        /// <summary>
        /// 客户端socket
        /// </summary>
        private Socket m_Client;

        public BaseAction<bool> m_OnConnectComplete;

        internal void OnUpdate()
        {
            if (m_IsDoConnected)
            {
                m_IsDoConnected = false;
                //Debug.LogError("m_IsConnectedSuccess=" + m_IsConnectedSuccess);
                if (!m_IsConnectedSuccess)
                {
                    GameEntry.Socket.RemoveSocketTcpRoutine(this);
                }
                var connected = m_OnConnectComplete;
                m_OnConnectComplete = null;
                connected?.Invoke(m_IsConnectedSuccess);
                //Debug.Log("连接成功");
            }

            if (!m_IsConnectedSuccess)
            {
                if (!m_IsConnecting) GameEntry.Socket.RemoveSocketTcpRoutine(this);
                return;
            }

            #region 从队列中获取数据
            while (true)
            {
                if (m_ReceiveCount <= GameEntry.Socket.MaxReceiveCount)
                {
                    m_ReceiveCount++;
                    byte[] buffer;
                    lock (m_ReceiveQueue)
                        buffer = m_ReceiveQueue.Count > 0 ? m_ReceiveQueue.Dequeue() : null;
                    {
                        if (buffer != null)
                        {
                            MMO_MemoryStream ms = m_SocketReceiveMS;
                            ms.SetLength(0);
                            ms.Write(buffer, 0, buffer.Length);
                            ms.Position = 0;

                            byte header = (byte)ms.ReadByte();
                            bool isCompress = BitUtil.GetBit(header, 0) == 1;
                            bool isEncrypt = BitUtil.GetBit(header, 1) == 1;

                            //协议编号
                            ushort protoId = ms.ReadUShort();
                            ProtoCategory protoCategory = (ProtoCategory)ms.ReadByte();

                            //定义小包体 也就是真正的protobuf协议数据
                            byte[] protoData = new byte[buffer.Length - 4];
                            Array.Copy(buffer, 4, protoData, 0, protoData.Length);
                            if (isEncrypt)
                            {
                                //解密
                                protoData = XXTEAUtil.Decrypt(protoData);
                            }

                            if (isCompress)
                            {
                                //解压
                                protoData = GZipUtil.Decompress(protoData);
                            }

                            GameEntry.Event.SocketEvent.Dispatch(protoId, protoData);
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                else
                {
                    m_ReceiveCount = 0;
                    break;
                }
            }
            #endregion

            CheckSendQueue();
        }

        #region Connect 连接到socket服务器
        /// <summary>
        /// 连接到socket服务器
        /// </summary>
        /// <param name="ip">ip</param>
        /// <param name="port">端口号</param>
        public void Connect(string ip, int port, BaseAction<bool> onConnectComplete)
        {
            //Debug.LogError("Connect ip=" + ip + " port=" + port);
            //如果socket已经存在 并且处于连接中状态 则直接返回
            if (m_Client != null && m_Client.Connected)
            {
                Debug.LogError("001");
                onConnectComplete?.Invoke(false);
                return;
            }
            DisConnect();
            m_OnConnectComplete = onConnectComplete;
            m_IsConnectedSuccess = false;
            m_IsConnecting = true;

            string newServerIp = ip;
            AddressFamily addressFamily = AddressFamily.InterNetwork;

#if UNITY_IPHONE && !UNITY_EDITOR && SDKCHANNEL_APPLE_STORE
        AppleStoreInterface.GetIPv6Type(ip, port.ToString(), out newServerIp, out addressFamily);
#endif

            m_Client = new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                GameEntry.Socket.RegisterSocketTcpRoutine(this);
                m_Client.BeginConnect(new IPEndPoint(IPAddress.Parse(newServerIp), port), ConnectCallBack, m_Client);
            }
            catch (Exception ex)
            {
                m_IsConnecting = false;
                m_IsDoConnected = true;
                GameEntry.LogError("连接失败=" + ex.Message);
            }
        }

        private void ConnectCallBack(IAsyncResult ar)
        {
            Socket client = (Socket)ar.AsyncState;
            try
            {
                client.EndConnect(ar);
                lock (m_ConnectionLock)
                {
                    if (!ReferenceEquals(client, m_Client)) return;
                    m_IsConnectedSuccess = client.Connected;
                    m_IsConnecting = false;
                    m_IsDoConnected = true;
                    if (m_IsConnectedSuccess) ReceiveMsg();
                }
            }
            catch (SocketException)
            {
                lock (m_ConnectionLock)
                    if (ReferenceEquals(client, m_Client)) { m_IsConnectedSuccess = false; m_IsConnecting = false; m_IsDoConnected = true; }
            }
            catch (ObjectDisposedException) { }
        }
        #endregion

        /// <summary>
        /// 断开连接
        /// </summary>
        public void DisConnect()
        {
            lock (m_ConnectionLock)
            {
                Socket client = m_Client;
                m_Client = null;
                m_IsConnectedSuccess = false;
                m_IsDoConnected = false;
                m_IsSending = false;
                m_IsConnecting = false;
                m_OnConnectComplete = null;
                if (client != null)
                {
                    try { if (client.Connected) client.Shutdown(SocketShutdown.Both); }
                    catch (SocketException) { }
                    catch (ObjectDisposedException) { }
                    client.Close();
                }
                m_ReceiveMS.SetLength(0);
                lock (m_SendQueue) m_SendQueue.Clear();
                lock (m_ReceiveQueue) m_ReceiveQueue.Clear();
                m_IsHasUnDealBytes = false;
                m_UnDealBytes = null;
                m_ReceiveCount = m_SendCount = 0;
            }
            GameEntry.Socket?.RemoveSocketTcpRoutine(this);
        }

        #region CheckSendQueue 检查发送队列
        /// <summary>
        /// 检查发送队列
        /// </summary>
        private void CheckSendQueue()
        {
            if (m_IsSending) return;
            while (true)
            {
                if (m_SendCount >= GameEntry.Socket.MaxSendCount)
                {
                    //等待下一帧发送
                    m_SendCount = 0;
                    break;
                }

                lock (m_SendQueue)
                {
                    if (m_SendQueue.Count > 0 || m_IsHasUnDealBytes)
                    {
                        //int smallCount = 0;

                        MMO_MemoryStream ms = m_SocketSendMS;
                        ms.SetLength(0);

                        //先处理未处理的包
                        if (m_IsHasUnDealBytes)
                        {
                            m_IsHasUnDealBytes = false;
                            ms.Write(m_UnDealBytes, 0, m_UnDealBytes.Length);
                            //smallCount++;
                        }

                        while (true)
                        {
                            if (m_SendQueue.Count == 0)
                            {
                                break;
                            }

                            //取出一个字节数组
                            byte[] buffer = m_SendQueue.Dequeue();

                            if ((buffer.Length + ms.Length) <= GameEntry.Socket.MaxSendByteCount)
                            {
                                //smallCount++;
                                ms.Write(buffer, 0, buffer.Length);
                            }
                            else
                            {
                                //已经取出了一个要发送的字节数组
                                m_UnDealBytes = buffer;
                                m_IsHasUnDealBytes = true;
                                break; //非常重要
                            }
                        }

                        m_SendCount++;
                        //Debug.LogError("拼凑了小包数量=" + smallCount);
                        Send(ms.ToArray());
                        break; // Complete all partial sends before beginning the next batch.
                    }
                    else
                    { //不加这个会死循环
                        break;
                    }
                }
            }
        }
        #endregion

        #region MakeData 封装数据包
        /// <summary>
        /// 封装数据包
        /// </summary>
        /// <param name="proto"></param>
        /// <returns></returns>
        private byte[] MakeData(IProto proto)
        {
            byte header = 0; //byte，1-2-3-4-5-6-7-8 |1-是否压缩|2-是否加密

            byte[] buffer = proto.ToByteArray();
            bool isCompress = buffer.Length > m_CompressLen;
            if (isCompress)
            {
                header = BitUtil.SetBit(header, 0);
                buffer = GZipUtil.Compress(buffer);
            }

            //2.加密
            header = BitUtil.SetBit(header, 1);
            buffer = XXTEAUtil.Encrypt(buffer);

            MMO_MemoryStream ms = this.m_SocketSendMS;
            ms.SetLength(0);

            ms.WriteUShort((ushort)(buffer.Length + 4)); //4=header 1 + ProtoId 2 + Category 1

            ms.WriteByte(header);
            ms.WriteUShort(proto.ProtoId);
            ms.WriteByte((byte)proto.Category);

            ms.Write(buffer, 0, buffer.Length);

            var retBuffer = ms.ToArray();
            return retBuffer;
        }

        /// <summary>
        /// 封装数据包
        /// </summary>
        /// <param name="protoId"></param>
        /// <param name="category"></param>
        /// <param name="buffer"></param>
        /// <returns></returns>
        private byte[] MakeData(ushort protoId, byte category, byte[] buffer)
        {
            byte header = 0; //byte，1-2-3-4-5-6-7-8 |1-是否压缩|2-是否加密


            bool isCompress = buffer.Length > m_CompressLen;
            if (isCompress)
            {
                header = BitUtil.SetBit(header, 0);
                buffer = GZipUtil.Compress(buffer);
            }

            //2.加密
            header = BitUtil.SetBit(header, 1);
            buffer = XXTEAUtil.Encrypt(buffer);

            MMO_MemoryStream ms = this.m_SocketSendMS;
            ms.SetLength(0);

            ms.WriteUShort((ushort)(buffer.Length + 4)); //4=header 1 + ProtoId 2 + Category 1

            ms.WriteByte(header);
            ms.WriteUShort(protoId);
            ms.WriteByte(category);

            ms.Write(buffer, 0, buffer.Length);

            var retBuffer = ms.ToArray();
            return retBuffer;
        }
        #endregion

        #region SendMsg 发送消息 把消息加入队列
        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="buffer"></param>
        public void SendMsg(IProto proto)
        {
            //得到封装后的数据包
            byte[] sendBuffer = MakeData(proto);

            lock (m_SendQueue)
            {
                //把数据包加入队列
                m_SendQueue.Enqueue(sendBuffer);
            }
        }

        public void SendMsg(ushort protoId, byte category, byte[] buffer)
        {
            //得到封装后的数据包
            byte[] sendBuffer = MakeData(protoId, category, buffer);

            lock (m_SendQueue)
            {
                //把数据包加入队列
                m_SendQueue.Enqueue(sendBuffer);
            }
        }
        #endregion

        #region Send 真正发送数据包到服务器
        /// <summary>
        /// 真正发送数据包到服务器
        /// </summary>
        /// <param name="buffer"></param>
        private void Send(byte[] buffer)
        {
            if (buffer.Length == 0) return;
            lock (m_ConnectionLock)
            {
                if (m_Client == null || !m_IsConnectedSuccess) return;
                m_IsSending = true;
                BeginSend(new PendingSend { Client = m_Client, Buffer = buffer });
            }
        }
        private sealed class PendingSend { public Socket Client; public byte[] Buffer; public int Offset; }

        private void BeginSend(PendingSend pending)
        {
            try { pending.Client.BeginSend(pending.Buffer, pending.Offset, pending.Buffer.Length - pending.Offset, SocketFlags.None, SendCallBack, pending); }
            catch (SocketException) { m_IsConnectedSuccess = false; m_IsSending = false; }
            catch (ObjectDisposedException) { m_IsConnectedSuccess = false; m_IsSending = false; }
        }
        #endregion

        #region SendCallBack 发送数据包的回调
        /// <summary>
        /// 发送数据包的回调
        /// </summary>
        /// <param name="ar"></param>
        private void SendCallBack(IAsyncResult ar)
        {
            var pending = (PendingSend)ar.AsyncState;
            try
            {
                int sent = pending.Client.EndSend(ar);
                lock (m_ConnectionLock)
                {
                    if (!ReferenceEquals(pending.Client, m_Client)) return;
                    if (sent <= 0) { m_IsConnectedSuccess = false; m_IsSending = false; return; }
                    pending.Offset += sent;
                    if (pending.Offset < pending.Buffer.Length) BeginSend(pending);
                    else m_IsSending = false;
                }
            }
            catch (SocketException) { MarkConnectionLost(pending.Client); }
            catch (ObjectDisposedException) { MarkConnectionLost(pending.Client); }
        }

        private void MarkConnectionLost(Socket client)
        {
            lock (m_ConnectionLock)
                if (ReferenceEquals(client, m_Client)) { m_IsConnectedSuccess = false; m_IsSending = false; }
        }
        #endregion

        //====================================================

        #region ReceiveMsg 接收数据
        /// <summary>
        /// 接收数据
        /// </summary>
        private void ReceiveMsg()
        {
            //异步接收数据
            m_Client.BeginReceive(m_ReceiveBuffer, 0, m_ReceiveBuffer.Length, SocketFlags.None, ReceiveCallBack, m_Client);
        }
        #endregion

        #region IsSocketConnected 判断socket是否连接
        /// <summary>
        /// 判断socket是否连接
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        bool IsSocketConnected(Socket s)
        {
            return !((s.Poll(1000, SelectMode.SelectRead) && (s.Available == 0)) || !s.Connected);
        }
        #endregion

        #region ReceiveCallBack 接收数据回调
        /// <summary>
        /// 接收数据回调
        /// </summary>
        /// <param name="ar"></param>
        private void ReceiveCallBack(IAsyncResult ar)
        {
            Socket client = (Socket)ar.AsyncState;
            try
            {
                int len = client.EndReceive(ar);
                lock (m_ConnectionLock)
                {
                    if (!ReferenceEquals(client, m_Client)) return;

                    if (len > 0)
                    {
                        //已经接收到数据
                        //把接收到数据 写入缓冲数据流的尾部
                        m_ReceiveMS.Position = m_ReceiveMS.Length;

                        //把指定长度的字节 写入数据流
                        m_ReceiveMS.Write(m_ReceiveBuffer, 0, len);

                        //如果缓存数据流的长度>2 说明至少有个不完整的包过来了
                        //为什么这里是2 因为我们客户端封装数据包 用的ushort 长度就是2
                        if (m_ReceiveMS.Length > 2)
                        {
                            //进行循环 拆分数据包
                            while (m_ReceiveMS.Length >= 2)
                            {
                                //把数据流指针位置放在0处
                                m_ReceiveMS.Position = 0;

                                //currMsgLen = 包体的长度
                                int currMsgLen = m_ReceiveMS.ReadUShort();
                                if (currMsgLen < 4) { m_IsConnectedSuccess = false; return; }

                                //currFullMsgLen 总包的长度=包头长度+包体长度
                                int currFullMsgLen = 2 + currMsgLen;

                                //如果数据流的长度>=整包的长度 说明至少收到了一个完整包
                                if (m_ReceiveMS.Length >= currFullMsgLen)
                                {
                                    //至少收到一个完整包
                                    //定义包体的byte[]数组
                                    byte[] buffer = new byte[currMsgLen];

                                    //把数据流指针放到2的位置 也就是包体的位置
                                    m_ReceiveMS.Position = 2;

                                    //把包体读到byte[]数组
                                    m_ReceiveMS.Read(buffer, 0, currMsgLen);

                                    lock (m_ReceiveQueue)
                                    {
                                        m_ReceiveQueue.Enqueue(buffer);
                                    }
                                    //==============处理剩余字节数组===================

                                    //剩余字节长度
                                    int remainLen = (int)m_ReceiveMS.Length - currFullMsgLen;
                                    if (remainLen > 0)
                                    {
                                        //把指针放在第一个包的尾部
                                        m_ReceiveMS.Position = currFullMsgLen;

                                        //定义剩余字节数组
                                        byte[] remainBuffer = new byte[remainLen];

                                        //把数据流读到剩余字节数组
                                        m_ReceiveMS.Read(remainBuffer, 0, remainLen);

                                        //清空数据流
                                        m_ReceiveMS.Position = 0;
                                        m_ReceiveMS.SetLength(0);

                                        //把剩余字节数组重新写入数据流
                                        m_ReceiveMS.Write(remainBuffer, 0, remainBuffer.Length);

                                        remainBuffer = null;
                                    }
                                    else
                                    {
                                        //没有剩余字节

                                        //清空数据流
                                        m_ReceiveMS.Position = 0;
                                        m_ReceiveMS.SetLength(0);

                                        break;
                                    }
                                }
                                else
                                {
                                    //还没有收到完整包
                                    break;
                                }
                            }
                        }

                        //进行下一次接收数据包
                        ReceiveMsg();
                    }
                    else
                    {
                        //客户端断开连接
                        m_IsConnectedSuccess = false;
                    }
                }
            }
            catch (SocketException) { MarkConnectionLost(client); }
            catch (ObjectDisposedException) { MarkConnectionLost(client); }
        }
        #endregion
    }
}
