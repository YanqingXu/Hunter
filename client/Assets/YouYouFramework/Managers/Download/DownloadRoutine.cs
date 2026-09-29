//===================================================
//作    者：边涯  http://www.u3dol.com
//创建时间：
//备    注：
//===================================================
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;

namespace YouYou
{
    /// <summary>
    /// 下载器
    /// </summary>
    public class DownloadRoutine
    {
        /// <summary>
        /// web请求
        /// </summary>
        private UnityWebRequest m_UnityWebRequest = null;

        /// <summary>
        /// 文件流
        /// </summary>
        private FileStream m_FileStream;

        /// <summary>
        /// 当前等待写入磁盘的大小
        /// </summary>
        private int m_CurrWaitFlushSize = 0;

        /// <summary>
        /// 上次写入的大小
        /// </summary>
        private int m_PrevWriteSize = 0;

        /// <summary>
        /// 文件总大小
        /// </summary>
        private ulong m_TotalSize;

        /// <summary>
        /// 当前下载的大小
        /// </summary>
        private ulong m_CurrDownloadedSize = 0;

        /// <summary>
        /// 起始位置
        /// </summary>
        private uint m_BeginPos = 0;

        /// <summary>
        /// 当前文件路径
        /// </summary>
        private string m_CurrFileUrl;

        /// <summary>
        /// 下载的本地文件路径
        /// </summary>
        private string m_DownloadLocalFilePath;

        /// <summary>
        /// 下载中委托
        /// </summary>
        private BaseAction<string, ulong, float> m_OnUpdate;

        /// <summary>
        /// 下载完毕委托
        /// </summary>
        private BaseAction<string, DownloadRoutine> m_OnComplete;

        /// <summary>
        /// 当前的资源包信息
        /// </summary>
        private AssetBundleInfoEntity m_CurrAssetBundleInfo;

        /// <summary>
        /// 当前重试次数
        /// </summary>
        private int m_CurrRetry = 0;

        /// <summary>
        /// 上次重试时间
        /// </summary>
        private float m_PrevRetryTime = 0;

        /// <summary>
        /// 开始下载
        /// </summary>
        /// <param name="url"></param>
        public void BeginDownload(string url, AssetBundleInfoEntity assetBundleInfo, BaseAction<string, ulong, float> onUpdate = null, BaseAction<string, DownloadRoutine> onComplete = null)
        {
            Reset();
            m_CurrRetry = 0;
            m_PrevRetryTime = 0;
            m_CurrFileUrl = url;
            m_CurrAssetBundleInfo = assetBundleInfo;
            m_OnUpdate = onUpdate;
            m_OnComplete = onComplete;

            m_DownloadLocalFilePath = string.Format("{0}/{1}", GameEntry.Resource.LocalFilePath, m_CurrFileUrl);

            //判断如果本地已经有目标文件 先删除
            if (File.Exists(m_DownloadLocalFilePath))
            {
                File.Delete(m_DownloadLocalFilePath);
            }

            m_DownloadLocalFilePath = m_DownloadLocalFilePath + ".temp";
            DownloadInner();
        }

        /// <summary>
        /// 内部下载
        /// </summary>
        private void DownloadInner()
        {
            if (File.Exists(m_DownloadLocalFilePath))
            {
                //Debug.LogError("验证MD5 如果以前残留文件的MD5和CDN的不一致 则删除本地临时文件 重新下载");
                if (PlayerPrefs.HasKey(m_CurrFileUrl))
                {
                    //验证 
                    if (!PlayerPrefs.GetString(m_CurrFileUrl).Equals(m_CurrAssetBundleInfo.MD5, System.StringComparison.CurrentCultureIgnoreCase))
                    {
                        //Debug.LogError("如果不一致 则删除本地临时文件 重新下载");
                        File.Delete(m_DownloadLocalFilePath);
                        BeginDownload();
                    }
                    else
                    {
                        //Debug.LogError("一致 继续下载");
                        m_FileStream = File.OpenWrite(m_DownloadLocalFilePath);
                        m_FileStream.Seek(0, SeekOrigin.End);
                        m_BeginPos = (uint)m_FileStream.Length;
                        Download(string.Format("{0}{1}", GameEntry.Data.SysDataManager.CurrChannelConfig.RealSourceUrl, m_CurrFileUrl), m_BeginPos);
                    }
                }
                else
                {
                    //Debug.LogError("本地没有MD5记录 重新下载");
                    File.Delete(m_DownloadLocalFilePath);
                    BeginDownload();
                }
            }
            else
            {
                BeginDownload();
            }
        }

