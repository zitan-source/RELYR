using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;

namespace RELYR;

internal static class DeckPanelLayout
{
    readonly record struct ThumbnailCacheKey(string Path, long ModifiedTicks, int Width, int Height);
    static readonly ConcurrentDictionary<ThumbnailCacheKey, System.Windows.Media.ImageSource> ThumbnailCache = new();
    internal const int MaxCachedThumbnails = 192;
    internal static int CachedLargeThumbnailCountForTest => ThumbnailCache.Keys.Count(key => key.Width > 160 || key.Height > 160);
    internal const string Layer = "Deck";
    internal const int Rows = 5;
    internal const int Columns = 9;
    internal const int SlotCount = Rows * Columns;
    internal const int MaximumRows = 18;
    internal const int MaximumColumns = 18;
    internal const int MaximumSlotCount = MaximumRows * MaximumColumns;
    internal const double KeyWidth = 54;
    internal const double KeyHeight = 52;
    internal const double Gap = 4;
    internal const double NameLabelHeight = 13;
    internal const double NameLabelAreaHeight = 14;
    // The name lives below the 54x52 button.  Match the visible horizontal
    // button gap to the vertical button-to-button distance, which includes
    // both the name row and the ordinary four-pixel gutter.
    internal const double ButtonGap = NameLabelAreaHeight + Gap;
    internal const double CellWidth = KeyWidth + ButtonGap;
    internal const double CellHeight = KeyHeight + ButtonGap;
    internal const string ActionPrefix = "ShowDeckPanelOverlay:";
    internal const string SlotDragFormat = "RELYR.DeckSlot";
    internal const string FileSourceDragFormat = "RELYR.DeckFileSource";
    internal const System.Windows.DragDropEffects ExternalFileDragEffects = System.Windows.DragDropEffects.Copy;

