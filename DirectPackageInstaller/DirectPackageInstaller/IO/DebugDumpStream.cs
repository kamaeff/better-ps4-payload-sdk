using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DirectPackageInstaller.IO
{
    public class DebugDumpStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly FileStream _dumpStream;
        private readonly object _lock = new object();

        public DebugDumpStream(Stream baseStream, string dumpFilePath)
        {
            _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
            
            // Open with ReadWrite share so multiple concurrent requests can write to the same dump file
            _dumpStream = new FileStream(dumpFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        }

        public override bool CanRead => _baseStream.CanRead;
        public override bool CanSeek => _baseStream.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _baseStream.Length;

        public override long Position
        {
            get => _baseStream.Position;
            set => _baseStream.Position = value;
        }

        public override void Flush() => _baseStream.Flush();

        private void WriteDump(byte[] buffer, int offset, int count, long startPosition)
        {
            if (count <= 0) return;
            
            lock (_lock)
            {
                _dumpStream.Position = startPosition;
                _dumpStream.Write(buffer, offset, count);
                _dumpStream.Flush();
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long startPos = Position;
            int read = _baseStream.Read(buffer, offset, count);
            if (read > 0)
                WriteDump(buffer, offset, read, startPos);
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            long startPos = Position;
            int read = await _baseStream.ReadAsync(buffer, offset, count, cancellationToken);
            if (read > 0)
                WriteDump(buffer, offset, read, startPos);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            long startPos = Position;
            int read = await _baseStream.ReadAsync(buffer, cancellationToken);
            if (read > 0)
            {
                // Memory<T> can be converted to array if it's backed by one
                if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)buffer, out var segment))
                {
                    WriteDump(segment.Array, segment.Offset, read, startPos);
                }
                else
                {
                    byte[] tempBuffer = buffer.Slice(0, read).ToArray();
                    WriteDump(tempBuffer, 0, read, startPos);
                }
            }
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => _baseStream.Seek(offset, origin);
        public override void SetLength(long value) => _baseStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _baseStream.Dispose();
                _dumpStream.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