        /// <summary>
        /// 进行下载
        /// </summary>
        private void BeginDownload()
        {
            m_BeginPos = 0;
            string directory = Path.GetDirectoryName(m_DownloadLocalFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            m_FileStream = new FileStream(m_DownloadLocalFilePath, FileMode.Create, FileAccess.Write);

            PlayerPrefs.SetString(m_CurrFileUrl, m_CurrAssetBundleInfo.MD5);
            //Debug.LogError("从头下载" + m_CurrFileUrl);
            Download(string.Format("{0}{1}", GameEntry.Data.SysDataManager.CurrChannelConfig.RealSourceUrl, m_CurrFileUrl));
        }

        /// <summary>
        /// 下载句柄
        /// </summary>
        private DownloadHandler m_DownloadHandler;

        /// <summary>
        /// 下载
        /// </summary>
        /// <param name="url"></param>
        private void Download(string url)
        {
            m_UnityWebRequest = UnityWebRequest.Get(url);
            m_DownloadHandler = new DownloadHandler();
            m_DownloadHandler.OnReceiveDataAction += DownloadHandlerReceiveDataCallBack;
            m_UnityWebRequest.downloadHandler = m_DownloadHandler;
            m_UnityWebRequest.disposeDownloadHandlerOnDispose = true;
            m_UnityWebRequest.SendWebRequest();
        }

        /// <summary>
        /// 下载
        /// </summary>
        /// <param name="url"></param>
        /// <param name="beginPos"></param>
        private void Download(string url, uint beginPos)
        {
            m_UnityWebRequest = UnityWebRequest.Get(url);
            m_DownloadHandler = new DownloadHandler();
            m_DownloadHandler.OnReceiveDataAction += DownloadHandlerReceiveDataCallBack;
            m_UnityWebRequest.downloadHandler = m_DownloadHandler;
            m_UnityWebRequest.disposeDownloadHandlerOnDispose = true;
            m_UnityWebRequest.SetRequestHeader("Range", string.Format("bytes={0}-", beginPos.ToString()));
            m_UnityWebRequest.SendWebRequest();
        }

        public void Reset()
        {
            if (m_UnityWebRequest != null)
            {
                m_UnityWebRequest.Abort();
                m_UnityWebRequest.Dispose();
                m_UnityWebRequest = null;
            }

            if (m_DownloadHandler != null)
            {
                m_DownloadHandler.OnReceiveDataAction -= DownloadHandlerReceiveDataCallBack;
                m_DownloadHandler = null;
            }

            if (m_FileStream != null)
            {
                m_FileStream.Close();
                m_FileStream.Dispose();
                m_FileStream = null;
            }

            m_PrevWriteSize = 0;
            m_TotalSize = 0;
            m_CurrDownloadedSize = 0;
            m_CurrWaitFlushSize = 0;
            m_BeginPos = 0;
        }

        internal void Cancel()
        {
            Reset();
            m_OnUpdate = null;
            m_OnComplete = null;
            m_CurrAssetBundleInfo = null;
        }

        /// <summary>
        /// 更新
        /// </summary>
        public void OnUpdate()
        {
            if (m_UnityWebRequest == null)
            {
                return;
            }

            //如果进行了重试 判断重试间隔
            if (m_CurrRetry > 0 && Time.time < m_PrevRetryTime + GameEntry.Download.RetryInterval)
            {
                return;
            }

            if (m_TotalSize == 0)
            {
                ulong.TryParse(m_UnityWebRequest.GetResponseHeader("Content-Length"), out m_TotalSize);
                if (m_TotalSize > 0) m_TotalSize += m_BeginPos;
            }

            if (!m_UnityWebRequest.isDone)
            {
                if (m_CurrDownloadedSize < m_BeginPos + m_UnityWebRequest.downloadedBytes)
                {
                    m_CurrDownloadedSize = m_BeginPos + m_UnityWebRequest.downloadedBytes;

                    //this.Sava(m_UnityWebRequest.downloadHandler.data);

                    m_OnUpdate?.Invoke(m_CurrFileUrl, m_CurrDownloadedSize, m_TotalSize == 0 ? 0 : m_CurrDownloadedSize / (float)m_TotalSize);
                }
                return;
            }

            if (m_UnityWebRequest.isNetworkError || m_UnityWebRequest.isHttpError)
            {
                m_CurrRetry++;
                m_PrevRetryTime = Time.time;
                if (m_CurrRetry < GameEntry.Download.Retry)
                {
                    string failedUrl = m_UnityWebRequest.url;
                    Reset();
                    GameEntry.Log(LogCategory.Resource, "下载完毕url=>{0}失败 当前重试次数{1}", failedUrl, m_CurrRetry);
                    //重新尝试下载
                    DownloadInner();
                    return;
                }
                GameEntry.LogError("下载失败url=>{0} error=>{1}", m_UnityWebRequest.url, m_UnityWebRequest.error);
                Reset();
            }
            else
            {
                m_CurrDownloadedSize = m_BeginPos + m_UnityWebRequest.downloadedBytes;
                //this.Sava(m_UnityWebRequest.downloadHandler.data, true);

                m_OnUpdate?.Invoke(m_CurrFileUrl, m_CurrDownloadedSize, m_TotalSize == 0 ? 0 : m_CurrDownloadedSize / (float)m_TotalSize);
                GameEntry.Log(LogCategory.Resource, "下载完毕url=>{0}", m_UnityWebRequest.url);
                Reset();

                // A successful HTTP response alone must not publish an incomplete/corrupt bundle.
                string md5;
                using (var stream = File.OpenRead(m_DownloadLocalFilePath))
                using (var hasher = MD5.Create())
                    md5 = System.BitConverter.ToString(hasher.ComputeHash(stream)).Replace("-", "");
                if ((m_CurrAssetBundleInfo.Size > 0 && (ulong)new FileInfo(m_DownloadLocalFilePath).Length != m_CurrAssetBundleInfo.Size) ||
                    !string.Equals(md5, m_CurrAssetBundleInfo.MD5, System.StringComparison.OrdinalIgnoreCase))
                {
                    GameEntry.LogError("下载文件校验失败=>{0}", m_CurrFileUrl);
                    return;
                }
                File.Move(m_DownloadLocalFilePath, m_DownloadLocalFilePath.Substring(0, m_DownloadLocalFilePath.Length - 5));
                m_DownloadLocalFilePath = null;

                if (PlayerPrefs.HasKey(m_CurrFileUrl))
                {
                    PlayerPrefs.DeleteKey(m_CurrFileUrl);
                }

                //写入本地版本文件
                GameEntry.Resource.ResourceManager.SaveVersion(m_CurrAssetBundleInfo);

                var completed = m_OnComplete;
                m_OnUpdate = null;
                m_OnComplete = null;
                completed?.Invoke(m_CurrFileUrl, this);
            }
        }

        /// <summary>
        /// 接收到数据后
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="leng"></param>
        private void DownloadHandlerReceiveDataCallBack(byte[] buffer, int leng)
        {
            if (m_UnityWebRequest.responseCode >= 400) return;
            // A server that ignores Range sends the entire file. Replace the partial prefix.
            if (m_BeginPos > 0 && m_UnityWebRequest.responseCode == 200)
            {
                m_FileStream.SetLength(0);
                m_FileStream.Position = 0;
                m_BeginPos = 0;
                m_TotalSize = 0;
            }
            Sava(buffer, false, leng);
        }

        /// <summary>
        /// 保存字节
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="downloadComplete"></param>
        private void Sava(byte[] buffer, bool downloadComplete = false, int cout = 0)
        {
            if (buffer == null)
            {
                return;
            }

            // DownloadHandlerScript reuses its buffer; only cout bytes belong to this chunk.
            m_FileStream?.Write(buffer, 0, cout);

            m_CurrWaitFlushSize += cout;
            if (m_CurrWaitFlushSize >= GameEntry.Download.FlushSize * 1024 || downloadComplete)
            {
                m_CurrWaitFlushSize = 0;
                m_FileStream.Flush();
            }
        }
    }
}
