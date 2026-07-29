using System.Drawing;
using System.Windows.Forms;

namespace RELYR;

/// <summary>通知領域メニューをアプリのダーク・ライト配色へ統一します。</summary>
internal static class TrayMenuTheme
{
    internal static ContextMenuStrip Create(bool dark)
    {
        var menu=new ContextMenuStrip
        {
            AutoSize=true,
            Font=new Font("Segoe UI",10F,FontStyle.Regular,GraphicsUnit.Point),
            ShowImageMargin=false,
            ShowCheckMargin=true,
            Padding=new Padding(6),
            MinimumSize=new Size(230,0)
        };
        Apply(menu,dark);
        return menu;
    }

    internal static void Apply(ToolStrip strip,bool dark)
    {
        var palette=new Palette(dark);
        strip.Renderer=new ToolStripProfessionalRenderer(new TrayColorTable(palette)){RoundedEdges=true};
        strip.BackColor=palette.Background;
        strip.ForeColor=palette.Foreground;
        foreach(ToolStripItem item in strip.Items)
        {
            item.BackColor=palette.Background;
            item.ForeColor=palette.Foreground;
            if(item is ToolStripMenuItem menuItem)
            {
                menuItem.Padding=new Padding(10,7,10,7);
                menuItem.Margin=new Padding(1);
                if(menuItem.HasDropDownItems)Apply(menuItem.DropDown,dark);
            }
        }
    }

    sealed class TrayColorTable(Palette palette):ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground=>palette.Background;
        public override Color ImageMarginGradientBegin=>palette.Background;
        public override Color ImageMarginGradientMiddle=>palette.Background;
        public override Color ImageMarginGradientEnd=>palette.Background;
        public override Color MenuItemSelected=>palette.Hover;
        public override Color MenuItemBorder=>palette.Accent;
        public override Color MenuItemSelectedGradientBegin=>palette.Hover;
        public override Color MenuItemSelectedGradientEnd=>palette.Hover;
        public override Color MenuItemPressedGradientBegin=>palette.Pressed;
        public override Color MenuItemPressedGradientMiddle=>palette.Pressed;
        public override Color MenuItemPressedGradientEnd=>palette.Pressed;
        public override Color SeparatorDark=>palette.Border;
        public override Color SeparatorLight=>palette.Border;
        public override Color ToolStripBorder=>palette.Border;
        public override Color CheckBackground=>palette.AccentSoft;
        public override Color CheckSelectedBackground=>palette.AccentSoft;
        public override Color CheckPressedBackground=>palette.AccentSoft;
    }

    sealed class Palette
    {
        internal Palette(bool dark)
        {
            Background=dark?Color.FromArgb(29,35,48):Color.White;
            Foreground=dark?Color.FromArgb(232,236,244):Color.FromArgb(23,34,49);
            Hover=dark?Color.FromArgb(53,65,88):Color.FromArgb(231,243,240);
            Pressed=dark?Color.FromArgb(36,75,74):Color.FromArgb(207,233,227);
            Border=dark?Color.FromArgb(70,81,104):Color.FromArgb(184,197,212);
            Accent=dark?Color.FromArgb(114,224,193):Color.FromArgb(8,123,105);
            AccentSoft=dark?Color.FromArgb(36,75,74):Color.FromArgb(221,242,237);
        }

        internal Color Background { get; }
        internal Color Foreground { get; }
        internal Color Hover { get; }
        internal Color Pressed { get; }
        internal Color Border { get; }
        internal Color Accent { get; }
        internal Color AccentSoft { get; }
    }
}
