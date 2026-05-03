using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlueArchiveLottery.Models;

namespace BlueArchiveLottery.Services;

public class LotteryResult
{
    public string Item { get; set; } = "";
    public int Star { get; set; }
}

public class NormalLotteryEngine : IDisposable
{
    private readonly List<Level> _levels;
    private CancellationTokenSource? _cts;

    public event Action<string>? UpdateResult;
    public event Action<LotteryResult>? ResultReady;
    public event Action<string>? ErrorOccurred;

    public NormalLotteryEngine(List<Level> levels) => _levels = levels;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(() => Run(token), token);
    }

    public void Stop() => _cts?.Cancel();

    private async Task Run(CancellationToken token)
    {
        try
        {
            var allItems = _levels.SelectMany(l => l.items).ToList();
            if (allItems.Count == 0)
            {
                InvokeError("没有可抽选的项目！");
                return;
            }
            var rnd = new Random();
            var start = DateTime.Now;

            // 快速滚动阶段：1.0 秒
            while (!token.IsCancellationRequested && (DateTime.Now - start).TotalSeconds < 1.0)
            {
                var picked = allItems[rnd.Next(allItems.Count)];
                InvokeUpdate(picked);
                await Task.Delay(100, token);
            }

            // 慢速滚动阶段：最多到 2.0 秒
            while (!token.IsCancellationRequested && (DateTime.Now - start).TotalSeconds < 2.0)
            {
                var picked = allItems[rnd.Next(allItems.Count)];
                InvokeUpdate(picked);
                int delay = 200 + (int)((DateTime.Now - start).TotalSeconds * 100);
                await Task.Delay(Math.Min(delay, 600), token);
            }

            if (!token.IsCancellationRequested)
            {
                var final = PickFinal(rnd);
                InvokeResult(final);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            InvokeError(ex.Message);
        }
        finally { _cts?.Dispose(); }
    }

    private LotteryResult PickFinal(Random rnd)
    {
        double randVal = rnd.NextDouble() * 100;
        double cumulative = 0;
        Level? sel = null;
        foreach (var l in _levels)
        {
            cumulative += l.probability;
            if (randVal <= cumulative) { sel = l; break; }
        }
        if (sel != null)
        {
            var item = sel.items[rnd.Next(sel.items.Count)];
            return new LotteryResult { Item = item, Star = sel.star };
        }
        var fallback = _levels[rnd.Next(_levels.Count)];
        return new LotteryResult
        {
            Item = fallback.items[rnd.Next(fallback.items.Count)],
            Star = fallback.star
        };
    }

    private void InvokeUpdate(string text)
        => App.Current.Dispatcher.Invoke(() => UpdateResult?.Invoke(text));
    private void InvokeResult(LotteryResult r)
        => App.Current.Dispatcher.Invoke(() => ResultReady?.Invoke(r));
    private void InvokeError(string msg)
        => App.Current.Dispatcher.Invoke(() => ErrorOccurred?.Invoke(msg));

    public void Dispose() => Stop();
}

public class SilentLotteryEngine
{
    private readonly List<Level> _levels;
    public event Action<LotteryResult>? ResultReady;
    public event Action<string>? ErrorOccurred;

    public SilentLotteryEngine(List<Level> levels) => _levels = levels;

    public Task RunAsync() => Task.Run(() =>
    {
        try
        {
            var allItems = _levels.SelectMany(l => l.items).ToList();
            if (allItems.Count == 0)
            {
                InvokeError("没有可抽选的项目！");
                return;
            }
            var rnd = new Random();
            var result = PickFinal(rnd);
            InvokeResult(result);
        }
        catch (Exception ex) { InvokeError(ex.Message); }
    });

    private LotteryResult PickFinal(Random rnd)
    {
        double randVal = rnd.NextDouble() * 100;
        double cumulative = 0;
        Level? sel = null;
        foreach (var l in _levels)
        {
            cumulative += l.probability;
            if (randVal <= cumulative) { sel = l; break; }
        }
        if (sel != null)
            return new LotteryResult { Item = sel.items[rnd.Next(sel.items.Count)], Star = sel.star };
        var fallback = _levels[rnd.Next(_levels.Count)];
        return new LotteryResult { Item = fallback.items[rnd.Next(fallback.items.Count)], Star = fallback.star };
    }

    private void InvokeResult(LotteryResult r)
        => App.Current.Dispatcher.Invoke(() => ResultReady?.Invoke(r));
    private void InvokeError(string msg)
        => App.Current.Dispatcher.Invoke(() => ErrorOccurred?.Invoke(msg));
}