using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace VFXViewer
{
    internal static class SpectatorTransport
    {
        public static async Task WriteEnvelopeAsync(NetworkStream stream, SpectatorMessageKind kind, object payload, CancellationToken cancellationToken)
        {
            if (stream == null)
                return;

            string json = payload == null
                ? SpectatorProtocol.SerializeEnvelope<SpectatorHeartbeat>(kind, null)
                : SpectatorProtocol.SerializeEnvelope(kind, payload);
            byte[] frame = SpectatorProtocol.EncodeFrame(json);
            await stream.WriteAsync(frame, 0, frame.Length, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async Task<string> ReadFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            byte[] lengthBytes = new byte[4];
            int read = await ReadExactAsync(stream, lengthBytes, 0, lengthBytes.Length, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                return null;

            int payloadLength = BitConverter.ToInt32(lengthBytes, 0);
            if (payloadLength <= 0)
                return string.Empty;

            byte[] payloadBytes = new byte[payloadLength];
            read = await ReadExactAsync(stream, payloadBytes, 0, payloadBytes.Length, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                return null;

            return System.Text.Encoding.UTF8.GetString(payloadBytes);
        }

        private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int length, CancellationToken cancellationToken)
        {
            int totalRead = 0;
            while (totalRead < length)
            {
                int read = await stream.ReadAsync(buffer, offset + totalRead, length - totalRead, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    return totalRead == 0 ? 0 : -1;

                totalRead += read;
            }

            return totalRead;
        }
    }
}
