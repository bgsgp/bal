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
        _videoFolder = Path.Combine(PathHelper.ResourcesPath, PathHelper.GetVideoFolder(starLevel));

        _drawStopTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _drawStopTimer.Tick += (s, e) => { _drawStopTimer.Stop(); PlayOpenVideo(); };

        PlayStartVideo();
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