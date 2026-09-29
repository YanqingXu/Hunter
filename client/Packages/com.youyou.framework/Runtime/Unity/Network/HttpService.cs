using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Networking;

namespace YouYou.Framework
{
    public sealed class HttpResponse
    {
        public long StatusCode { get; internal set; }
        public string Error { get; internal set; }
        public byte[] Data { get; internal set; }
        public string Text => Data == null ? "" : Encoding.UTF8.GetString(Data);
        public bool Success => string.IsNullOrEmpty(Error) && StatusCode >= 200 && StatusCode < 300;
    }

    /// <summary>Start returned enumerators on the main thread. No account URL, signing, or implicit POST retries.</summary>
    public sealed class HttpService : IDisposable
    {
        private readonly HashSet<UnityWebRequest> active = new HashSet<UnityWebRequest>();
        private bool disposed;

        public IEnumerator Get(string url, Action<HttpResponse> completed, int timeoutSeconds = 15,
            IDictionary<string, string> headers = null)
        {
            return Send(url, "GET", null, completed, timeoutSeconds, headers);
        }

        public IEnumerator PostJson(string url, string json, Action<HttpResponse> completed, int timeoutSeconds = 15,
            IDictionary<string, string> headers = null)
        {
            return Send(url, "POST", Encoding.UTF8.GetBytes(json ?? "{}"), completed, timeoutSeconds, headers);
        }

        private IEnumerator Send(string url, string method, byte[] body, Action<HttpResponse> completed,
            int timeoutSeconds, IDictionary<string, string> headers)
        {
            if (disposed) throw new ObjectDisposedException(nameof(HttpService));
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                throw new ArgumentException("An absolute HTTP(S) URL is required.", nameof(url));
            if (timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            HttpResponse response;
            using (var request = new UnityWebRequest(url, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = timeoutSeconds;
                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(body);
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                if (headers != null)
                    foreach (var header in headers) request.SetRequestHeader(header.Key, header.Value);
                active.Add(request);
                try
                {
                    yield return request.SendWebRequest();
                    if (disposed) yield break;
                    response = new HttpResponse { StatusCode = request.responseCode, Error = request.error, Data = request.downloadHandler.data };
                }
                finally { active.Remove(request); }
            }
            completed?.Invoke(response);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var request in active) { request.Abort(); request.Dispose(); }
            active.Clear();
        }
    }
}
