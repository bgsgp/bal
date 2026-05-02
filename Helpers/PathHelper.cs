using System;
using System.IO;

namespace BlueArchiveLottery.Helpers;

public static class PathHelper
{
    public static string BasePath => AppContext.BaseDirectory;
    public static string ResourcesPath => Path.Combine(BasePath, "resources");

    public static string GetVideoFolder(int starLevel)
    {
        return starLevel == 3 ? "special" : "simple";
    }
}