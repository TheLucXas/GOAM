namespace Goam.App;

/// <summary>Colours and small styling helpers (dark theme, same palette as GOAM 1.x).</summary>
internal static class Theme
{
    public static readonly Color Window = Color.FromArgb(0x24, 0x24, 0x24);
    public static readonly Color Panel = Color.FromArgb(0x2B, 0x2B, 0x2B);
    public static readonly Color PanelAlt = Color.FromArgb(0x33, 0x33, 0x33);
    public static readonly Color Text = Color.FromArgb(0xEE, 0xEE, 0xEE);
    public static readonly Color TextMuted = Color.FromArgb(0x99, 0x99, 0x99);

    public static readonly Color Green = Color.FromArgb(0x2C, 0x5F, 0x2E);
    public static readonly Color GreenDark = Color.FromArgb(0x1A, 0x5C, 0x1A);
    public static readonly Color GreenRow = Color.FromArgb(0x1A, 0x3A, 0x1A);
    public static readonly Color GreenText = Color.FromArgb(0x4A, 0xDE, 0x80);
    public static readonly Color Red = Color.FromArgb(0xC4, 0x2B, 0x2B);
    public static readonly Color RedDark = Color.FromArgb(0x5C, 0x1A, 0x1A);
    public static readonly Color RedText = Color.FromArgb(0xF8, 0x71, 0x71);
    public static readonly Color Blue = Color.FromArgb(0x1A, 0x3A, 0x6E);
    public static readonly Color BlueLight = Color.FromArgb(0x2A, 0x4A, 0x7A);
    public static readonly Color Neutral = Color.FromArgb(0x3A, 0x3A, 0x3A);

    public static Font Ui(float size = 9.5f, FontStyle style = FontStyle.Regular) =>
        new("Segoe UI", size, style, GraphicsUnit.Point);

    public static Button Button(string text, Color back, int width = 120, int height = 32, float fontSize = 9.5f)
    {
        var b = new Button
        {
            Text = text,
            Width = width,
            Height = height,
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = Text,
            Font = Ui(fontSize),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            UseMnemonic = false, // "Download & Convert" must show the ampersand
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.15f);
        b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(back, 0.1f);
        return b;
    }

    public static Label Label(string text, float size = 9.5f, FontStyle style = FontStyle.Regular, Color? color = null) =>
        new()
        {
            Text = text,
            AutoSize = true,
            Font = Ui(size, style),
            ForeColor = color ?? Text,
            BackColor = Color.Transparent,
            UseMnemonic = false,
        };

    public static void Style(Form form)
    {
        // Layouts are positioned in 96-dpi pixels; let WinForms scale them to the monitor (PerMonitorV2).
        form.AutoScaleDimensions = new SizeF(96F, 96F);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.BackColor = Window;
        form.ForeColor = Text;
        form.Font = Ui();
    }
}
