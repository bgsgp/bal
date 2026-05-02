using System.Windows;   // 提供 SystemParameters
using Microsoft.Win32;

namespace BlueArchiveLottery.Services;

public enum AppTheme { Light, Dark }

public static class ThemeService
{
    public static AppTheme GetSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key != null)
            {
                var value = key.GetValue("AppsUseLightTheme");
                if (value is int useLight && useLight == 0)
                    return AppTheme.Dark;
            }
        }
        catch { }
        try
        {
            // SystemParameters.WindowGlassColor 返回 System.Windows.Media.Color
            var c = SystemParameters.WindowGlassColor;
            // 自行计算亮度（公式：0.299*R + 0.587*G + 0.114*B）
            double brightness = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            if (brightness < 128) return AppTheme.Dark;
        }
        catch { }
        return AppTheme.Light;
    }

    public static class LightColors
    {
        public const string Primary = "#87CEEB";
        public const string Secondary = "#FFB6C1";
        public const string Accent = "#98FB98";
        public const string Background = "#F0F8FF";
        public const string Text = "#4682B4";
        public const string White = "#FFFFFF";
        public const string Shadow = "#ADD8E6";
        public const string Reward = "#228B22";
        public const string Punishment = "#DC143C";
    }

    public static class DarkColors
    {
        public const string Primary = "#4682B4";
        public const string Secondary = "#FF69B4";
        public const string Accent = "#6B8E23";
        public const string Background = "#191928";
        public const string Text = "#F0F8FF";
        public const string White = "#2D2D41";
        public const string Shadow = "#323246";
        public const string Reward = "#90EE90";
        public const string Punishment = "#FF6A6A";
    }
}