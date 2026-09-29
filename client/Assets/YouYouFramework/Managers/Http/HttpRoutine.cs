using LitJson;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace YouYou
{
    public delegate void HttpSendDataCallBack(HttpCallBackArgs args);

    /// <summary>The original UnityWebRequest transport, with request ownership through completion/cancellation.</summary>
    public class HttpRoutine
    {
        private HttpSendDataCallBack m_CallBack;
        private readonly HttpCallBackArgs m_CallBackArgs;
        private Dictionary<string, object> m_Dic;
        private UnityWebRequest m_Request;
        private Coroutine m_Coroutine;
        private MonoBehaviour m_Host;
        private HttpManager m_Owner;
        public bool IsBusy { get; private set; }

        public HttpRoutine() { m_CallBackArgs = new HttpCallBackArgs(); }

        public void SendData(string url, HttpSendDataCallBack callBack, bool isPost = false, bool isGetData = false, Dictionary<string, object> dic = null)
        {
            if (IsBusy) return;
            IsBusy = true;
            m_CallBack = callBack;
            m_Dic = dic;
            m_Owner = GameEntry.Http;
            m_Host = GameEntry.Instance;
            m_Owner.Register(this);
            try
            {
                string json = string.Empty;
                if (isPost && m_Dic != null)
                {
                    // Preserve the course's form, device fields, timestamp and signature contract.
                    m_Dic["deviceIdentifier"] = DeviceUtil.DeviceIdentifier;
                    m_Dic["deviceModel"] = DeviceUtil.DeviceModel;
                    long t = GameEntry.Data.SysDataManager.CurrServerTime;
                    m_Dic["sign"] = EncryptUtil.Md5(string.Format("{0}:{1}", t, DeviceUtil.DeviceIdentifier));
                    m_Dic["t"] = t;
                    json = JsonMapper.ToJson(m_Dic);
                }
                m_Coroutine = m_Host.StartCoroutine(Request(url, isPost, isGetData, json));
            }
            catch
            {
                Release();
                throw;
            }
        }

        private IEnumerator Request(string url, bool isPost, bool isGetData, string json)
        {
            try
            {
                int retry = 0;
                while (IsBusy)
                {
                    if (isPost)
                    {
                        WWWForm form = new WWWForm();
                        form.AddField("json", json);
                        m_Request = UnityWebRequest.Post(url, form);
                    }
                    else m_Request = UnityWebRequest.Get(url);
                    yield return m_Request.SendWebRequest();
                    if (!IsBusy) yield break;
                    bool hasError = m_Request.result != UnityWebRequest.Result.Success;
                    if (hasError && retry < m_Owner.Retry)
                    {
                        retry++;
                        m_Request.Dispose();
                        m_Request = null;
                        yield return new WaitForSecondsRealtime(m_Owner.RetryInterval);
                        continue;
                    }
                    m_CallBackArgs.HasError = hasError;
                    m_CallBackArgs.Value = hasError ? m_Request.error : m_Request.downloadHandler.text;
                    m_CallBackArgs.Data = hasError ? null : m_Request.downloadHandler.data;
#if DEBUG_LOG_PROTO && DEBUG_MODEL
                    if (!isGetData)
                    {
                        GameEntry.Log(LogCategory.Proto, "HTTP {0}: {1}", url, JsonUtility.ToJson(m_CallBackArgs));
                    }
#endif
                    m_CallBack?.Invoke(m_CallBackArgs);
                    yield break;
                }
            }
            finally { Release(); }
        }

        internal void Cancel()
        {
            if (!IsBusy) return;
            Coroutine coroutine = m_Coroutine;
            m_Coroutine = null;
            if (m_Host && coroutine != null) m_Host.StopCoroutine(coroutine);
            Release();
        }

        private void Release()
        {
            if (!IsBusy) return;
            IsBusy = false;
            if (m_Request != null)
            {
                if (!m_Request.isDone) m_Request.Abort();
                m_Request.Dispose();
                m_Request = null;
            }
            m_CallBack = null;
            m_CallBackArgs.Data = null;
            m_CallBackArgs.Value = null;
            m_Host = null;
            m_Coroutine = null;
            m_Owner?.Unregister(this);
            m_Owner = null;
            if (m_Dic != null)
            {
                // The original API transfers its pooled parameter dictionary to the request.
                m_Dic.Clear();
                GameEntry.Pool?.EnqueueClassObject(m_Dic);
                m_Dic = null;
            }
            GameEntry.Pool?.EnqueueClassObject(this);
        }
    }
}
