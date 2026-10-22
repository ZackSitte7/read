using System.Drawing;
using System.Windows.Forms;

namespace Replica.Desktop;

/// <summary>
/// 原软件 DFM 中的坐标按 144 DPI 设计；复刻版统一按 96 DPI 设计，
/// 再交给 WinForms 的 Dpi 缩放，因此所有尺寸除以 1.5。
/// 颜色按 Delphi TColor($00BBGGRR) 还原为 .NET RGB。
/// </summary>
internal static class Theme
{
    public const double Scale = 1.5d;

    public static readonly Color FormBack = Delphi(15718603);        // #CBD8EF，TYYWin.Color
    public static readonly Color MenuBack = Color.FromArgb(240, 240, 240);
    public static readonly Color MoneyGreen = Color.FromArgb(192, 220, 192);
    public static readonly Color SkyBlue = Color.FromArgb(166, 202, 240);
    public static readonly Color GridBack = Delphi(10790052);        // #A4A4A4
    public static readonly Color DataBack = Delphi(8815234);         // #828286
    public static readonly Color ChangeBack = Delphi(52309);         // #55CC00
    public static readonly Color RedBack = Delphi(7895260);          // #DC7878
    public static readonly Color CashBack = Delphi(16744703);        // #FF80FF
    public static readonly Color ModeBack = Delphi(12615935);        // #FF80C0

    public static int S(double value) => (int)Math.Round(value / Scale);

    public static Color Delphi(int value) =>
        Color.FromArgb(value & 0xFF, (value >> 8) & 0xFF, (value >> 16) & 0xFF);

    /// <summary>按 DFM 中的 Font.Height（负像素）换算为 pt。</summary>
    public static Font F(double dfmPixelHeight, bool bold = false, string family = "新宋体")
    {
        var points = (float)(dfmPixelHeight / Scale * 0.75d);
        return new Font(
            family,
            Math.Max(5f, points),
            bold ? FontStyle.Bold : FontStyle.Regular,
            GraphicsUnit.Point);
    }

    public static Label MakeLabel(
        Control parent,
        string name,
        int x,
        int y,
        int width,
        int height,
        string text,
        Font font,
        Color fore,
        Color? back = null,
        ContentAlignment align = ContentAlignment.MiddleLeft)
    {
        var label = new Label
        {
            Name = name,
            AutoSize = false,
            Left = x,
            Top = y,
            Width = width,
            Height = height,
            Text = text,
            Font = font,
            ForeColor = fore,
            BackColor = back ?? Color.Transparent,
            TextAlign = align,
            UseMnemonic = false,
        };

        parent.Controls.Add(label);
        return label;
    }

    public static Button MakeButton(
        Control parent,
        string name,
        int x,
        int y,
        int width,
        int height,
        string text,
        Font font,
        Color fore,
        Color? back = null)
    {
        var button = new Button
        {
            Name = name,
            Left = x,
            Top = y,
            Width = width,
            Height = height,
            Text = text,
            Font = font,
            ForeColor = fore,
            BackColor = back ?? Color.Transparent,
            FlatStyle = FlatStyle.System,
            TabStop = false,
        };

        parent.Controls.Add(button);
        return button;
    }

    public static TextBox MakeEdit(
        Control parent,
        string name,
        int x,
        int y,
        int width,
        int height,
        string text,
        Font font,
        Color fore,
        Color? back = null,
        ContentAlignment align = ContentAlignment.MiddleLeft)
    {
        var edit = new TextBox
        {
            Name = name,
            Left = x,
            Top = y,
            Width = width,
            Height = height,
            Text = text,
            Font = font,
            ForeColor = fore,
            BackColor = back ?? Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = align switch
            {
                ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight =>
                    HorizontalAlignment.Right,
                ContentAlignment.MiddleCenter or ContentAlignment.TopCenter or ContentAlignment.BottomCenter =>
                    HorizontalAlignment.Center,
                _ => HorizontalAlignment.Left,
            },
        };

        parent.Controls.Add(edit);
        return edit;
    }

    /// <summary>原软件的开关是带 √ / 空格的 TLabel，这里保持同样的表现。</summary>
    public static string ToggleText(bool on, string caption) => (on ? "√ " : "   ") + caption;

    public static bool ToggleFromText(string? text) => text?.StartsWith('√') == true;
}
