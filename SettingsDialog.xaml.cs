using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using BlueArchiveLottery.Helpers;

namespace BlueArchiveLottery;

public partial class SettingsDialog : Window
{
    public bool FullMode { get; private set; }

    public SettingsDialog(bool currentFullMode)
    {
        InitializeComponent();
        FullMode = currentFullMode;
        FullModeCheckBox.IsChecked = FullMode;
        string configPath = Path.Combine(PathHelper.ResourcesPath, "obj.json");
        ConfigPathLabel.Text = $"配置文件路径：\n{configPath}";

        if (!File.Exists(configPath))
            CreateDefaultConfig(configPath);
    }

    private void CreateDefaultConfig(string path)
    {
        var json = @"{
  ""levels"": [
    { ""star"": 1, ""probability"": 78.5, ""items"": [""获得100金币"",""获得经验值+50"",""跳过一次作业""] },
    { ""star"": 2, ""probability"": 18.5, ""items"": [""打扫教室卫生"",""写额外数学作业""] },
    { ""star"": 3, ""probability"": 3.0, ""items"": [""操场跑圈5圈"",""背完整本语文课本重点""] }
  ]
}";
        File.WriteAllText(path, json);
        MessageBox.Show("已创建默认配置文件！", "提示");
    }

    private void EditConfig_Click(object sender, RoutedEventArgs e)
    {
        string configPath = Path.Combine(PathHelper.ResourcesPath, "obj.json");
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = configPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开文件失败：{ex.Message}\n请手动打开：{configPath}", "错误");
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        FullMode = FullModeCheckBox.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}