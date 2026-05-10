using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DirectPackageInstaller.IO
{
    public class SegmentedStream : Stream
    {
        public static int DefaultConcurrency = 4;
        
        public int BufferSize { get; }
        private readonly bool _closeBuffer;

        private readonly Func<Stream> _openSegment;
        private readonly Func<Stream> _openBuffer;
        
        private Stream _readerStream;
        private readonly Stream _writerStream;

        private readonly List<Stream> _streams = new List<Stream>();
        
        public int Concurrency { get; }
        public long TotalSize { get; private set; }
        
        private readonly List<Segment> _segments = new List<Segment>();
        private readonly object _lock = new object();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly List<Task> _tasks = new List<Task>();
        
        private class Segment
        {
            public long Offset;
            public long Length;
            public long Downloaded;
            public bool IsCompleted => Downloaded >= Length;
            public Stream BaseStream;
        }

        public long ScanProgress
        {
            get
            {
                lock (_lock)
                {
                    long progress = 0;
                    foreach (var seg in _segments.OrderBy(s => s.Offset))
                    {
                        if (seg.Offset > progress)
                            break;
                        progress = Math.Max(progress, seg.Offset + seg.Downloaded);
                        if (!seg.IsCompleted)
                            break;
                    }
                    return progress;
                }
            }
        }

        public long TotalProgress
        {
            get
            {
                lock (_lock)
                {
                    return _segments.Sum(x => x.Downloaded);
                }
            }
        }

        public bool InProgress => _tasks.Any(t => !t.IsCompleted);
        public bool InProgess => InProgress; // Alias for backward compatibility
        public bool Finished => TotalProgress >= TotalSize && !InProgress;
        public Func<Stream> OpenSegment => _openSegment;

        public SegmentedStream(Func<Stream> openConnection, Func<Stream> openBuffer, int bufferSize = 1024 * 1024, bool closeBuffer = false, int? concurrency = null)
        {
            _openSegment = openConnection;
            _openBuffer = openBuffer;
            BufferSize = bufferSize;
            _closeBuffer = closeBuffer;
            Concurrency = concurrency ?? DefaultConcurrency;

            if (_openBuffer == null)
            {
                var tempFile = TempHelper.GetTempFile(null);
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
                
                _writerStream = new FileStream(tempFile, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite, BufferSize, TransferTuning.TempFileOptions);
            }
            else
            {
                _writerStream = _openBuffer();
            }

            _streams.Add(_writerStream);

            // Open first connection to get size
            var firstStream = _openSegment();
            TotalSize = firstStream.Length;

            if (_writerStream.Length != TotalSize)
                _writerStream.SetLength(TotalSize);

            var firstSegment = new Segment
            {
                Offset = 0,
                Length = TotalSize,
                Downloaded = 0,
                BaseStream = firstStream
            };

            _segments.Add(firstSegment);

            StartDownloadTask(firstSegment);
            
            // Start monitor task to spawn more connections
            Task.Run(MonitorTasksAsync);
        }

        private async Task MonitorTasksAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested && TotalProgress < TotalSize)
                {
                    int activeConnections;
                    Segment bestSegmentToSplit = null;

                    lock (_lock)
                    {
                        activeConnections = _segments.Count(s => !s.IsCompleted);

                        if (activeConnections < Concurrency)
                        {
                            // Find the segment with the largest remaining bytes
                            bestSegmentToSplit = _segments
                                .Where(s => !s.IsCompleted)
                                .OrderByDescending(s => s.Length - s.Downloaded)
                                .FirstOrDefault();

                            if (bestSegmentToSplit != null)
                            {
                                long remaining = bestSegmentToSplit.Length - bestSegmentToSplit.Downloaded;
                                if (remaining < 2 * 1024 * 1024) // Only split if more than 2MB remaining
                                {
                                    bestSegmentToSplit = null;
                                }
                            }
                        }
                    }

                    if (bestSegmentToSplit != null)
                    {
                        long remaining = bestSegmentToSplit.Length - bestSegmentToSplit.Downloaded;
                        long splitSize = remaining / 2;

                        long newSegmentOffset;
                        long newSegmentLength = splitSize;
                        
                        lock (_lock)
                        {
                            newSegmentOffset = bestSegmentToSplit.Offset + bestSegmentToSplit.Length - splitSize;
                            bestSegmentToSplit.Length -= splitSize;
                            
                            var newSegment = new Segment
                            {
                                Offset = newSegmentOffset,
                                Length = newSegmentLength,
                                Downloaded = 0
                            };
                            _segments.Add(newSegment);
                            StartDownloadTask(newSegment);
                        }
                    }

                    await Task.Delay(500, _cts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                Cancel();
            }
        }

        private void StartDownloadTask(Segment segment)
        {
            var t = Task.Run(() => DownloadSegmentAsync(segment));
            lock (_lock)
            {
                _tasks.Add(t);
                _tasks.RemoveAll(x => x.IsCompleted);
            }
        }

        private async Task DownloadSegmentAsync(Segment segment)
        {
            byte[] buffer = new byte[BufferSize];

            while (!_cts.IsCancellationRequested)
            {
                long currentLength;
                lock (_lock) { currentLength = segment.Length; }

                if (segment.Downloaded >= currentLength)
                    break;

                VirtualStream vStream = null;
                try
                {
                    if (segment.BaseStream == null)
                    {
                        segment.BaseStream = _openSegment();
                    }

                    vStream = new VirtualStream(segment.BaseStream, segment.Offset, segment.Length) { ForceAmount = true };
                    vStream.Position = segment.Downloaded;

                    while (!_cts.IsCancellationRequested)
                    {
                        lock (_lock) { currentLength = segment.Length; }
                        vStream.SetLength(currentLength);

                        if (segment.Downloaded >= currentLength)
                            break;

                        int toRead = (int)Math.Min(buffer.Length, currentLength - segment.Downloaded);
                        
                        int read = await vStream.ReadAsync(buffer, 0, toRead, _cts.Token);
                        
                        if (read == 0)
                        {
                            await Task.Delay(100, _cts.Token);
                            continue;
                        }

                        lock (_lock)
                        {
                            _writerStream.Position = segment.Offset + segment.Downloaded;
                            _writerStream.Write(buffer, 0, read);
                            segment.Downloaded += read;
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception)
                {
                    if (!_cts.IsCancellationRequested)
                    {
                        await Task.Delay(2000);
                    }
                }
                finally
                {
                    vStream?.Dispose();
                    segment.BaseStream?.Dispose();
                    segment.BaseStream = null;
                }
            }
        }

        public void Cancel()
        {
            _cts.Cancel();
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => TotalSize;

        private long _position = 0;
        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override void Flush()
        {
            lock (_lock)
            {
                _writerStream?.Flush();
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count <= 0)
                return 0;

            int antiBuffer = Finished ? 0 : BufferSize * 2;
            
            while (true)
            {
                long scanProgress = ScanProgress;
                if (scanProgress >= _position + antiBuffer + count)
                {
                    break;
                }

                if (Finished || scanProgress == TotalSize)
                {
                    break;
                }
                
                long requestedEnd = _position + count + antiBuffer;
                bool isReady = false;
                lock (_lock)
                {
                    var seg = _segments.FirstOrDefault(s => s.Offset <= _position && s.Offset + s.Downloaded > _position);
                    if (seg != null)
                    {
                        if (seg.Offset + seg.Downloaded >= requestedEnd || seg.IsCompleted)
                        {
                            isReady = true;
                        }
                    }
                }

                if (isReady)
                    break;

                Task.Delay(100).Wait();
            }

            lock (_lock)
            {
                _writerStream.Seek(_position, SeekOrigin.Begin);
                
                long maxAvailable = 0;
                var seg = _segments.FirstOrDefault(s => s.Offset <= _position && s.Offset + s.Downloaded > _position);
                if (seg != null)
                {
                    maxAvailable = (seg.Offset + seg.Downloaded) - _position;
                    if (!Finished && !seg.IsCompleted)
                        maxAvailable -= antiBuffer;
                }

                if (maxAvailable <= 0 && TotalSize > 0 && !Finished)
                    maxAvailable = 1;

                if (count > maxAvailable && maxAvailable > 0)
                    count = (int)maxAvailable;
                
                if (count <= 0) return 0;

                int read = _writerStream.Read(buffer, offset, count);
                _position += read;
                return read;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    Position = offset;
                    break;
                case SeekOrigin.Current:
                    Position += offset;
                    break;
                case SeekOrigin.End:
                    Position = Length + offset;
                    break;
            }
            return Position;
        }

        protected override void Dispose(bool disposing)
        {
            Cancel();
            
            if (_closeBuffer)
            {
                foreach (var stream in _streams)
                    stream?.Close();
                _streams.Clear();
            }

            base.Dispose(disposing);
        }

        public override void SetLength(long value) => throw new NotImplementedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();
    }
}
