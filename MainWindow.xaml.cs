using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using BlueArchiveLottery.Helpers;
using BlueArchiveLottery.Models;
using BlueArchiveLottery.Services;
using Microsoft.Win32;

namespace BlueArchiveLottery;

public partial class MainWindow : Window
{
    private List<Level> _levels = new();
    private bool _fullMode = false;
    private NormalLotteryEngine? _normalEngine;
    private SilentLotteryEngine? _silentEngine;
    private VideoPlayerWindow? _videoWindow;
    private LotteryResult? _cachedResult;

    private readonly MediaElement _bgmPlayer = new()
    {
        LoadedBehavior = MediaState.Manual,
        UnloadedBehavior = MediaState.Stop
    };

    public MainWindow()
    {
        InitializeComponent();
        var host = new Grid { Visibility = Visibility.Collapsed };
        host.Children.Add(_bgmPlayer);
        ((Panel)Content).Children.Add(host);

        LoadLevels();
        ApplyTheme(ThemeService.GetSystemTheme());
        SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
                Dispatcher.BeginInvoke(() => ApplyTheme(ThemeService.GetSystemTheme()));
        };
    }

    private void LoadLevels()
    {
        string path = Path.Combine(PathHelper.ResourcesPath, "obj.json");
        if (!File.Exists(path))
        {
            var defaultConfig = new LotteryConfig
            {
                levels = new()
                {
                    new() { star = 1, probability = 78.5, items = new(){"获得100金币","获得经验值+50","跳过一次作业"} },
                    new() { star = 2, probability = 18.5, items = new(){"打扫教室卫生","写额外数学作业"} },
                    new() { star = 3, probability = 3.0, items = new(){"操场跑圈5圈","背完整本语文课本重点"} }
                }
            };
            File.WriteAllText(path, JsonSerializer.Serialize(defaultConfig,
                new JsonSerializerOptions { WriteIndented = true }));
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<LotteryConfig>(json);
            if (config?.levels != null)
            {
                _levels = config.levels;
                double total = 0;
                foreach (var l in _levels) total += l.probability;
                if (Math.Abs(total - 100) > 0.1)
                    MessageBox.Show($"概率总和为{total:F1}%（建议100%），请检查配置文件！", "提示");
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载配置失败：{ex.Message}\n使用默认配置", "警告");
        }
    }

    private void ApplyTheme(AppTheme theme)
    {
        bool isDark = theme == AppTheme.Dark;
        string p, s, a, bg, t, w, sha, r, pun;
        if (isDark)
        {
            p = ThemeService.DarkColors.Primary;
            s = ThemeService.DarkColors.Secondary;
            a = ThemeService.DarkColors.Accent;
            bg = ThemeService.DarkColors.Background;
            t = ThemeService.DarkColors.Text;
            w = ThemeService.DarkColors.White;
            sha = ThemeService.DarkColors.Shadow;
            r = ThemeService.DarkColors.Reward;
            pun = ThemeService.DarkColors.Punishment;
        }
        else
        {
            p = ThemeService.LightColors.Primary;
            s = ThemeService.LightColors.Secondary;
            a = ThemeService.LightColors.Accent;
            bg = ThemeService.LightColors.Background;
            t = ThemeService.LightColors.Text;
            w = ThemeService.LightColors.White;
            sha = ThemeService.LightColors.Shadow;
            r = ThemeService.LightColors.Reward;
            pun = ThemeService.LightColors.Punishment;
        }

        void SetBrush(string key, string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            App.Current.Resources[key] = new SolidColorBrush(color);
        }

        SetBrush("PrimaryBrush", p);
        SetBrush("SecondaryBrush", s);
        SetBrush("AccentBrush", a);
        SetBrush("BackgroundBrush", bg);
        SetBrush("TextBrush", t);
        SetBrush("WhiteBrush", w);
        SetBrush("ShadowBrush", sha);
        SetBrush("RewardBrush", r);
        SetBrush("PunishmentBrush", pun);
    }

    public void SetFullMode(bool enabled)
    {
        if (_fullMode == enabled) return;
        _fullMode = enabled;
        ModeLabel.Text = enabled ? "当前模式：完全模式" : "当前模式：普通模式";
        if (enabled) StartBGM(); else StopBGM();
    }

    private void StartBGM()
    {
        string[] candidates = {
            Path.Combine(PathHelper.ResourcesPath, "music", "bgm.m4s"),
            Path.Combine(PathHelper.ResourcesPath, "music", "bgm.mp3")
        };
        string? bgm = candidates.FirstOrDefault(File.Exists);
        if (bgm == null) return;
        _bgmPlayer.Source = new Uri(bgm);
        _bgmPlayer.MediaEnded += BgmLoop;
        _bgmPlayer.Play();
    }

    private void StopBGM()
    {
        _bgmPlayer.Stop();
        _bgmPlayer.MediaEnded -= BgmLoop;
    }

    private void BgmLoop(object sender, RoutedEventArgs e)
    {
        _bgmPlayer.Position = TimeSpan.Zero;
        _bgmPlayer.Play();
    }

    private void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        if (_levels.Count == 0 || _levels.Sum(l => l.items.Count) == 0)
        {
            MessageBox.Show("没有可抽选的项目！");
            return;
        }
        BtnStart.IsEnabled = false;

        if (_fullMode) StartSilentLottery();
        else StartNormalLottery();
    }

    private void StartSilentLottery()
    {
        ResultDisplay.Text = "正在抽取结果...";
        _silentEngine = new SilentLotteryEngine(_levels);
        _silentEngine.ResultReady += OnSilentResult;
        _silentEngine.ErrorOccurred += OnLotteryError;
        _ = _silentEngine.RunAsync();
    }

    private void StartNormalLottery()
    {
        // 不再需要启用停止按钮
        _normalEngine = new NormalLotteryEngine(_levels);
        _normalEngine.UpdateResult += text => ResultDisplay.Text = text;
        _normalEngine.ResultReady += OnNormalResult;
        _normalEngine.ErrorOccurred += OnLotteryError;
        _normalEngine.Start();
    }

    private void OnSilentResult(LotteryResult result)
    {
        _cachedResult = result;
        PlayVideo(result.Star);
    }

    private void OnNormalResult(LotteryResult result)
    {
        Dispatcher.Invoke(() =>
        {
            ShowResultWithGoldStars(result);
            BtnStart.IsEnabled = true;
        });
    }

    private void OnLotteryError(string msg)
    {
        Dispatcher.Invoke(() =>
        {
            MessageBox.Show(msg, "错误");
            BtnStart.IsEnabled = true;
        });
    }

    private void PlayVideo(int starLevel)
    {
        _videoWindow?.Close();
        _videoWindow = new VideoPlayerWindow(starLevel);
        _videoWindow.VideoEnded += () =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_cachedResult != null)
                {
                    ShowResultWithGoldStars(_cachedResult);
                }
                BtnStart.IsEnabled = true;
            });
        };
        _videoWindow.Show();
    }

    private void ShowResultWithGoldStars(LotteryResult result)
    {
        ResultDisplay.Inlines.Clear();

        Brush textBrush = (Brush)App.Current.Resources["TextBrush"] ?? Brushes.White;
        Brush goldBrush = Brushes.Gold;

        int extraStars = Math.Max(0, result.Star - 1);
        string starsStr = new string('⭐', extraStars);

        ResultDisplay.Inlines.Add(new Run($"抽选结果：{result.Item} (⭐") { Foreground = textBrush });
        ResultDisplay.Inlines.Add(new Run(starsStr) { Foreground = goldBrush });
        ResultDisplay.Inlines.Add(new Run(")") { Foreground = textBrush });
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsDialog(_fullMode) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            SetFullMode(dlg.FullMode);
            LoadLevels();
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        string imgPath = Path.Combine(PathHelper.ResourcesPath, "img", "title.png");
        if (!File.Exists(imgPath))
            TitleFallbackText.Visibility = Visibility.Visible;
        ModeLabel.Text = _fullMode ? "当前模式：完全模式" : "当前模式：普通模式";
    }

    protected override void OnClosed(EventArgs e)
    {
        StopBGM();
        base.OnClosed(e);
    }
}