using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MUMUsic.Desktop.Audio;

/// <summary>
/// Plays 16-bit stereo PCM pushed by <see cref="Write"/> on the default output device (waveOut). Small buffers keep the
/// sound about 100 ms behind the page; when the page sends faster than the device plays (two clocks), the oldest
/// samples are dropped, and when it sends nothing (paused) silence fills in. The device is closed after a few silent
/// seconds, so a paused app holds no audio stream (which would keep Windows from sleeping).
/// </summary>
internal sealed unsafe partial class WaveOutput : IDisposable
{
    private const int Channels = 2;
    private const int BlockAlign = Channels * sizeof(short);
    private const int BufferCount = 4;
    private const int BufferMs = 20;
    private const int MaxQueuedMs = 150;
    private const int StartQueuedBuffers = 2;
    private static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(3);

    private const uint WaveMapper = uint.MaxValue;
    private const uint CallbackEvent = 0x00050000;
    private const uint HeaderDone = 0x00000001;

    private readonly int _sampleRate;
    private readonly int _bufferBytes;
    private readonly byte[] _queue;
    private int _queueStart, _queued;
    private readonly object _gate = new();
    // Set by Write and by waveOut when a buffer has been played.
    private readonly AutoResetEvent _signal = new(false);
    private volatile bool _disposed;

    public WaveOutput(int sampleRate)
    {
        _sampleRate = sampleRate;
        _bufferBytes = sampleRate * BufferMs / 1000 * BlockAlign;
        _queue = new byte[sampleRate * MaxQueuedMs / 1000 * BlockAlign];
        new Thread(Loop) { IsBackground = true, Name = "Audio output", Priority = ThreadPriority.AboveNormal }.Start();
    }

    /// <summary>Interleaved 16-bit stereo samples (little endian), whole frames only.</summary>
    public void Write(ReadOnlySpan<byte> pcm)
    {
        pcm = pcm[..(pcm.Length / BlockAlign * BlockAlign)];
        if (pcm.Length > _queue.Length) pcm = pcm[^_queue.Length..];
        lock (_gate)
        {
            var overflow = _queued + pcm.Length - _queue.Length;
            if (overflow > 0)
            {
                _queueStart = (_queueStart + overflow) % _queue.Length;
                _queued -= overflow;
            }
            var end = (_queueStart + _queued) % _queue.Length;
            var first = Math.Min(pcm.Length, _queue.Length - end);
            pcm[..first].CopyTo(_queue.AsSpan(end));
            pcm[first..].CopyTo(_queue);
            _queued += pcm.Length;
        }
        _signal.Set();
    }

    public void Dispose()
    {
        _disposed = true;
        _signal.Set();
    }

    private int Queued
    {
        get
        {
            lock (_gate) return _queued;
        }
    }

    private int Read(Span<byte> target)
    {
        lock (_gate)
        {
            var count = Math.Min(target.Length, _queued);
            var first = Math.Min(count, _queue.Length - _queueStart);
            _queue.AsSpan(_queueStart, first).CopyTo(target);
            _queue.AsSpan(0, count - first).CopyTo(target[first..]);
            _queueStart = (_queueStart + count) % _queue.Length;
            _queued -= count;
            return count;
        }
    }

    private void Loop()
    {
        while (!_disposed)
        {
            _signal.WaitOne();
            if (_disposed || Queued < _bufferBytes * StartQueuedBuffers) continue;
            try
            {
                Play();
            }
            catch (IOException error)
            {
                Log.Error("audio output", error);
                Thread.Sleep(1000);
            }
        }
    }

    /// <summary>Opens the device and keeps it fed until the page has been silent for a while.</summary>
    private void Play()
    {
        var format = new WaveFormat
        {
            FormatTag = 1, Channels = Channels, SamplesPerSec = (uint)_sampleRate, AvgBytesPerSec = (uint)(_sampleRate * BlockAlign),
            BlockAlign = BlockAlign, BitsPerSample = 16,
        };
        Check(WaveOutOpen(out var device, WaveMapper, &format, _signal.SafeWaitHandle.DangerousGetHandle(), 0, CallbackEvent), "open");
        var headers = (WaveHeader*)NativeMemory.AllocZeroed((nuint)(BufferCount * sizeof(WaveHeader)));
        try
        {
            for (var i = 0; i < BufferCount; i++)
            {
                headers[i].Data = (byte*)NativeMemory.AllocZeroed((nuint)_bufferBytes);
                headers[i].BufferLength = (uint)_bufferBytes;
                Check(WaveOutPrepareHeader(device, &headers[i], (uint)sizeof(WaveHeader)), "prepare");
                headers[i].Flags |= HeaderDone;
            }
            var lastSound = Stopwatch.StartNew();
            while (!_disposed && lastSound.Elapsed < IdleClose)
            {
                for (var i = 0; i < BufferCount; i++)
                {
                    if ((headers[i].Flags & HeaderDone) == 0) continue;
                    var buffer = new Span<byte>(headers[i].Data, _bufferBytes);
                    var read = Read(buffer);
                    if (read > 0) lastSound.Restart();
                    buffer[read..].Clear();
                    Check(WaveOutWrite(device, &headers[i], (uint)sizeof(WaveHeader)), "write");
                }
                _signal.WaitOne(BufferMs * 2);
            }
        }
        finally
        {
            WaveOutReset(device);
            for (var i = 0; i < BufferCount; i++)
            {
                if (headers[i].Data is null) continue;
                WaveOutUnprepareHeader(device, &headers[i], (uint)sizeof(WaveHeader));
                NativeMemory.Free(headers[i].Data);
            }
            NativeMemory.Free(headers);
            WaveOutClose(device);
        }
    }

    private static void Check(int result, string step)
    {
        if (result != 0) throw new IOException($"waveOut {step} failed ({result})");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public byte* Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public nint User;
        public uint Flags;
        public uint Loops;
        public nint Next;
        public nint Reserved;
    }

    [LibraryImport("winmm.dll", EntryPoint = "waveOutOpen")]
    private static partial int WaveOutOpen(out nint device, uint deviceId, WaveFormat* format, nint callback, nint instance, uint flags);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutPrepareHeader")]
    private static partial int WaveOutPrepareHeader(nint device, WaveHeader* header, uint size);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutUnprepareHeader")]
    private static partial int WaveOutUnprepareHeader(nint device, WaveHeader* header, uint size);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutWrite")]
    private static partial int WaveOutWrite(nint device, WaveHeader* header, uint size);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutReset")]
    private static partial int WaveOutReset(nint device);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutClose")]
    private static partial int WaveOutClose(nint device);
}
