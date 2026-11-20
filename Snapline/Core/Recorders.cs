using System.IO;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Snapline.Core;

public interface IFrameRecorder
{
    Task StartAsync();
    Task<string> StopAsync();
    TimeSpan Elapsed { get; }
    bool Paused { get; set; }
    BitmapSource? Thumbnail { get; }
}

/// <summary>Wall clock that excludes paused time.</summary>
internal sealed class RecClock
{
    private readonly object _gate = new();
    private readonly Stopwatch _sw = new();
    private TimeSpan _pausedTotal;
    private Stopwatch? _pause;

    public void Start() { lock (_gate) _sw.Start(); }
    public bool Paused { get { lock (_gate) return _pause != null; } }
    public void Pause() { lock (_gate) _pause ??= Stopwatch.StartNew(); }
    public void Resume()
    {
        lock (_gate)
        {
            if (_pause == null) return;
            _pausedTotal += _pause.Elapsed;
            _pause = null;
        }
    }
    public TimeSpan Elapsed
    {
        get { lock (_gate) return _sw.Elapsed - _pausedTotal - (_pause?.Elapsed ?? TimeSpan.Zero); }
    }
}

internal static class FrameUtil
{
    public static BitmapSource ToBitmap(byte[] bgra, int w, int h)
    {
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        b.Freeze();
        return b;
    }
}

/// <summary>H.264 MP4 recorder: GDI frames are pulled by a MediaStreamSource and encoded by MediaTranscoder (hardware when available).</summary>
public sealed class Mp4Recorder : IFrameRecorder
{
    private readonly Int32Rect _r;
    private readonly int _fps;
    private readonly bool _cursor;
    private readonly RecClock _clock = new();
    private GdiCapturer? _cap;
    private byte[] _frame = Array.Empty<byte>();
    private global::Windows.Storage.Streams.IRandomAccessStream? _stream;
    private Task? _transcode;
    private volatile bool _stopping;
    private long _index;

    public Mp4Recorder(Int32Rect region, int fps, bool cursor, string path)
    {
        _r = region; _fps = Math.Clamp(fps, 10, 60); _cursor = cursor; OutputPath = path;
    }

    public string OutputPath { get; private set; }
    public BitmapSource? Thumbnail { get; private set; }
    public TimeSpan Elapsed => _clock.Elapsed;
    public bool Paused { get => _clock.Paused; set { if (value) _clock.Pause(); else _clock.Resume(); } }

    public async Task StartAsync()
    {
        _cap = new GdiCapturer(_r.Width, _r.Height);
        _frame = new byte[_cap.ByteCount];

        var props = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)_r.Width, (uint)_r.Height);
        props.FrameRate.Numerator = (uint)_fps;
        props.FrameRate.Denominator = 1;
        var mss = new MediaStreamSource(new VideoStreamDescriptor(props)) { BufferTime = TimeSpan.Zero, IsLive = true };
        mss.Starting += (_, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
        mss.SampleRequested += OnSampleRequested;

        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Audio = null;
        profile.Video.Width = (uint)_r.Width;
        profile.Video.Height = (uint)_r.Height;
        profile.Video.FrameRate.Numerator = (uint)_fps;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.Bitrate = (uint)Math.Clamp((long)_r.Width * _r.Height * _fps / 7, 2_000_000L, 60_000_000L);

        var dir = Path.GetDirectoryName(OutputPath)!;
        Directory.CreateDirectory(dir);
        var folder = await StorageFolder.GetFolderFromPathAsync(dir);
        var file = await folder.CreateFileAsync(Path.GetFileName(OutputPath), CreationCollisionOption.GenerateUniqueName);
        OutputPath = file.Path;
        _stream = await file.OpenAsync(FileAccessMode.ReadWrite);

        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prep = await transcoder.PrepareMediaStreamSourceTranscodeAsync(mss, _stream, profile);
        if (!prep.CanTranscode) throw new InvalidOperationException("The video encoder is unavailable: " + prep.FailureReason);
        _clock.Start();
        _transcode = prep.TranscodeAsync().AsTask();
    }

    private void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        try
        {
            var due = TimeSpan.FromTicks(_index * TimeSpan.TicksPerSecond / _fps);
            while (!_stopping)
            {
                var el = _clock.Elapsed;
                bool paused = _clock.Paused;
                if (!paused && el >= due) break;
                int wait = paused ? 15 : (int)Math.Clamp((due - el).TotalMilliseconds, 1, 15);
                Thread.Sleep(wait);
            }
            if (_stopping || _cap == null) { args.Request.Sample = null; return; }

            var ts = _clock.Elapsed;
            _cap.Grab(_r.X, _r.Y, _cursor, false, _frame);
            Thumbnail ??= FrameUtil.ToBitmap((byte[])_frame.Clone(), _r.Width, _r.Height);

            // Media Foundation treats uncompressed RGB as bottom-up, so write the rows in reverse.
            var buffer = new global::Windows.Storage.Streams.Buffer((uint)_frame.Length);
            int stride = _r.Width * 4;
            for (int row = 0; row < _r.Height; row++)
                _frame.CopyTo((_r.Height - 1 - row) * stride, buffer, (uint)(row * stride), stride);
            buffer.Length = (uint)_frame.Length;
            var sample = MediaStreamSample.CreateFromBuffer(buffer, ts);
            sample.Duration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / _fps);
            args.Request.Sample = sample;
            _index = Math.Max(_index + 1, (long)(ts.TotalSeconds * _fps) + 1);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            args.Request.Sample = null;
        }
    }

    public async Task<string> StopAsync()
    {
        _stopping = true;
        _clock.Resume();
        try
        {
            if (_transcode != null)
            {
                var done = await Task.WhenAny(_transcode, Task.Delay(20000));
                if (done == _transcode) await _transcode;
            }
        }
        finally
        {
            _stream?.Dispose();
            _cap?.Dispose();
            _cap = null;
        }
        return OutputPath;
    }
}

