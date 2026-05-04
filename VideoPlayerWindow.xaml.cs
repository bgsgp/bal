using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using BlueArchiveLottery.Helpers;

namespace BlueArchiveLottery;

public partial class VideoPlayerWindow : Window
{
    private readonly int _starLevel;
    private readonly string _videoFolder;
    private string _currentPhase = "start";
    private bool _skipPlayed = false;
    private Point _pressPoint;

    public event Action? VideoEnded;

    public VideoPlayerWindow(int starLevel)
    {
        InitializeComponent();
        _starLevel = starLevel;

        string baseVideoFolder = Path.Combine(PathHelper.ResourcesPath, PathHelper.GetVideoFolder(starLevel));

        if (starLevel == 3)
        {
            _videoFolder = ChooseSpecialCharacterFolder(baseVideoFolder);
        }
        else
        {
            var rnd = new Random();
            string characterFolder = rnd.Next(2) == 0 ? "A.R.O.N.A" : "Plana";
            _videoFolder = Path.Combine(baseVideoFolder, characterFolder);
            if (!Directory.Exists(_videoFolder))
            {
                string fallback = characterFolder == "A.R.O.N.A" ? "Plana" : "A.R.O.N.A";
                _videoFolder = Path.Combine(baseVideoFolder, fallback);
            }
        }

        // 不再初始化或使用任何计时器
        PlayStartVideo();
    }

    private static string ChooseSpecialCharacterFolder(string basePath)
    {
        var rnd = new Random();
        double roll = rnd.NextDouble();
        string[] candidates = ["A.R.O.N.A", "Plana", "Both", "Change"];
        double[] probabilities = [0.4, 0.4, 0.15, 0.05];
        double cumulative = 0;
        string selected = candidates[0];
        for (int i = 0; i < candidates.Length; i++)
        {
            cumulative += probabilities[i];
            if (roll < cumulative) { selected = candidates[i]; break; }
        }
        string fullPath = Path.Combine(basePath, selected);
        if (!Directory.Exists(fullPath))
        {
            foreach (var folder in candidates)
            {
                string fallbackPath = Path.Combine(basePath, folder);
                if (Directory.Exists(fallbackPath)) { fullPath = fallbackPath; break; }
            }
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
        SkipButton.Visibility = Visibility.Visible; // 确保手动跳过按钮可见
    }

    private void SignatureCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressPoint = e.GetPosition(SignatureCanvas);
    }

    private void SignatureCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        // 无操作，不再重启计时器
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
        // 绘制完成后不启动任何计时器，永久停留
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