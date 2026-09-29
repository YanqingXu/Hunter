using System;

namespace YouYou.Framework
{
    public sealed class EventManager : IDisposable
    {
        public CommonEvent CommonEvent { get; } = new CommonEvent();
        public SocketEvent SocketEvent { get; } = new SocketEvent();
        public void Dispose() { CommonEvent.Dispose(); SocketEvent.Dispose(); }
    }
}