/// <summary>Animated GIF recorder: a capture thread feeds a bounded queue drained by an encoder thread.</summary>
public sealed class GifRecorder : IFrameRecorder
{
    private readonly Int32Rect _r;
    private readonly int _fps;
    private readonly bool _cursor;
    private readonly string _path;
    private readonly int _outW, _outH;
    private readonly RecClock _clock = new();
    private readonly BlockingCollection<(byte[] Px, TimeSpan T)> _queue = new(boundedCapacity: 120);
    private volatile bool _stop;
    private TimeSpan _end;
    private double _carry;
    private Task? _captureTask, _encodeTask;

    public GifRecorder(Int32Rect region, int fps, bool cursor, string path, int maxWidth = 960)
    {
        _r = region; _fps = fps; _cursor = cursor; _path = path;
        double s = Math.Min(1.0, maxWidth / (double)region.Width);
        _outW = Math.Max(2, (int)Math.Round(region.Width * s));
        _outH = Math.Max(2, (int)Math.Round(region.Height * s));
    }

    public BitmapSource? Thumbnail { get; private set; }
    public TimeSpan Elapsed => _clock.Elapsed;
    public bool Paused { get => _clock.Paused; set { if (value) _clock.Pause(); else _clock.Resume(); } }

    public Task StartAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _clock.Start();
        _captureTask = Task.Factory.StartNew(CaptureLoop, TaskCreationOptions.LongRunning);
        _encodeTask = Task.Factory.StartNew(EncodeLoop, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }

    private void CaptureLoop()
    {
        try
        {
            using var cap = new GdiCapturer(_outW, _outH);
            byte[]? prev = null;
            long index = 0;
            while (!_stop)
            {
                var due = TimeSpan.FromTicks(index * TimeSpan.TicksPerSecond / _fps);
                var el = _clock.Elapsed;
                if (_clock.Paused || el < due)
                {
                    Thread.Sleep(_clock.Paused ? 20 : (int)Math.Clamp((due - el).TotalMilliseconds, 1, 20));
                    continue;
                }
                var buf = new byte[cap.ByteCount];
                if (_outW == _r.Width && _outH == _r.Height) cap.Grab(_r.X, _r.Y, _cursor, false, buf);
                else cap.GrabScaled(_r.X, _r.Y, _r.Width, _r.Height, _cursor, buf);
                var t = _clock.Elapsed;
                index = Math.Max(index + 1, (long)(t.TotalSeconds * _fps) + 1);
                if (prev != null && buf.AsSpan().SequenceEqual(prev)) continue; // static screen: stretch previous frame
                Thumbnail ??= FrameUtil.ToBitmap(buf, _outW, _outH);
                _queue.Add((buf, t));
                prev = buf;
            }
        }
        catch (Exception ex) { Log.Error(ex); }
        finally { _queue.CompleteAdding(); }
    }

    private int Centiseconds(TimeSpan d)
    {
        double exact = d.TotalMilliseconds / 10.0 + _carry;
        int cs = Math.Max(2, (int)Math.Round(exact));
        _carry = exact - cs;
        return cs;
    }

    private void EncodeLoop()
    {
        using var gif = new GifWriter(_path, _outW, _outH);
        (byte[] Px, TimeSpan T)? pending = null;
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            if (pending is { } p) gif.AddFrame(p.Px, Centiseconds(item.T - p.T));
            pending = item;
        }
        if (pending is { } last) gif.AddFrame(last.Px, Math.Max(10, Centiseconds(_end - last.T)));
    }

    public async Task<string> StopAsync()
    {
        _end = _clock.Elapsed;
        _stop = true;
        if (_captureTask != null) await _captureTask;
        if (_encodeTask != null) await _encodeTask;
        return _path;
    }
}
