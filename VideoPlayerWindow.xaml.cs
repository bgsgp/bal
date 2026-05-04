using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using BlueArchiveLottery.Helpers;

namespace BlueArchiveLottery;

public partial class VideoPlayerWindow : Window
{
    private readonly int _starLevel;
    private readonly string _videoFolder;
    private string _currentPhase = "start";
    private bool _skipPlayed = false;
    private readonly DispatcherTimer _drawStopTimer;
    private Point _pressPoint;

    public event Action? VideoEnded;

    public VideoPlayerWindow(int starLevel)
    {
        InitializeComponent();
        _starLevel = starLevel;

        // 基础视频文件夹（simple 或 special）
        string baseVideoFolder = Path.Combine(PathHelper.ResourcesPath, PathHelper.GetVideoFolder(starLevel));

        // 根据星级选择不同的角色文件夹逻辑
        if (starLevel == 3)
        {
            // 三星：按概率随机选择 A.R.O.N.A、Plana、Both、Change
            _videoFolder = ChooseSpecialCharacterFolder(baseVideoFolder);
        }
        else
        {
            // 普通星：随机选择 A.R.O.N.A 或 Plana（各 50%）
            var rnd = new Random();
            string characterFolder = rnd.Next(2) == 0 ? "A.R.O.N.A" : "Plana";
            _videoFolder = Path.Combine(baseVideoFolder, characterFolder);
            // 若随机到的文件夹不存在，则回退到另一个
            if (!Directory.Exists(_videoFolder))
            {
                string fallback = characterFolder == "A.R.O.N.A" ? "Plana" : "A.R.O.N.A";
                _videoFolder = Path.Combine(baseVideoFolder, fallback);
            }
        }

        _drawStopTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _drawStopTimer.Tick += (s, e) => { _drawStopTimer.Stop(); PlayOpenVideo(); };

        PlayStartVideo();
    }

    /// <summary>
    /// 三星时按概率选取角色文件夹：0.4 A.R.O.N.A、0.4 Plana、0.15 Both、0.05 Change
    /// </summary>
    private static string ChooseSpecialCharacterFolder(string basePath)
    {
        var rnd = new Random();
        double roll = rnd.NextDouble(); // 0.0 ~ 1.0

        string[] candidates = ["A.R.O.N.A", "Plana", "Both", "Change"];
        double[] probabilities = [0.4, 0.4, 0.15, 0.05];

        double cumulative = 0;
        string selected = candidates[0]; // 默认为 A.R.O.N.A

        for (int i = 0; i < candidates.Length; i++)
        {
            cumulative += probabilities[i];
            if (roll < cumulative)
            {
                selected = candidates[i];
                break;
            }
        }

        string fullPath = Path.Combine(basePath, selected);

        // 若所选文件夹不存在，按顺序回退到第一个存在的文件夹
        if (!Directory.Exists(fullPath))
        {
            foreach (var folder in candidates)
            {
                string fallbackPath = Path.Combine(basePath, folder);
                if (Directory.Exists(fallbackPath))
                {
                    fullPath = fallbackPath;
                    break;
                }
            }
            // 如果都不存在，保持原 fullPath，后续播放时会报错（可接受）
        }

        return fullPath;
    }

    private void PlayStartVideo()
    {
        _currentPhase = "start";
        _skipPlayed = false;
        string path = Path.Combine(_videoFolder, "Start.mp4");
        if (File.Exists(path))
        {
            PlayVideoFile(path);
            SkipButton.Visibility = Visibility.Visible;
        }
        else ShowSignatureCanvas();
    }

    private void PlaySkipVideo()
    {
        if (_skipPlayed) return;
        _skipPlayed = true;
        _currentPhase = "skip";
        SkipButton.Visibility = Visibility.Collapsed;
        SignatureCanvas.Visibility = Visibility.Collapsed;
        _drawStopTimer.Stop();
        SignatureCanvas.Strokes.Clear();

        string path = Path.Combine(_videoFolder, "Skip.mp4");
        if (File.Exists(path)) PlayVideoFile(path);
        else Task.Delay(1500).ContinueWith(_ => Dispatcher.Invoke(PlayOpenVideo));
    }

    private void PlayOpenVideo()
    {
        _currentPhase = "open";
        SkipButton.Visibility = Visibility.Collapsed;
        SignatureCanvas.Visibility = Visibility.Collapsed;
        _drawStopTimer.Stop();
        SignatureCanvas.Strokes.Clear();

        string path = Path.Combine(_videoFolder, "Open.mp4");
        if (File.Exists(path)) PlayVideoFile(path);
        else CloseWithEnded();
    }

    private void PlayVideoFile(string path)
    {
        VideoPlayer.Stop();
        VideoPlayer.Source = new Uri(path);
        VideoPlayer.Play();
    }

    private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (_currentPhase == "start")
        {
            VideoPlayer.Pause();
            ShowSignatureCanvas();
        }
        else if (_currentPhase == "skip")
            Task.Delay(500).ContinueWith(_ => Dispatcher.Invoke(PlayOpenVideo));
        else
            CloseWithEnded();
    }

    private void VideoPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (_currentPhase == "start") ShowSignatureCanvas();
        else if (_currentPhase == "skip") Task.Delay(1500).ContinueWith(_ => Dispatcher.Invoke(PlayOpenVideo));
        else CloseWithEnded();
    }

    private void ShowSignatureCanvas()
    {
        SignatureCanvas.Visibility = Visibility.Visible;
        _drawStopTimer.Start();
    }

    private void SignatureCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressPoint = e.GetPosition(SignatureCanvas);
        _drawStopTimer.Stop();
    }

    private void SignatureCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && SignatureCanvas.Visibility == Visibility.Visible)
        {
            _drawStopTimer.Stop();
            _drawStopTimer.Start();
        }
    }

    private void SignatureCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        Point upPoint = e.GetPosition(SignatureCanvas);
        double dist = (upPoint - _pressPoint).Length;
        if (dist < 5)
        {
            if (SignatureCanvas.Strokes.Count > 0)
            {
                var last = SignatureCanvas.Strokes.Last();
                if (last.StylusPoints.Count <= 1)
                    SignatureCanvas.Strokes.Remove(last);
            }
            PlaySkipVideo();
            return;
        }
        _drawStopTimer.Start();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e) => PlaySkipVideo();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) CloseWithEnded();
    }

    private void CloseWithEnded()
    {
        VideoPlayer.Stop();
        VideoEnded?.Invoke();
        Close();
    }
}