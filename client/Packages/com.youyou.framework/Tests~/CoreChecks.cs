using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YouYou.Framework.Validation
{
    public static class CoreChecks
    {
        private static int passed;
        public static string Run()
        {
            passed = 0;
            Check("event mutation and deduplication", EventMutation);
            Check("nested event dispatch", NestedEvent);
            Check("socket events", SocketEvents);
            Check("timer cancellation during traversal", TimerCancellation);
            Check("timer pause, repeat and completion", TimerLifecycle);
            Check("static timer callback", StaticTimer);
            Check("FSM invalid transition preserves state", InvalidFsm);
            Check("FSM ownership and shutdown", FsmLifecycle);
            Check("procedures supplied by project", Procedures);
            Check("pool identity, reset and capacity", Pool);
            Check("independent contexts", Contexts);
            Check("localization uses project data", Localization);
            Check("binary roundtrip and little endian", BinaryRoundTrip);
            Check("truncated binary rejected", BinaryTruncation);
            Check("gzip roundtrip", Compression);
            Check("fragmented and coalesced TCP frames", Frames);
            Check("oversized TCP frames rejected", FrameLimit);
            Check("TCP loopback and main-thread delivery", () => TcpLoopback().GetAwaiter().GetResult());
            return passed + " core checks passed.";
        }
        private static void Check(string name, Action test)
        {
            try { test(); passed++; }
            catch (Exception e) { throw new Exception("Failed: " + name, e); }
        }
        public static void Assert(bool condition, string message = "Assertion failed")
        { if (!condition) throw new Exception(message); }
        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); } catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }
        private static void EventMutation()
        {
            var bus = new CommonEvent(); int calls = 0;
            CommonEvent.OnActionHandler second = _ => calls++;
            CommonEvent.OnActionHandler first = null;
            first = _ => { bus.RemoveEventListener(1, first); bus.RemoveEventListener(1, second); calls++; };
            bus.AddEventListener(1, first); bus.AddEventListener(1, second); bus.AddEventListener(1, second);
            bus.Dispatch(1); bus.Dispatch(1); Assert(calls == 2);
        }
        private static void NestedEvent()
        {
            var bus = new CommonEvent(); int calls = 0;
            bus.AddEventListener(1, _ => { calls++; if (calls == 1) bus.Dispatch(1); });
            bus.Dispatch(1); Assert(calls == 2); bus.Dispose(); bus.Dispatch(1); Assert(calls == 2);
        }
        private static void SocketEvents()
        {
            var bus = new SocketEvent(); byte value = 0;
            SocketEvent.OnActionHandler handler = data => value = data[0];
            bus.AddEventListener(4, handler); bus.Dispatch(4, new byte[] { 9 });
            bus.RemoveEventListener(4, handler); bus.Dispatch(4, new byte[] { 1 }); Assert(value == 9);
        }
        private static void TimerCancellation()
        {
            var timers = new TimeManager(); int calls = 0; TimeAction second = null;
            timers.CreateTimeAction().Init(onUpdate: _ => { calls++; second.Stop(); }).Run();
            second = timers.CreateTimeAction().Init(onUpdate: _ => calls += 100); second.Run();
            timers.CreateTimeAction().Init(onUpdate: _ => calls++).Run();
            timers.Tick(0); Assert(calls == 2); timers.Tick(1); Assert(calls == 2);
        }
        private static void TimerLifecycle()
        {
            var timers = new TimeManager(); int calls = 0, complete = 0, starts = 0;
            var timer = timers.CreateTimeAction().Init(delayTime: 1, interval: 1, loop: 2,
                onStar: () => starts++, onUpdate: _ => calls++, onComplete: () => complete++);
            timer.Run(); timer.Run(); timers.Tick(.5f); timer.Pause(); timers.Tick(10);
            timer.Resume(); timers.Tick(.5f); timers.Tick(1); timers.Tick(1);
            Assert(starts == 1 && calls == 2 && complete == 1 && !timer.IsRuning);
            timer.Run(); timer.Stop(); Assert(complete == 1);
            timer.Run(); timers.Tick(1); Assert(calls == 3); timers.Dispose();
        }
        private static int staticCalls;
        private static void StaticCallback(int _) { staticCalls++; }
        private static void StaticTimer()
        {
            staticCalls = 0; var timers = new TimeManager();
            timers.CreateTimeAction().Init(onUpdate: StaticCallback).Run(); timers.Tick(0); Assert(staticCalls == 1);
        }
        private sealed class State : FsmState<object>
        {
            public int Enters, Leaves, Destroyed, Updates;
            public override void OnEnter() { Enters++; }
            public override void OnLeave() { Leaves++; }
            public override void OnDestroy() { Destroyed++; }
            public override void OnUpdate() { Updates++; }
        }
        private static void InvalidFsm()
        {
            var state = new State(); var fsm = new Fsm<object>(1, new object(), new FsmState<object>[] { state });
            fsm.ChangeState(0); Throws<ArgumentOutOfRangeException>(() => fsm.ChangeState(1));
            Assert(fsm.CurrStateType == 0 && state.Leaves == 0); fsm.ShutDown();
        }
        private static void FsmLifecycle()
        {
            var manager = new FsmManager(); var state = new State();
            var fsm = manager.Create(7, new object(), new FsmState<object>[] { state });
            Throws<ArgumentException>(() => manager.Create(7, new object(), new FsmState<object>[] { new State() }));
            Throws<ArgumentException>(() => manager.Create(new object(), new FsmState<object>[] { state }));
            fsm.ChangeState(0); manager.Tick(); manager.DestroyFsm(7); fsm.ShutDown();
            Assert(state.Enters == 1 && state.Leaves == 1 && state.Destroyed == 1 && state.Updates == 1 && state.CurrFsm == null);
        }
        private sealed class Procedure : ProcedureBase { public int Enters; public override void OnEnter() { Enters++; } }
        private static void Procedures()
        {
            using (var context = new ClientContext())
            {
                var first = new Procedure(); var second = new Procedure();
                context.Procedure.Start(new ProcedureBase[] { first, second }); context.Procedure.ChangeState(1);
                Assert(first.Enters == 1 && second.Enters == 1);
            }
        }
        public sealed class Pooled : IRecyclable
        {
            public int Value;
            public void Clear() { Value = 0; }
            public override bool Equals(object obj) { return obj is Pooled; }
            public override int GetHashCode() { return 1; }
        }
        private static void Pool()
        {
            var pool = new ClassObjectPool(); var a = pool.Dequeue<Pooled>(); var b = pool.Dequeue<Pooled>();
            a.Value = 42; pool.Enqueue(a); pool.Enqueue(b); Throws<InvalidOperationException>(() => pool.Enqueue(a));
            Assert(ReferenceEquals(a, pool.Dequeue<Pooled>()) && a.Value == 0);
            Assert(ReferenceEquals(b, pool.Dequeue<Pooled>()));
            pool.SetResideCount<Pooled>(0); pool.Enqueue(a); Assert(!ReferenceEquals(a, pool.Dequeue<Pooled>()));
        }
        private static void Contexts()
        {
            using (var a = new ClientContext()) using (var b = new ClientContext())
            {
                int calls = 0; a.Event.CommonEvent.AddEventListener(1, _ => calls++);
                b.Event.CommonEvent.Dispatch(1); Assert(calls == 0);
                a.Event.CommonEvent.Dispatch(1); Assert(calls == 1); a.Dispose();
                Throws<ObjectDisposedException>(() => a.Tick(1)); b.Tick(1);
            }
        }
        private static void Localization()
        {
            var localization = new LocalizationManager();
            localization.SetLanguage("zh-CN", new Dictionary<string, string> { { "greeting", "你好，{0}" } });
            Assert(localization.GetString("greeting", "玩家") == "你好，玩家" && localization.GetString("missing") == "missing");
        }
        private static void BinaryRoundTrip()
        {
            using (var stream = new MMO_MemoryStream())
            {
                stream.WriteInt(0x01020304); stream.WriteUTF8String("你好"); stream.WriteBool(true);
                Assert(stream.ToArray()[0] == 4); stream.Position = 0;
                Assert(stream.ReadInt() == 0x01020304 && stream.ReadUTF8String() == "你好" && stream.ReadBool());
            }
        }
        private static void BinaryTruncation()
        {
            using (var stream = new MMO_MemoryStream(new byte[] { 1 })) Throws<EndOfStreamException>(() => stream.ReadInt());
            using (var stream = new MMO_MemoryStream()) Throws<EndOfStreamException>(() => stream.ReadBool());
        }
        private static void Compression()
        {
            var bytes = Encoding.UTF8.GetBytes("framework data 中文");
            Assert(Encoding.UTF8.GetString(GZipUtil.Decompress(GZipUtil.Compress(bytes))) == "framework data 中文");
        }
        private static void Frames()
        {
            var encoder = new LengthPrefixCodec(); var wire = new List<byte>();
            wire.AddRange(encoder.Encode(new byte[] { 1, 2, 3 })); wire.AddRange(encoder.Encode(new byte[0])); wire.AddRange(encoder.Encode(new byte[] { 4 }));
            var output = new List<byte[]>(); var decoder = new LengthPrefixCodec();
            foreach (byte value in wire) decoder.Feed(new[] { value }, 0, 1, output);
            Assert(output.Count == 3 && output[0][2] == 3 && output[1].Length == 0 && output[2][0] == 4);
            output.Clear(); decoder = new LengthPrefixCodec(); decoder.Feed(wire.ToArray(), 0, wire.Count, output); Assert(output.Count == 3);
        }
        private static void FrameLimit()
        {
            var codec = new LengthPrefixCodec(8);
            Throws<InvalidDataException>(() => codec.Feed(new byte[] { 255, 255, 255, 255 }, 0, 4, new List<byte[]>()));
            Throws<ArgumentOutOfRangeException>(() => new LengthPrefixCodec(8).Encode(new byte[9]));
        }
        private static async Task TcpLoopback()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            using (var channel = new TcpChannel())
            {
                try
                {
                    var accepted = listener.AcceptTcpClientAsync();
                    await channel.ConnectAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port).ConfigureAwait(false);
                    using (var server = await accepted.ConfigureAwait(false))
                    {
                        int callbacks = 0, pumpThread = 0;
                        channel.MessageReceived += bytes => { Assert(Thread.CurrentThread.ManagedThreadId == pumpThread); Assert(bytes[0] == 7); callbacks++; };
                        await channel.SendAsync(new byte[] { 7 }).ConfigureAwait(false);
                        var stream = server.GetStream(); var wire = new byte[5]; int offset = 0;
                        using (var timeout = new CancellationTokenSource(5000))
                            while (offset < wire.Length) offset += await stream.ReadAsync(wire, offset, wire.Length - offset, timeout.Token).ConfigureAwait(false);
                        await stream.WriteAsync(wire, 0, 2).ConfigureAwait(false);
                        await stream.WriteAsync(wire, 2, 3).ConfigureAwait(false);
                        await Task.Delay(20).ConfigureAwait(false); Assert(callbacks == 0);
                        var until = DateTime.UtcNow.AddSeconds(5);
                        while (callbacks == 0 && DateTime.UtcNow < until)
                        {
                            pumpThread = Thread.CurrentThread.ManagedThreadId; channel.Pump();
                            if (callbacks == 0) await Task.Delay(10).ConfigureAwait(false);
                        }
                        Assert(callbacks == 1);
                    }
                }
                finally { listener.Stop(); }
            }
        }
    }
}
