using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Google.Protobuf;
using UnityEngine;
using YouYou;
using YouYou.Proto;

/// <summary>End-to-end tests of the original managers against listeners owned by this fixture.</summary>
public static class YouYouNetworkValidation
{
    private const ushort RawProtocol = 65001;

    public static IEnumerator Run(Action<bool, string> check)
    {
        using (var http = new HttpFixture())
        using (var tcp = new TcpFixture())
        using (var configuration = new ConfigurationLease(http.Url))
        {
            SocketTcpRoutine routine = null;
            SocketEvent.OnActionHandler heartbeatHandler = null, rawHandler = null;
            var ownedPaths = new List<string>();
            var ownedPrefs = new List<string>();
            try
            {
                check(new Uri(GameEntry.Data.SysDataManager.CurrChannelConfig.RealSourceUrl).IsLoopback,
                    "Network fixture uses only loopback ChannelConfig and listeners");

                bool done = false, failed = false;
                string text = null;
                byte[] data = null;
                GameEntry.Http.SendData(http.Url + "/get", args =>
                { failed = args.HasError; text = args.Value; data = args.Data; done = true; }, isGetData: true);
                yield return Until(() => done, http, 10, "original HTTP GET");
                check(!failed && text == HttpFixture.GetPayload && Encoding.UTF8.GetString(data) == text,
                    "Original HttpManager GET returns actual response text and bytes");

                done = false;
                var parameters = GameEntry.Pool.DequeueClassObject<Dictionary<string, object>>();
                parameters.Clear(); parameters["probe"] = "original-form"; parameters["number"] = 73;
                GameEntry.Http.SendData(http.Url + "/post-retry", args =>
                { failed = args.HasError; text = args.Value; done = true; }, isPost: true, dic: parameters);
                yield return Until(() => done, http, 25, "original signed HTTP POST retry");
                check(!failed && http.PostCount == 2 && http.FirstPost == http.LastPost && text == http.LastPost,
                    "Original HTTP POST retries one local 503 and retains the same form payload");
                var posted = LitJson.JsonMapper.ToObject(text);
                string expectedSign = EncryptUtil.Md5(posted["t"].ToString() + ":" + posted["deviceIdentifier"]);
                check((string)posted["probe"] == "original-form" && (int)posted["number"] == 73 &&
                    (string)posted["sign"] == expectedSign && posted["deviceModel"] != null,
                    "Original POST includes device fields, timestamp and original MD5 signature");

                routine = GameEntry.Socket.CreateSocketTcpRoutine();
                bool connected = false, connectionCompleted = false;
                int heartbeatCount = 0, rawCount = 0;
                var sent = new C2GWS_Heartbeat { Time = 638947001234567890L, Ping = 37 };
                var largePayload = MakePayload(8193, 491);
                C2GWS_Heartbeat received = null;
                byte[] receivedRaw = null;
                heartbeatHandler = bytes => { received = C2GWS_Heartbeat.Parser.ParseFrom(bytes); heartbeatCount++; };
                rawHandler = bytes => { receivedRaw = bytes; rawCount++; };
                GameEntry.Event.SocketEvent.AddEventListener(sent.ProtoId, heartbeatHandler);
                GameEntry.Event.SocketEvent.AddEventListener(RawProtocol, rawHandler);
                routine.Connect("127.0.0.1", tcp.Port, success => { connected = success; connectionCompleted = true; });
                yield return Until(() => connectionCompleted, tcp, 10, "original TCP connect callback");
                check(connected, "Original SocketManager-created TCP routine connects to a real loopback socket");
                routine.SendMsg(sent);
                routine.SendMsg(RawProtocol, (byte)sent.Category, largePayload);
                yield return Until(() => heartbeatCount == 1 && rawCount == 1 && tcp.FrameCount == 2, tcp, 12,
                    "original framed protobuf and compressed raw socket round trips");
                check(received.Time == sent.Time && received.Ping == sent.Ping && tcp.Heartbeat.Time == sent.Time &&
                    tcp.Heartbeat.Ping == sent.Ping && tcp.ProtoId == sent.ProtoId,
                    "Original IProto serialization, encrypted framing, event dispatch and protobuf parsing round-trip");
                check(Equal(largePayload, tcp.RawPayload) && Equal(largePayload, receivedRaw) && tcp.CompressedRaw,
                    "Original raw C# socket API round-trips GZip and XXTEA across fragmented TCP packets");
                routine.DisConnect();
                check(!routine.IsConnected, "Original TCP disconnect clears live connection state");
                GameEntry.Event.SocketEvent.RemoveEventListener(sent.ProtoId, heartbeatHandler); heartbeatHandler = null;
                GameEntry.Event.SocketEvent.RemoveEventListener(RawProtocol, rawHandler); rawHandler = null;
                GameEntry.Pool.EnqueueClassObject(routine); routine = null;
                yield return null; // Allow canceled async receive callbacks to drain before teardown.

                string run = "youyou-validation/network-" + Guid.NewGuid().ToString("N") + "/";
                var files = new[]
                {
                    new DownloadCase(run + "fresh.bytes", MakePayload(131089, 37), 0, false),
                    new DownloadCase(run + "resume.bytes", MakePayload(786469, 51), 131103, true),
                    new DownloadCase(run + "range-ignored.bytes", MakePayload(262181, 19), 32773, false, true)
                };
                GameEntry.Resource.ResourceManager.InitializeLocalManifest(CreateManifest(files));
                foreach (var file in files)
                {
                    string path = Path.GetFullPath(Path.Combine(GameEntry.Resource.LocalFilePath, file.Name));
                    string safeRoot = Path.GetFullPath(Path.Combine(GameEntry.Resource.LocalFilePath, "youyou-validation")) + Path.DirectorySeparatorChar;
                    if (!path.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture path.");
                    ownedPaths.Add(path); ownedPaths.Add(path + ".temp"); ownedPrefs.Add(file.Name);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    http.Add(file);
                    if (file.Prefix > 0)
                    {
                        using (var partial = File.Create(path + ".temp")) partial.Write(file.Bytes, 0, file.Prefix);
                        PlayerPrefs.SetString(file.Name, file.Hash);
                    }
                    done = false;
                    ulong downloaded = 0;
                    float progress = 0;
                    GameEntry.Download.BeginDownloadSingle(file.Name, (url, count, fraction) =>
                    { downloaded = count; progress = fraction; }, url => done = url == file.Name);
                    yield return Until(() => done, http, 30, "original download " + file.Name);
                    check(File.Exists(path) && Equal(File.ReadAllBytes(path), file.Bytes) && Hash(File.ReadAllBytes(path)) == file.Hash,
                        "Original DownloadManager writes matching bytes and MD5: " + Path.GetFileName(file.Name));
                    check(downloaded == (ulong)file.Bytes.Length && Mathf.Abs(progress - 1f) < .0001f &&
                        !File.Exists(path + ".temp") && !PlayerPrefs.HasKey(file.Name),
                        "Original download completes full progress and clears resume state: " + Path.GetFileName(file.Name));
                    if (file.Prefix > 0)
                        check(http.LastRange == "bytes=" + file.Prefix + "-" && http.LastRangeStart == file.Prefix,
                            "Original downloader sends the precise Range prefix: " + Path.GetFileName(file.Name));
                    if (file.FailFirst)
                    {
                        int attempts;
                        lock (http.Attempts) attempts = http.Attempts[file.Name];
                        check(attempts == 2, "Original resumed download recovers from one real HTTP 503");
                    }
                    string version = File.ReadAllText(GameEntry.Resource.ResourceManager.LocalAssetsManager.LocalVersionFilePath);
                    var savedVersions = LitJson.JsonMapper.ToObject<Dictionary<string, AssetBundleInfoEntity>>(version);
                    check(savedVersions.TryGetValue(file.Name, out var saved) && saved.MD5 == file.Hash,
                        "Original successful download persists its original resource version entry: " + Path.GetFileName(file.Name));
                }
            }
            finally
            {
                // These managers belong to the validation GameEntry. Cancel its requests before stopping its servers.
                GameEntry.Download?.Dispose();
                GameEntry.Http?.Dispose();
                if (heartbeatHandler != null) GameEntry.Event?.SocketEvent.RemoveEventListener(ProtoIdDefine.Proto_C2GWS_Heartbeat, heartbeatHandler);
                if (rawHandler != null) GameEntry.Event?.SocketEvent.RemoveEventListener(RawProtocol, rawHandler);
                if (routine != null) { routine.DisConnect(); GameEntry.Pool?.EnqueueClassObject(routine); }
                foreach (string key in ownedPrefs) PlayerPrefs.DeleteKey(key);
                foreach (string path in ownedPaths) if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    private static IEnumerator Until(Func<bool> condition, LoopbackFixture fixture, float timeout, string operation)
    {
        float deadline = Time.realtimeSinceStartup + timeout;
        while (!condition())
        {
            if (fixture.Error != null) throw new InvalidOperationException(operation, fixture.Error);
            if (Time.realtimeSinceStartup > deadline) throw new TimeoutException(operation);
            yield return null;
        }
        if (fixture.Error != null) throw new InvalidOperationException(operation, fixture.Error);
    }

    private static byte[] MakePayload(int length, int seed) { var value = new byte[length]; new System.Random(seed).NextBytes(value); return value; }
    private static bool Equal(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
    private static string Hash(byte[] bytes)
    { using (var md5 = MD5.Create()) return BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", ""); }
    private static byte[] CreateManifest(DownloadCase[] files)
    {
        using (var stream = new MMO_MemoryStream())
        {
            stream.WriteInt(files.Length + 1); stream.WriteUTF8String("loopback-validation");
            foreach (var file in files)
            {
                stream.WriteUTF8String(file.Name); stream.WriteUTF8String(file.Hash);
                stream.WriteULong((ulong)file.Bytes.Length); stream.WriteByte(0); stream.WriteByte(0);
            }
            return ZlibHelper.CompressBytes(stream.ToArray());
        }
    }

    private sealed class ConfigurationLease : IDisposable
    {
        private readonly ChannelConfigEntity channel;
        private readonly string source, version, cache;
        private readonly FieldInfo cachedUrl;
        private readonly string versionPath;
        private readonly string localPath;
        private readonly byte[] versionBytes;
        public ConfigurationLease(string url)
        {
            channel = GameEntry.Data.SysDataManager.CurrChannelConfig;
            cachedUrl = typeof(ChannelConfigEntity).GetField("m_RealSourceUrl", BindingFlags.Instance | BindingFlags.NonPublic);
            if (cachedUrl == null) throw new MissingFieldException("ChannelConfigEntity.m_RealSourceUrl");
            source = channel.SourceUrl; version = channel.SourceVersion; cache = (string)cachedUrl.GetValue(channel);
            versionPath = GameEntry.Resource.ResourceManager.LocalAssetsManager.LocalVersionFilePath;
            versionBytes = File.Exists(versionPath) ? File.ReadAllBytes(versionPath) : null;
            localPath = GameEntry.Resource.LocalFilePath;
            GameEntry.Resource.LocalFilePath = Application.persistentDataPath;
            channel.SourceUrl = url; channel.SourceVersion = "fixture";
            cachedUrl.SetValue(channel, null); // Original property caches SourceUrl; restore that cache exactly on exit.
        }
        public void Dispose()
        {
            channel.SourceUrl = source; channel.SourceVersion = version; cachedUrl.SetValue(channel, cache);
            if (GameEntry.Resource != null) GameEntry.Resource.LocalFilePath = localPath;
            if (versionBytes != null) File.WriteAllBytes(versionPath, versionBytes);
            else if (File.Exists(versionPath)) File.Delete(versionPath);
        }
    }

    private sealed class DownloadCase
    {
        public readonly string Name, Hash;
        public readonly byte[] Bytes;
        public readonly int Prefix;
        public readonly bool FailFirst, IgnoreRange;
        public DownloadCase(string name, byte[] bytes, int prefix, bool failFirst, bool ignoreRange = false)
        { Name = name; Bytes = bytes; Hash = YouYouNetworkValidation.Hash(bytes); Prefix = prefix; FailFirst = failFirst; IgnoreRange = ignoreRange; }
    }

    private abstract class LoopbackFixture : IDisposable
    {
        protected readonly TcpListener Listener;
        private readonly Thread worker;
        private readonly object clientLock = new object();
        private TcpClient currentClient;
        protected volatile bool Stopping;
        public volatile Exception Error;
        public int Port { get; private set; }
        protected LoopbackFixture()
        {
            Listener = new TcpListener(IPAddress.Loopback, 0); Listener.Start();
            Port = ((IPEndPoint)Listener.LocalEndpoint).Port;
            worker = new Thread(Serve) { IsBackground = true, Name = "YouYou loopback validation" };
            // Derived fields are populated before Start() is called by the derived constructor.
        }
        protected void Start() { worker.Start(); }
        private void Serve()
        {
            try
            {
                while (!Stopping)
                {
                    var client = Listener.AcceptTcpClient();
                    lock (clientLock)
                    {
                        if (Stopping) { client.Close(); return; }
                        currentClient = client;
                    }
                    using (client)
                    {
                        client.ReceiveTimeout = 10000; client.SendTimeout = 10000; client.NoDelay = true;
                        Handle(client.GetStream());
                    }
                    lock (clientLock) currentClient = null;
                }
            }
            catch (Exception exception) { if (!Stopping) Error = exception; }
        }
        protected abstract void Handle(NetworkStream stream);
        public void Dispose()
        {
            Stopping = true; Listener.Stop();
            lock (clientLock) { currentClient?.Close(); currentClient = null; }
            worker.Join(1000);
        }
        protected static byte[] ReadExact(Stream stream, int count)
        {
            var bytes = new byte[count]; int offset = 0;
            while (offset < count)
            { int read = stream.Read(bytes, offset, count - offset); if (read <= 0) throw new EndOfStreamException(); offset += read; }
            return bytes;
        }
    }

    private sealed class HttpFixture : LoopbackFixture
    {
        public const string GetPayload = "original-youyou-loopback";
        public string Url { get { return "http://127.0.0.1:" + Port; } }
        public volatile int PostCount;
        public string FirstPost, LastPost, LastRange;
        public int LastRangeStart;
        private readonly Dictionary<string, DownloadCase> downloads = new Dictionary<string, DownloadCase>();
        public readonly Dictionary<string, int> Attempts = new Dictionary<string, int>();
        public HttpFixture() { Start(); }
        public void Add(DownloadCase file) { lock (downloads) downloads.Add(file.Name, file); }
        protected override void Handle(NetworkStream stream)
        {
            var header = new List<byte>();
            while (header.Count < 65536)
            {
                int value = stream.ReadByte(); if (value < 0) throw new EndOfStreamException(); header.Add((byte)value);
                int n = header.Count;
                if (n >= 4 && header[n - 4] == 13 && header[n - 3] == 10 && header[n - 2] == 13 && header[n - 1] == 10) break;
            }
            string[] lines = Encoding.ASCII.GetString(header.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] request = lines[0].Split(' ');
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            { int split = lines[i].IndexOf(':'); if (split > 0) headers[lines[i].Substring(0, split)] = lines[i].Substring(split + 1).Trim(); }
            if (headers.TryGetValue("Expect", out var expect) && expect.IndexOf("100-continue", StringComparison.OrdinalIgnoreCase) >= 0)
            { var interim = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"); stream.Write(interim, 0, interim.Length); }
            int length = headers.TryGetValue("Content-Length", out var lengthText) ? int.Parse(lengthText) : 0;
            string body = Encoding.UTF8.GetString(ReadExact(stream, length));
            if (request[0] == "GET" && request[1] == "/get")
            { Respond(stream, 200, Encoding.UTF8.GetBytes(GetPayload)); return; }
            if (request[0] == "POST" && request[1] == "/post-retry")
            {
                string json = null;
                foreach (string item in body.Split('&'))
                    if (item.StartsWith("json=", StringComparison.Ordinal)) json = WebUtility.UrlDecode(item.Substring(5));
                if (json == null) throw new InvalidDataException("Original POST did not send its json form field.");
                LastPost = json; if (PostCount == 0) FirstPost = json;
                PostCount++;
                Respond(stream, PostCount == 1 ? 503 : 200, PostCount == 1 ? new byte[0] : Encoding.UTF8.GetBytes(json)); return;
            }
            DownloadCase download = null;
            lock (downloads)
                foreach (var candidate in downloads.Values)
                    if (request[1].EndsWith("/" + candidate.Name, StringComparison.Ordinal)) { download = candidate; break; }
            if (download == null) { Respond(stream, 404, new byte[0]); return; }
            int attempts;
            lock (Attempts) { Attempts.TryGetValue(download.Name, out attempts); Attempts[download.Name] = ++attempts; }
            int prefix = 0;
            if (headers.TryGetValue("Range", out var range))
            { LastRange = range; prefix = int.Parse(range.Substring(6).TrimEnd('-')); LastRangeStart = prefix; }
            if (download.FailFirst && attempts == 1) { Respond(stream, 503, new byte[0]); return; }
            if (download.IgnoreRange) prefix = 0;
            Respond(stream, prefix > 0 ? 206 : 200, download.Bytes, prefix);
        }
        private static void Respond(NetworkStream stream, int status, byte[] bytes, int prefix = 0)
        {
            string header = "HTTP/1.1 " + status + " " + (status == 200 ? "OK" : status == 206 ? "Partial Content" : "Fixture Response") +
                "\r\nContent-Type: application/octet-stream\r\nConnection: close\r\nContent-Length: " + (bytes.Length - prefix) + "\r\n";
            if (status == 206) header += "Content-Range: bytes " + prefix + "-" + (bytes.Length - 1) + "/" + bytes.Length + "\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header + "\r\n"); stream.Write(headerBytes, 0, headerBytes.Length);
            for (int position = prefix; position < bytes.Length; position += 16384)
            { int count = Math.Min(16384, bytes.Length - position); stream.Write(bytes, position, count); Thread.Sleep(1); }
        }
    }

    private sealed class TcpFixture : LoopbackFixture
    {
        public volatile int FrameCount;
        public ushort ProtoId;
        public C2GWS_Heartbeat Heartbeat;
        public byte[] RawPayload;
        public bool CompressedRaw;
        public TcpFixture() { Start(); }
        protected override void Handle(NetworkStream stream)
        {
            var frames = new List<byte[]>();
            for (int i = 0; i < 2; i++)
            {
                byte[] length = ReadExact(stream, 2);
                byte[] body = ReadExact(stream, BitConverter.ToUInt16(length, 0));
                if (body.Length < 4 || (body[0] & 2) == 0) throw new InvalidDataException("Original socket encryption flag missing.");
                ushort id = BitConverter.ToUInt16(body, 1);
                var encrypted = new byte[body.Length - 4]; Buffer.BlockCopy(body, 4, encrypted, 0, encrypted.Length);
                byte[] payload = XXTEAUtil.Decrypt(encrypted);
                if ((body[0] & 1) != 0) payload = GZipUtil.Decompress(payload);
                if (i == 0) { ProtoId = id; Heartbeat = C2GWS_Heartbeat.Parser.ParseFrom(payload); }
                else { if (id != RawProtocol) throw new InvalidDataException("Unexpected raw protocol ID."); RawPayload = payload; CompressedRaw = (body[0] & 1) != 0; }
                var frame = new byte[length.Length + body.Length]; Buffer.BlockCopy(length, 0, frame, 0, 2); Buffer.BlockCopy(body, 0, frame, 2, body.Length);
                frames.Add(frame); FrameCount++;
            }
            // Split the first ushort, then leave one byte of the second header after a complete first packet.
            stream.Write(frames[0], 0, 1); Thread.Sleep(20);
            var middle = new byte[frames[0].Length]; Buffer.BlockCopy(frames[0], 1, middle, 0, frames[0].Length - 1); middle[middle.Length - 1] = frames[1][0];
            stream.Write(middle, 0, middle.Length); Thread.Sleep(20);
            for (int offset = 1; offset < frames[1].Length; offset += 257)
            { stream.Write(frames[1], offset, Math.Min(257, frames[1].Length - offset)); Thread.Sleep(1); }
            // Keep the peer alive while the main thread dispatches the received messages.
            while (!Stopping) { if (stream.DataAvailable) stream.ReadByte(); Thread.Sleep(10); }
        }
    }
}
