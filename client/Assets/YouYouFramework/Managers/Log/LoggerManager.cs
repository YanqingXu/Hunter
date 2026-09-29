using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace YouYou
{
    /// <summary>
    /// 日志管理器
    /// </summary>
    public class LoggerManager : ManagerBase, IDisposable
    {
        private List<string> m_LogArray;
        private readonly object m_LogLock = new object();
        private string m_LogDirectory;
        private string m_SessionName;
        private int m_LogPart;
        private bool m_Initialized;
        private bool m_Disposed;

        /// <summary>当前日志文件路径；仅用于定位当前实例自己的日志。</summary>
        public string LogPath { get { lock (m_LogLock) return m_LogPath; } }

        /// <summary>
        /// 记录日志的路径
        /// </summary>
        private string m_LogPath = null;

        /// <summary>
        /// 单个日志文件最大日志数量
        /// </summary>
        private int m_LogMaxCapacity = 500;

        /// <summary>
        /// 当前日志数量
        /// </summary>
        private int m_CurrLogCount = 0;

        /// <summary>
        /// 日志缓存的数量
        /// </summary>
        private int m_LogBufferMaxNumber = 10;

        public override void Init()
        {
            lock (m_LogLock)
            {
                if (m_Initialized) return;
                // 只在入口主线程获取 Unity 路径；Reporter 的其他线程仅使用已缓存的路径。
                m_LogDirectory = Path.GetFullPath(Application.persistentDataPath);
                Directory.CreateDirectory(m_LogDirectory);
                m_SessionName = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss-fff") + "-" + Guid.NewGuid().ToString("N");
                m_LogPath = Path.Combine(m_LogDirectory, m_SessionName + "-Start.txt");
                m_LogArray = new List<string>(m_LogBufferMaxNumber);
                m_CurrLogCount = 0;
                m_LogPart = 0;
                m_Disposed = false;
                m_Initialized = true;
            }
        }

        public void Write(string writeFileData, LogType type)
        {
            if (string.IsNullOrEmpty(writeFileData)) return;
            lock (m_LogLock)
            {
                // 关闭后的迟到 Reporter 回调不能重新打开日志或递归产生错误。
                if (m_Disposed) return;
                if (!m_Initialized) throw new InvalidOperationException("LoggerManager 必须先在入口主线程 Init。");
                if (m_CurrLogCount >= m_LogMaxCapacity)
                {
                    SyncLog();
                    m_LogPart++;
                    m_LogPath = Path.Combine(m_LogDirectory, m_SessionName + "-" + m_LogPart.ToString("D4") + ".txt");
                    m_CurrLogCount = 0;
                }
                m_CurrLogCount++;
                AppendDataToFile(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:fff") + "|" + type + "|" + writeFileData);
            }
        }

        #region AppendDataToFile
        private void AppendDataToFile(string writeFileDate)
        {
            lock (m_LogLock)
            {
                if (string.IsNullOrEmpty(writeFileDate)) return;
                m_LogArray.Add(writeFileDate);
                if (m_LogArray.Count >= m_LogBufferMaxNumber) SyncLog();
            }
        }
        #endregion

        #region CreateFile
        private void CreateFile(string pathAndName, string info)
        {
            using (var stream = new FileStream(pathAndName, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.WriteLine(info);
            }
        }
        #endregion

        #region ClearLogArray
        private void ClearLogArray()
        {
            lock (m_LogLock) m_LogArray?.Clear();
        }
        #endregion

        #region SyncLog
        public void SyncLog()
        {
            lock (m_LogLock)
            {
                if (!m_Initialized || m_Disposed || m_LogArray.Count == 0) return;
                // 写入成功后才清缓冲，I/O 失败时保留记录，调用方可重试 SyncLog。
                CreateFile(m_LogPath, string.Join(Environment.NewLine, m_LogArray));
                ClearLogArray();
            }
        }
        #endregion

        public void Dispose()
        {
            lock (m_LogLock)
            {
                if (m_Disposed) return;
                SyncLog();
                ClearLogArray();
                m_LogArray = null;
                m_Initialized = false;
                m_Disposed = true;
            }
        }
    }
}
