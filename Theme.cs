using System.Runtime.InteropServices;

namespace AudioClipExtractor;

public static class Theme
{
    public static bool IsDark { get; private set; }

    public static Color Window;
    public static Color Card;
    public static Color CardAlt;
    public static Color TextPrimary;
    public static Color TextSecondary;
    public static Color TextMuted;
    public static Color Accent;
    public static Color AccentHover;
    public static Color AccentPressed;
    public static Color AccentSoft;
    public static Color Border;
    public static Color TrackBg;
    public static Color Danger;
    public static Color Success;

    public static Font FontUI = null!;
    public static Font FontTitle = null!;
    public static Font FontMono = null!;

    public static event Action? Changed;

    static Theme()
    {
        FontUI    = new Font("Microsoft YaHei UI", 9F,  FontStyle.Regular, GraphicsUnit.Point);
        FontTitle = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold,    GraphicsUnit.Point);
        FontMono  = new Font("Consolas",           12F, FontStyle.Regular, GraphicsUnit.Point);
        Apply(false);
    }

    public static void Toggle() => Apply(!IsDark);

    public static void Apply(bool dark)
    {
        IsDark = dark;
        if (dark)
        {
            Window        = Color.FromArgb(24, 24, 28);
            Card          = Color.FromArgb(34, 34, 40);
            CardAlt       = Color.FromArgb(44, 44, 52);
            TextPrimary   = Color.FromArgb(240, 240, 245);
            TextSecondary = Color.FromArgb(176, 176, 186);
            TextMuted     = Color.FromArgb(128, 128, 138);
            Accent        = Color.FromArgb(96, 132, 255);
            AccentHover   = Color.FromArgb(118, 152, 255);
            AccentPressed = Color.FromArgb(76, 108, 224);
            AccentSoft    = Color.FromArgb(48, 64, 120);
            Border        = Color.FromArgb(56, 56, 66);
            TrackBg       = Color.FromArgb(60, 60, 70);
            Danger        = Color.FromArgb(232, 96, 96);
            Success       = Color.FromArgb(88, 196, 132);
        }
        else
        {
            Window        = Color.FromArgb(244, 245, 248);
            Card          = Color.White;
            CardAlt       = Color.FromArgb(248, 249, 252);
            TextPrimary   = Color.FromArgb(28, 30, 36);
            TextSecondary = Color.FromArgb(96, 100, 112);
            TextMuted     = Color.FromArgb(148, 152, 164);
            Accent        = Color.FromArgb(64, 108, 240);
            AccentHover   = Color.FromArgb(86, 128, 250);
            AccentPressed = Color.FromArgb(48, 88, 214);
            AccentSoft    = Color.FromArgb(224, 232, 255);
            Border        = Color.FromArgb(226, 228, 234);
            TrackBg       = Color.FromArgb(232, 234, 240);
            Danger        = Color.FromArgb(220, 72, 72);
            Success       = Color.FromArgb(40, 168, 100);
        }
        Changed?.Invoke();
    }
}

public static class Native
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    public static void ApplyWindowStyle(IntPtr hwnd, bool dark)
    {
        try
        {
            int darkVal = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkVal, sizeof(int));
            int round = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        }
        catch { }
    }
}