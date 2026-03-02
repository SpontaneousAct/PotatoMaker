using PotatoMaker.Core;

namespace PotatoMaker.Cli;

sealed class ConsoleProgressHandler : IProgress<EncodeProgress>
{
    public void Report(EncodeProgress value)
    {
        int    percent = Math.Clamp(value.Percent, 0, 100);
        int    filled  = percent / 5;
        string bar     = new string('█', filled) + new string('░', 20 - filled);
        Console.Write($"\r{value.Label}  [{bar}] {percent,3}%   ");
    }
}
