using System;
using System.Collections.Generic;
using System.IO;

namespace YouYou.Framework
{
    public interface IFrameCodec
    {
        byte[] Encode(byte[] payload);
        void Feed(byte[] buffer, int offset, int count, List<byte[]> output);
    }

    /// <summary>Four-byte little-endian payload length. Not the original MMO server protocol.</summary>
    public sealed class LengthPrefixCodec : IFrameCodec
    {
        private readonly int maxLength;
        private readonly byte[] header = new byte[4];
        private int headerCount, bodyCount;
        private byte[] body;

        public LengthPrefixCodec(int maxLength = 1024 * 1024)
        {
            if (maxLength <= 0 || maxLength > int.MaxValue - 4) throw new ArgumentOutOfRangeException(nameof(maxLength));
            this.maxLength = maxLength;
        }

        public byte[] Encode(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length > maxLength) throw new ArgumentOutOfRangeException(nameof(payload));
            int n = payload.Length;
            var result = new byte[n + 4];
            for (int i = 0; i < 4; i++) result[i] = (byte)(n >> (i * 8));
            Buffer.BlockCopy(payload, 0, result, 4, n);
            return result;
        }

        public void Feed(byte[] buffer, int offset, int count, List<byte[]> output)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException(nameof(count));
            while (count > 0)
            {
                if (body == null)
                {
                    int take = Math.Min(4 - headerCount, count);
                    Buffer.BlockCopy(buffer, offset, header, headerCount, take);
                    offset += take; count -= take; headerCount += take;
                    if (headerCount < 4) continue;
                    uint n = (uint)(header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24));
                    if (n > maxLength) throw new InvalidDataException("Incoming frame exceeds the configured limit.");
                    body = new byte[(int)n]; bodyCount = 0;
                }
                int copied = Math.Min(body.Length - bodyCount, count);
                Buffer.BlockCopy(buffer, offset, body, bodyCount, copied);
                offset += copied; count -= copied; bodyCount += copied;
                if (bodyCount == body.Length)
                {
                    output.Add(body);
                    body = null; headerCount = 0;
                }
            }
        }
    }
}
