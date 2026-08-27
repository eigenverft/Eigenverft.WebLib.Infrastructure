using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http.Features;

namespace Eigenverft.WebLib.Infrastructure.Hosting.RequestTrafficLogging
{
    internal sealed class RequestTrafficCountingReadStream : Stream
    {
        private readonly Stream _inner;

        public RequestTrafficCountingReadStream(Stream inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public long BytesRead { get; private set; }

        public bool ReachedEnd { get; private set; }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _inner.Read(buffer, offset, count);
            ObserveRead(read, count);
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            int read = _inner.Read(buffer);
            ObserveRead(read, buffer.Length);
            return read;
        }

        public override int ReadByte()
        {
            int value = _inner.ReadByte();
            if (value < 0)
            {
                ReachedEnd = true;
            }
            else
            {
                BytesRead++;
            }

            return value;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int read = await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            ObserveRead(read, count);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            ObserveRead(read, buffer.Length);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            // The framework-owned request body stream is restored and disposed by its owner.
            base.Dispose(disposing);
        }

        private void ObserveRead(int read, int requestedCount)
        {
            if (read > 0)
            {
                BytesRead += read;
            }
            else if (requestedCount > 0)
            {
                ReachedEnd = true;
            }
        }
    }

    internal sealed class RequestTrafficCountingWriteStream : Stream
    {
        private readonly Stream _inner;

        public RequestTrafficCountingWriteStream(Stream inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public long BytesWritten { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => _inner.CanWrite;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            BytesWritten += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _inner.Write(buffer);
            BytesWritten += buffer.Length;
        }

        public override void WriteByte(byte value)
        {
            _inner.WriteByte(value);
            BytesWritten++;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _inner.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            BytesWritten += count;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            BytesWritten += buffer.Length;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => _inner.SetLength(value);

        protected override void Dispose(bool disposing)
        {
            // The framework-owned response stream is restored and disposed by its owner.
            base.Dispose(disposing);
        }

        internal void AddBytes(long count)
        {
            if (count > 0)
            {
                BytesWritten += count;
            }
        }
    }

    internal sealed class RequestTrafficCountingResponseBodyFeature : IHttpResponseBodyFeature
    {
        private readonly IHttpResponseBodyFeature _inner;
        private PipeWriter? _writer;

        public RequestTrafficCountingResponseBodyFeature(IHttpResponseBodyFeature inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            CountingStream = new RequestTrafficCountingWriteStream(inner.Stream);
        }

        public RequestTrafficCountingWriteStream CountingStream { get; }

        public Stream Stream => CountingStream;

        public PipeWriter Writer => _writer ??= PipeWriter.Create(
            CountingStream,
            new StreamPipeWriterOptions(leaveOpen: true));

        public void DisableBuffering() => _inner.DisableBuffering();

        public async Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellation)
        {
            await _inner.SendFileAsync(path, offset, count, cancellation).ConfigureAwait(false);

            long bytesSent = count ?? Math.Max(0L, new FileInfo(path).Length - offset);
            CountingStream.AddBytes(bytesSent);
        }

        public Task StartAsync(CancellationToken token = default) => _inner.StartAsync(token);

        public Task CompleteAsync() => _inner.CompleteAsync();
    }
}