    static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"
    };

    static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".m4a", ".aac", ".wma", ".flac", ".ogg"
    };

    static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".avi", ".wmv", ".mkv", ".webm", ".mpeg", ".mpg"
    };

    static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".json", ".xml", ".log", ".srt", ".ass", ".vtt"
    };

    internal static string InputName(int slot) => $"{Layer}+{slot:00}";

    internal static bool IsInputName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || !input.StartsWith(Layer + "+", StringComparison.OrdinalIgnoreCase))
            return false;
        return int.TryParse(input[(Layer.Length + 1)..], out int slot) && slot is >= 1 and <= MaximumSlotCount;
    }

    internal static int SlotNumber(string input) =>
        IsInputName(input) && int.TryParse(input[(Layer.Length + 1)..], out int slot) ? slot : 0;

    internal static DeckLayoutDefinition? FindLayout(AppConfig config, string? id)
        => string.IsNullOrWhiteSpace(id) ? null : config.DeckLayouts.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    internal static Profile? ActiveProfile(AppConfig config)
        => config.Profiles.FirstOrDefault(x => x.Name.Equals(config.ActiveProfile, StringComparison.OrdinalIgnoreCase)) ?? config.Profiles.FirstOrDefault();

    internal static bool IsAvailableToProfile(DeckLayoutDefinition layout, Profile? profile)
        => !layout.ProfileSwitchEnabled || profile != null && layout.ProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase);

    internal static IEnumerable<DeckLayoutDefinition> LayoutsForActiveProfile(AppConfig config)
    {
        var profile = ActiveProfile(config);
        return config.DeckLayouts.Where(layout => IsAvailableToProfile(layout, profile));
    }

    internal static DeckLayoutDefinition? VariantForProfile(AppConfig config, DeckLayoutDefinition layout, Profile? profile)
    {
        if (!layout.ProfileSwitchEnabled)
            return layout;
        return profile == null ? null : config.DeckLayouts.FirstOrDefault(candidate => candidate.ProfileSwitchEnabled
            && candidate.ProfileGroupId.Equals(layout.ProfileGroupId, StringComparison.OrdinalIgnoreCase)
            && candidate.ProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
    }

    internal static DeckLayoutDefinition? DefaultLayout(AppConfig config)
    {
        var profile = ActiveProfile(config);
        var preferred = FindLayout(config, profile?.DefaultDeckLayoutId);
        if (preferred != null && IsAvailableToProfile(preferred, profile))
            return preferred;
        var global = FindLayout(config, config.DefaultDeckLayoutId);
        if (global != null && IsAvailableToProfile(global, profile))
            return global;
        return LayoutsForActiveProfile(config).FirstOrDefault();
    }

    internal static DeckLayoutDefinition? ResolveActionLayout(AppConfig config, string? action)
    {
        if (action?.StartsWith(ActionPrefix, StringComparison.OrdinalIgnoreCase) == true)
        {
            var requested = FindLayout(config, action[ActionPrefix.Length..]);
            return requested == null ? null : VariantForProfile(config, requested, ActiveProfile(config));
        }
        return action?.Equals(OverlayService.DeckPanelAction, StringComparison.OrdinalIgnoreCase) == true ? DefaultLayout(config) : null;
    }

    internal static string ActionValue(string layoutId) => ActionPrefix + layoutId;
    internal static bool IsDeckAction(string? value) => value?.Equals(OverlayService.DeckPanelAction, StringComparison.OrdinalIgnoreCase) == true
        || value?.StartsWith(ActionPrefix, StringComparison.OrdinalIgnoreCase) == true;

    internal static Mapping? FindMapping(DeckLayoutDefinition? layout, int slot)
    {
        string input = InputName(slot);
        return layout?.Mappings.LastOrDefault(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
    }

    internal static Mapping? FindMapping(AppConfig config, int slot) => FindMapping(DefaultLayout(config), slot);

    internal static void SwapSlots(DeckLayoutDefinition layout, int firstSlot, int secondSlot)
    {
        if (firstSlot == secondSlot || firstSlot < 1 || secondSlot < 1 || firstSlot > MaximumSlotCount || secondSlot > MaximumSlotCount)
            return;
        string first = InputName(firstSlot), second = InputName(secondSlot);
        MainWindow.TransferAssignments(layout.Mappings, first, second);
    }

    internal static bool TryParseButtonColor(string? value, out WpfColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        try
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(value) is WpfColor parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch { }
        return false;
    }

    internal static bool TryGetButtonColor(Mapping? mapping, out WpfColor color)
        => TryParseButtonColor(mapping?.DeckColor, out color);

    internal static bool HasRegisteredFile(Mapping? mapping) => !string.IsNullOrWhiteSpace(mapping?.DeckFilePath);
    internal static bool IsAvailableFile(Mapping? mapping) => HasRegisteredFile(mapping) && File.Exists(mapping!.DeckFilePath);
    internal static bool IsExecutableFile(string? path)
        => Path.GetExtension(path ?? "").Equals(".exe", StringComparison.OrdinalIgnoreCase);

    internal static void ApplyRegisteredFile(Mapping mapping, string path)
    {
        string normalized = Path.GetFullPath(path);
        mapping.DeckFilePath = normalized;
        mapping.DeckMonitor = string.Empty;
        if (!IsExecutableFile(normalized))
            return;

        // An executable dropped on a Deck button is a launcher, not merely a
        // decorative file face. Replace stale executable fields and let the
        // button render the file's own associated icon.
        mapping.Kind = ActionKind.Launch;
        mapping.Value = normalized;
        mapping.LongPressKind = ActionKind.None;
        mapping.LongPressValue = string.Empty;
        mapping.LongPressMs = 500;
        mapping.DragValue = string.Empty;
        mapping.DragEndValue = string.Empty;
        mapping.Application = string.Empty;
        mapping.DeckIcon = string.Empty;
        mapping.DeckIconPath = string.Empty;
        mapping.DeckIconAutoAssigned = false;
    }
    internal static string? GetDroppedFile(System.Windows.IDataObject data)
    {
        object? value;
        try
        {
            value = data.GetData(System.Windows.DataFormats.FileDrop, true);
        }
        catch { return null; }

        return value switch
        {
            string[] paths => FirstExistingFile(paths),
            StringCollection collection => FirstExistingFile(collection.Cast<string>()),
            IEnumerable<string> paths => FirstExistingFile(paths),
            _ => null
        };
    }

    static string? FirstExistingFile(IEnumerable<string> paths) => paths.FirstOrDefault(File.Exists);

    internal static bool IsInternalFileDrag(System.Windows.IDataObject data)
    {
        try
        {
            return data.GetDataPresent(FileSourceDragFormat);
        }
        catch { return false; }
    }

    internal static bool IsImageFile(string? path) => HasFileExtension(path, ImageExtensions);
    internal static bool IsAudioFile(string? path) => HasFileExtension(path, AudioExtensions);
    internal static bool IsVideoFile(string? path) => HasFileExtension(path, VideoExtensions);
    internal static bool IsTextFile(string? path) => HasFileExtension(path, TextExtensions);

    static bool HasFileExtension(string? path, ISet<string> extensions)
        => extensions.Contains(Path.GetExtension(path ?? ""));

    internal static string FileDisplayName(Mapping? mapping)
    {
        if (!HasRegisteredFile(mapping))
            return "";
        string name = Path.GetFileName(mapping!.DeckFilePath);
        return string.IsNullOrWhiteSpace(name) ? mapping.DeckFilePath : name;
    }

    internal static System.Windows.Media.ImageSource? LoadImageThumbnail(string? path, int decodePixels = 160)
    {
        if (!IsImageFile(path) || !File.Exists(path))
            return null;
        try
        {
            var key = ThumbnailKey(path!, decodePixels, 0);
            if (ThumbnailCache.TryGetValue(key, out var cached))
                return cached;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = decodePixels;
            image.UriSource = new Uri(path!, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            StoreThumbnail(key, image);
            return image;
        }
        catch { return null; }
    }

    internal static System.Windows.Media.ImageSource? LoadFileThumbnail(string? path, int decodePixels = 160)
    {
        var image = LoadImageThumbnail(path, decodePixels);
        if (image != null || !IsVideoFile(path) || !File.Exists(path))
            return image;
        return LoadVideoThumbnail(path, decodePixels, decodePixels);
    }

    internal static System.Windows.Media.ImageSource? LoadVideoThumbnail(string? path, int width = 320, int height = 180)
    {
        if (!IsVideoFile(path) || !File.Exists(path))
            return null;
        IntPtr bitmap = IntPtr.Zero;
        IShellItemImageFactory? shellItem = null;
        try
        {
            var key = ThumbnailKey(path!, width, height);
            if (ThumbnailCache.TryGetValue(key, out var cached))
                return cached;
            Guid iid = typeof(IShellItemImageFactory).GUID;
            int result = SHCreateItemFromParsingName(path!, IntPtr.Zero, ref iid, out shellItem);
            if (result < 0 || shellItem == null)
                return null;
            result = shellItem.GetImage(new NativeSize(width, height), ShellImageFlags.ThumbnailOnly | ShellImageFlags.BiggerSizeOk, out bitmap);
            if (result < 0 || bitmap == IntPtr.Zero)
                return null;
            // NativeSize is a requested bounding box. Keep the shell bitmap's
            // dimensions so WPF preserves the source video's aspect ratio.
            var thumbnail = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            thumbnail.Freeze();
            StoreThumbnail(key, thumbnail);
            return thumbnail;
        }
        catch { return null; }
        finally
        {
            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);
            if (shellItem != null)
                Marshal.ReleaseComObject(shellItem);
        }
    }

    internal static FrameworkElement CreateFileIcon(string? path, double size = 20)
    {
        if (IsExecutableFile(path) && ApplicationIconService.TryGetExtractedIcon(path) is { } executableIcon)
        {
            return new System.Windows.Controls.Image
            {
                Source = executableIcon,
                Width = size,
                Height = size,
                Stretch = System.Windows.Media.Stretch.Uniform,
                IsHitTestVisible = false
            };
        }
        var icon = new System.Windows.Shapes.Path
        {
            Stretch = System.Windows.Media.Stretch.Uniform,
            StrokeThickness = 1.8,
            StrokeLineJoin = System.Windows.Media.PenLineJoin.Round,
            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
            StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
            IsHitTestVisible = false
        };
        icon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(System.Windows.Controls.Button), 1)
        });
        if (IsAudioFile(path))
        {
            icon.Data = Geometry.Parse("M 3,2 L 19,12 L 3,22 Z");
            icon.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding("Foreground")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(System.Windows.Controls.Button), 1)
            });
            icon.StrokeThickness = 0;
        }
        else if (IsTextFile(path))
            icon.Data = Geometry.Parse("M 4,1 L 14,1 L 20,7 L 20,23 L 4,23 Z M 14,1 L 14,7 L 20,7 M 7,11 L 17,11 M 7,15 L 17,15 M 7,19 L 14,19");
        else
            icon.Data = Geometry.Parse("M 4,1 L 14,1 L 20,7 L 20,23 L 4,23 Z M 14,1 L 14,7 L 20,7");
        return new Viewbox { Width = size, Height = size, Stretch = System.Windows.Media.Stretch.Uniform, Child = icon, IsHitTestVisible = false };
    }

    internal static WpfColor TextColorFor(WpfColor background)
        => ContrastRatio(background, WpfColors.Black) >= ContrastRatio(background, WpfColors.White) ? WpfColors.Black : WpfColors.White;

    internal static double ContrastRatio(WpfColor first, WpfColor second)
    {
        double firstLuminance = RelativeLuminance(first);
        double secondLuminance = RelativeLuminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + .05) / (Math.Min(firstLuminance, secondLuminance) + .05);
    }

    static double RelativeLuminance(WpfColor color) =>
        .2126 * LinearChannel(color.R) + .7152 * LinearChannel(color.G) + .0722 * LinearChannel(color.B);

    static double LinearChannel(byte channel)
    {
        double value = channel / 255d;
        return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
    }

    internal static int VisibleSlotCount(DeckLayoutDefinition layout) => Math.Clamp(layout.Rows, 1, MaximumRows) * Math.Clamp(layout.Columns, 1, MaximumColumns);

    internal static IReadOnlyList<Profile> ProfilesWithDeckMappings(IEnumerable<Profile> profiles) => profiles
        .Where(profile => profile.Mappings.Any(map => IsInputName(map.Input)))
        .ToList();

    internal static int DistinctDeckCount(IEnumerable<Profile> profiles) => profiles
        .Select(profile => DeckSignature(profile.Mappings))
        .Distinct(StringComparer.Ordinal)
        .Count();

    static string DeckSignature(IEnumerable<Mapping> mappings) => string.Join("\n", mappings
        .Where(map => IsInputName(map.Input))
        .OrderBy(map => SlotNumber(map.Input))
        .Select(map => $"{map.Input}\u001f{map.Kind}\u001f{map.Value}\u001f{map.LongPressKind}\u001f{map.LongPressValue}\u001f{map.LongPressMs}\u001f{map.Application}\u001f{map.Description}\u001f{map.DeckColor}\u001f{map.DeckFilePath}\u001f{map.DeckIcon}\u001f{map.DeckIconPath}\u001f{map.DeckMonitor}"));

    internal static string ActionLabel(string input, Mapping? mapping)
    {
        int slot = SlotNumber(input);
        string action = MainWindow.MappingInterceptsInput(mapping)
            ? MainWindow.FriendlyActionValue(mapping!.Kind, mapping.Value)
            : slot.ToString("00");
        if (action.Length > 10)
            action = action[..9] + "…";
        return action;
    }

    internal static TextBlock CreateNameLabel(Mapping? mapping)
    {
        var label = new TextBlock
        {
            Text = mapping?.Description ?? "",
            Height = NameLabelHeight,
            Width = KeyWidth,
            FontSize = 8,
            Margin = new Thickness(0, 1, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        };
        // The floating Deck keeps labels in the same light hierarchy even when
        // a user gives an individual cell a custom tint.
        label.Foreground = new System.Windows.Media.SolidColorBrush(WpfColor.FromRgb(0x9A, 0x9E, 0xA5));
        return label;
    }

    static ThumbnailCacheKey ThumbnailKey(string path, int width, int height) =>
        new(Path.GetFullPath(path).ToUpperInvariant(), File.GetLastWriteTimeUtc(path).Ticks, width, height);

    static void StoreThumbnail(ThumbnailCacheKey key, System.Windows.Media.ImageSource image)
    {
        // Keep only compact Deck-face thumbnails. Large hover previews are
        // transient; caching hundreds of them can exhaust WPF render memory.
        if (key.Width > 160 || key.Height > 160)
            return;
        if (ThumbnailCache.Count >= MaxCachedThumbnails)
            ThumbnailCache.TryRemove(ThumbnailCache.Keys.FirstOrDefault(), out _);
        ThumbnailCache[key] = image;
    }

    internal static FrameworkElement CreateButtonContent(string input, Mapping? mapping, bool loadThumbnail = true)
    {
        if (DeckMonitorCatalog.TryGet(mapping?.DeckMonitor, out var monitor))
            return new DeckMonitorView(monitor);
        if (HasRegisteredFile(mapping) && !File.Exists(mapping!.DeckFilePath))
            return CreateMissingFileIcon(22);
        var configuredIcon = DeckIconCatalog.CreateVisual(mapping, 22);
        if (configuredIcon != null)
            return configuredIcon;
        if (HasRegisteredFile(mapping))
        {
            bool video = IsVideoFile(mapping!.DeckFilePath);
            var thumbnail = loadThumbnail ? (video ? LoadVideoThumbnail(mapping.DeckFilePath, 96, 54) : LoadFileThumbnail(mapping.DeckFilePath, 96)) : null;
            if (thumbnail != null)
            {
                var image = new System.Windows.Controls.Image { Source = thumbnail, Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(4), IsHitTestVisible = false };
                if (!video)
                    return image;
                var root = new Grid { ClipToBounds = true, IsHitTestVisible = false };
                root.SizeChanged += (_, _) => root.Clip = new RectangleGeometry(new Rect(root.RenderSize), 10, 10);
                root.Children.Add(image);
                var badge = new Border
                {
                    Width = 18,
                    Height = 18,
                    CornerRadius = new CornerRadius(9),
                    Background = new SolidColorBrush(WpfColor.FromArgb(202, 10, 16, 20)),
                    BorderBrush = new SolidColorBrush(WpfColor.FromArgb(168, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 6,4 L 14,9 L 6,14 Z"), Fill = System.Windows.Media.Brushes.White, Stretch = Stretch.Uniform, Margin = new Thickness(5, 4, 4, 4) }
                };
                root.Children.Add(badge);
                return root;
            }
            return CreateFileIcon(mapping.DeckFilePath, IsAudioFile(mapping.DeckFilePath) ? 20 : 18);
        }
        return new TextBlock
        {
            Text = ActionLabel(input, mapping),
            FontSize = 10,
            FontWeight = FontWeights.Medium,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
    }

    internal static FrameworkElement CreateMissingFileIcon(double size)
    {
        var root = new Grid { Width = size, Height = size, IsHitTestVisible = false };
        var link = new TextBlock
        {
            Text = "\uE71B",
            FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = size * .9,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = .72
        };
        link.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(System.Windows.Controls.Button), 1) });
        root.Children.Add(link);
        root.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 3,3 L 19,19"),
            Stroke = ThemeService.Brush("DangerBrush"),
            StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(2)
        });
        return root;
    }

    internal static System.Windows.Controls.ToolTip CreateMissingFileToolTip() => new()
    {
        Content = new TextBlock
        {
            Text = "参照先のファイルが削除されたか、移動された可能性があります。",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 280,
            FontSize = 12,
            Foreground = ThemeService.Brush("PrimaryText")
        },
        Padding = new Thickness(10, 7, 10, 7),
        BorderThickness = new Thickness(1),
        Background = ThemeService.Brush("CardBackground"),
        BorderBrush = ThemeService.Brush("DangerBrush"),
        Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse
    };

    [Flags]
    enum ShellImageFlags
    {
        BiggerSizeOk = 0x1,
        ThumbnailOnly = 0x8
    }

    [StructLayout(LayoutKind.Sequential)]
    readonly struct NativeSize
    {
        internal NativeSize(int width, int height)
        {
            Width = width;
            Height = height;
        }
        internal readonly int Width;
        internal readonly int Height;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, ShellImageFlags flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory shellItem);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DeleteObject(IntPtr objectHandle);
}
