using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace YouYou
{
    public class DownloadHandler : DownloadHandlerScript
    {
        /// <summary>
        /// 初始化下载句柄，定义每次下载的数据上限为200kb
        /// </summary>
        /// <param name="filePath">保存到本地的文件路径</param>
        public DownloadHandler() : base(new byte[1024 * 200])
        {

        }

        /// <summary>
        /// 接收到数据委托
        /// </summary>
        public BaseAction<byte[], int> OnReceiveDataAction;

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || data.Length == 0)
            {
                return false;
            }

            OnReceiveDataAction?.Invoke(data, dataLength);
            return true;
        }
    }
}