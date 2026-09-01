using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace RELYR;

/// <summary>
/// A bounded, shared-clock GIF renderer for Deck icons. Decoding is performed
/// away from the UI thread and every frame is detached at the displayed size,
/// so a large source image cannot make each Deck button retain full-size frames.
/// </summary>
internal sealed class AnimatedGifIcon : System.Windows.Controls.Image
{
    const long MaxFileBytes = 16 * 1024 * 1024;
    const int MaxSourceEdge = 4096;
    const int MaxRetainedFrames = 24;
    const int MaxRenderedEdge = 64;
    internal const int MaxCachedAnimations = 32;
    static readonly SemaphoreSlim DecodeSlots = new(2, 2);
    static readonly ConcurrentDictionary<string, Lazy<Task<AnimationData?>>> Cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly List<WeakReference<AnimatedGifIcon>> Active = [];
    static readonly DispatcherTimer Clock = CreateClock();

    readonly string path;
    readonly int renderedEdge;
    AnimationData? animation;
    int frameIndex;
    long frameAdvanceCount;
    long nextFrameAt;
    bool registered;
    internal int FrameCountForTest => animation?.Frames.Count ?? 0;
    internal int FrameIndexForTest => frameIndex;
    internal long FrameAdvanceCountForTest => frameAdvanceCount;

    internal AnimatedGifIcon(string path, double size)
    {
        this.path = Path.GetFullPath(path);
        renderedEdge = Math.Clamp((int)Math.Ceiling(size * 1.5), 16, MaxRenderedEdge);
        Width = size;
        Height = size;
        Stretch = Stretch.Uniform;
        IsHitTestVisible = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    static DispatcherTimer CreateClock()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += (_, _) => TickAnimations();
        return timer;
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Register();
        if (animation != null)
            return;
        try
        {
            string cacheKey = $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}|{renderedEdge}";
            if (Cache.Count >= MaxCachedAnimations)
                Cache.TryRemove(Cache.Keys.FirstOrDefault() ?? "", out _);
            var lazy = Cache.GetOrAdd(cacheKey, _ => new Lazy<Task<AnimationData?>>(() => DecodeAsync(path, renderedEdge)));
            animation = await lazy.Value;
            if (animation is { Frames.Count: > 0 } && IsLoaded)
            {
                frameIndex = 0;
                Source = animation.Frames[0];
                nextFrameAt = Environment.TickCount64 + animation.Delays[0];
            }
        }
        catch
        {
            // Invalid or inaccessible GIFs remain blank instead of taking down the overlay.
        }
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        registered = false;
        lock (Active)
        {
            Active.RemoveAll(reference => !reference.TryGetTarget(out var icon) || ReferenceEquals(icon, this));
            if (Active.Count == 0)
                Clock.Stop();
        }
    }

    void Register()
    {
        if (registered)
            return;
        registered = true;
        lock (Active)
        {
            Active.Add(new WeakReference<AnimatedGifIcon>(this));
            if (!Clock.IsEnabled)
                Clock.Start();
        }
    }

    static void TickAnimations()
    {
        long now = Environment.TickCount64;
        lock (Active)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                if (!Active[i].TryGetTarget(out var icon))
                {
                    Active.RemoveAt(i);
                    continue;
                }
                if (!icon.registered || !icon.IsVisible || icon.animation is not { Frames.Count: > 1 } data || now < icon.nextFrameAt)
                    continue;
                icon.frameIndex = (icon.frameIndex + 1) % data.Frames.Count;
                icon.frameAdvanceCount++;
                icon.Source = data.Frames[icon.frameIndex];
                icon.nextFrameAt = now + data.Delays[icon.frameIndex];
            }
            if (Active.Count == 0)
                Clock.Stop();
        }
    }

    static async Task<AnimationData?> DecodeAsync(string path, int edge)
    {
        await DecodeSlots.WaitAsync();
        try
        {
            return await Task.Run(() => Decode(path, edge));
        }
        finally
        {
            DecodeSlots.Release();
        }
    }

    static AnimationData? Decode(string path, int edge)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0 || info.Length > MaxFileBytes)
            return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> header = stackalloc byte[10];
        if (stream.Read(header) != header.Length || header[0] != (byte)'G' || header[1] != (byte)'I' || header[2] != (byte)'F')
            return null;
        int sourceWidth = header[6] | header[7] << 8;
        int sourceHeight = header[8] | header[9] << 8;
        if (sourceWidth <= 0 || sourceHeight <= 0 || sourceWidth > MaxSourceEdge || sourceHeight > MaxSourceEdge)
            return null;
        stream.Position = 0;
        var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0 || decoder.Frames[0].PixelWidth > MaxSourceEdge || decoder.Frames[0].PixelHeight > MaxSourceEdge)
            return null;
        int sourceCount = decoder.Frames.Count;
        int retainedCount = Math.Min(sourceCount, MaxRetainedFrames);
        var frames = new List<BitmapSource>(retainedCount);
        var delays = new List<int>(retainedCount);
        for (int retained = 0; retained < retainedCount; retained++)
        {
            int start = retained * sourceCount / retainedCount;
            int end = Math.Max(start + 1, (retained + 1) * sourceCount / retainedCount);
            var frame = decoder.Frames[start];
            double scale = Math.Min((double)edge / frame.PixelWidth, (double)edge / frame.PixelHeight);
            var transformed = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            transformed.Freeze();
            int bitsPerPixel = transformed.Format.BitsPerPixel;
            int stride = (transformed.PixelWidth * bitsPerPixel + 7) / 8;
            var pixels = new byte[stride * transformed.PixelHeight];
            transformed.CopyPixels(pixels, stride, 0);
            var rendered = BitmapSource.Create(transformed.PixelWidth, transformed.PixelHeight, 96, 96, transformed.Format, transformed.Palette, pixels, stride);
            rendered.Freeze();
            frames.Add(rendered);
            int delay = 0;
            for (int source = start; source < end; source++)
                delay += FrameDelay(decoder.Frames[source]);
            delays.Add(Math.Clamp(delay, 50, 1000));
        }
        return new AnimationData(frames, delays);
    }

    static int FrameDelay(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("/grctlext/Delay") is ushort hundredths)
                return Math.Max(20, hundredths * 10);
        }
        catch { }
        return 100;
    }

    sealed record AnimationData(IReadOnlyList<BitmapSource> Frames, IReadOnlyList<int> Delays);
}
