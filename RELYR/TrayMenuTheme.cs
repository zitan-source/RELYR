using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace RELYR;

/// <summary>通知領域メニューをアプリのダーク・ライト配色へ統一します。</summary>
internal static class TrayMenuTheme
{
    static readonly ConditionalWeakTable<ToolStrip, object> roundedStrips = new();

    internal static ContextMenuStrip Create(bool dark)
    {
        var menu = new ContextMenuStrip
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Regular, GraphicsUnit.Point),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Padding = new Padding(8),
            MinimumSize = new Size(230, 0)
        };
        Apply(menu, dark);
        return menu;
    }

    internal static void Apply(ToolStrip strip, bool dark)
    {
        var palette = new Palette(dark);
        strip.Renderer = new TrayRenderer(palette);
        strip.BackColor = palette.Background;
        strip.ForeColor = palette.Foreground;
        AttachRoundedCorners(strip);
        foreach (ToolStripItem item in strip.Items)
        {
            item.BackColor = palette.Background;
            item.ForeColor = palette.Foreground;
            if (item is ToolStripMenuItem menuItem)
            {
                menuItem.Padding = new Padding(12, 8, 12, 8);
                menuItem.Margin = new Padding(1, 2, 1, 2);
                if (menuItem.HasDropDownItems)
                    Apply(menuItem.DropDown, dark);
            }
        }
    }

    static void AttachRoundedCorners(ToolStrip strip)
    {
        if (roundedStrips.TryGetValue(strip, out _))
            return;
        roundedStrips.Add(strip, new object());
        strip.HandleCreated += (_, _) => UpdateRoundedRegion(strip);
        strip.SizeChanged += (_, _) => UpdateRoundedRegion(strip);
        strip.Layout += (_, _) => UpdateRoundedRegion(strip);
        UpdateRoundedRegion(strip);
    }

    static void UpdateRoundedRegion(ToolStrip strip)
    {
        if (strip.Width < 2 || strip.Height < 2)
            return;
        using var path = RoundedRectangle(new Rectangle(0, 0, strip.Width, strip.Height), 14);
        var previous = strip.Region;
        strip.Region = new Region(path);
        previous?.Dispose();
    }

    static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    sealed class TrayRenderer(Palette palette) : ToolStripProfessionalRenderer(new TrayColorTable(palette))
    {
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed)
                return;
            var bounds = new Rectangle(2, 1, Math.Max(1, e.Item.Width - 4), Math.Max(1, e.Item.Height - 2));
            using var path = RoundedRectangle(bounds, 8);
            using var brush = new SolidBrush(e.Item.Pressed ? palette.Pressed : palette.Hover);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using var pen = new Pen(palette.Separator);
            e.Graphics.DrawLine(pen, 12, y, Math.Max(12, e.Item.Width - 12), y);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var bounds = new Rectangle(0, 0, Math.Max(1, e.ToolStrip.Width - 1), Math.Max(1, e.ToolStrip.Height - 1));
            using var path = RoundedRectangle(bounds, 14);
            using var pen = new Pen(palette.Border);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, path);
        }
    }

    sealed class TrayColorTable(Palette palette) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => palette.Background;
        public override Color ImageMarginGradientBegin => palette.Background;
        public override Color ImageMarginGradientMiddle => palette.Background;
        public override Color ImageMarginGradientEnd => palette.Background;
        public override Color MenuItemSelected => palette.Hover;
        public override Color MenuItemBorder => palette.Accent;
        public override Color MenuItemSelectedGradientBegin => palette.Hover;
        public override Color MenuItemSelectedGradientEnd => palette.Hover;
        public override Color MenuItemPressedGradientBegin => palette.Pressed;
        public override Color MenuItemPressedGradientMiddle => palette.Pressed;
        public override Color MenuItemPressedGradientEnd => palette.Pressed;
        public override Color SeparatorDark => palette.Separator;
        public override Color SeparatorLight => palette.Separator;
        public override Color ToolStripBorder => palette.Border;
        public override Color CheckBackground => palette.AccentSoft;
        public override Color CheckSelectedBackground => palette.AccentSoft;
        public override Color CheckPressedBackground => palette.AccentSoft;
    }

    sealed class Palette
    {
        internal Palette(bool dark)
        {
            Background = dark ? Color.FromArgb(30, 30, 31) : Color.White;
            Foreground = dark ? Color.FromArgb(232, 236, 244) : Color.FromArgb(23, 34, 49);
            Hover = dark ? Color.FromArgb(47, 47, 49) : Color.FromArgb(231, 243, 240);
            Pressed = dark ? Color.FromArgb(42, 65, 61) : Color.FromArgb(207, 233, 227);
            Border = dark ? Color.FromArgb(67, 67, 70) : Color.FromArgb(184, 197, 212);
            Separator = dark ? Color.FromArgb(52, 52, 55) : Color.FromArgb(226, 229, 233);
            Accent = dark ? Color.FromArgb(114, 224, 193) : Color.FromArgb(8, 123, 105);
            AccentSoft = dark ? Color.FromArgb(36, 75, 74) : Color.FromArgb(221, 242, 237);
        }

        internal Color Background
        {
            get;
        }
        internal Color Foreground
        {
            get;
        }
        internal Color Hover
        {
            get;
        }
        internal Color Pressed
        {
            get;
        }
        internal Color Border
        {
            get;
        }
        internal Color Separator
        {
            get;
        }
        internal Color Accent
        {
            get;
        }
        internal Color AccentSoft
        {
            get;
        }
    }
}
