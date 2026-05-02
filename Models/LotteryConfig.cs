using System.Collections.Generic;

namespace BlueArchiveLottery.Models;

public class LotteryConfig
{
    public List<Level> levels { get; set; } = new();
}

public class Level
{
    public int star { get; set; }
    public double probability { get; set; }
    public List<string> items { get; set; } = new();
}