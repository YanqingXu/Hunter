using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace YouYou.Framework
{
    /// <summary>One connection per instance. Pump on the main thread to deliver messages.</summary>
    public sealed class TcpChannel : IDisposable
    {
        private readonly TcpClient client = new TcpClient();
        private readonly IFrameCodec codec;
        private readonly object codecGate = new object();
        private readonly SemaphoreSlim sendGate = new SemaphoreSlim(1, 1);
        private readonly ConcurrentQueue<byte[]> incoming = new ConcurrentQueue<byte[]>();
        private readonly int maxPendingMessages;
        private NetworkStream stream;
        private Task receiveTask;
        private int started, closed, pending;
        private bool notified;
        private Exception disconnectError;
        public event Action<byte[]> MessageReceived;
        public event Action<Exception> Disconnected;

        public TcpChannel(IFrameCodec codec = null, int maxPendingMessages = 1024)
        {
            if (maxPendingMessages <= 0) throw new ArgumentOutOfRangeException(nameof(maxPendingMessages));
            this.codec = codec ?? new LengthPrefixCodec();
            this.maxPendingMessages = maxPendingMessages;
        }

        public async Task ConnectAsync(string host, int port, int timeoutMilliseconds = 10000)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host is required.", nameof(host));
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            if (timeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            if (Interlocked.Exchange(ref started, 1) != 0 || Volatile.Read(ref closed) != 0)
                throw new InvalidOperationException("Create a new channel for each connection.");
            try
            {
                var connect = client.ConnectAsync(host, port);
                if (await Task.WhenAny(connect, Task.Delay(timeoutMilliseconds)).ConfigureAwait(false) != connect)
                {
                    client.Close();
                    _ = connect.ContinueWith(t => { var observed = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    throw new TimeoutException("TCP connection timed out.");
                }
                await connect.ConfigureAwait(false);
                if (Volatile.Read(ref closed) != 0) throw new ObjectDisposedException(nameof(TcpChannel));
                stream = client.GetStream();
                receiveTask = ReceiveAsync();
            }
            catch (Exception e) { Close(e); throw; }
        }

        public async Task SendAsync(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            await sendGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (stream == null || Volatile.Read(ref closed) != 0) throw new InvalidOperationException("Channel is not connected.");
                byte[] bytes;
                lock (codecGate) bytes = codec.Encode(payload);
                await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }
            catch (IOException e) { Close(e); throw; }
            finally { sendGate.Release(); }
        }

        private async Task ReceiveAsync()
        {
            var buffer = new byte[8192];
            var messages = new List<byte[]>();
            try
            {
                while (Volatile.Read(ref closed) == 0)
                {
                    int count = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (count == 0) break;
                    messages.Clear();
                    lock (codecGate) codec.Feed(buffer, 0, count, messages);
                    foreach (var message in messages)
                    {
                        if (Interlocked.Increment(ref pending) > maxPendingMessages)
                            throw new IOException("Incoming message queue is full; call Pump regularly.");
                        incoming.Enqueue(message);
                    }
                }
                Close(null);
            }
            catch (Exception e) { Close(e); }
        }

        public void Pump(int maxMessages = 64)
        {
            if (maxMessages < 1) throw new ArgumentOutOfRangeException(nameof(maxMessages));
            for (int i = 0; i < maxMessages && incoming.TryDequeue(out var message); i++)
            {
                Interlocked.Decrement(ref pending);
                MessageReceived?.Invoke(message);
            }
            if (Volatile.Read(ref closed) != 0 && (receiveTask == null || receiveTask.IsCompleted) && incoming.IsEmpty && !notified)
            {
                notified = true;
                Disconnected?.Invoke(disconnectError);
            }
        }

        private void Close(Exception error)
        {
            if (Interlocked.CompareExchange(ref closed, 1, 0) != 0) return;
            disconnectError = error;
            client.Close();
        }

        public void Dispose()
        {
            Close(null);
            MessageReceived = null; Disconnected = null;
            while (incoming.TryDequeue(out _)) Interlocked.Decrement(ref pending);
        }
    }
}
