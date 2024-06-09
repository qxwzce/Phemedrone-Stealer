using System;

namespace Phemedrone.Extensions;

public class RandomUserAgent
{
    // Random UserAgent from Leaf.Xnet library
    private static Random rnd = new();
    public static string Chrome()
    {
        var num = rnd.Next(62, 71);
        var num2 = rnd.Next(2100, 3539);
        var num3 = rnd.Next(171);
        return "Mozilla/5.0 (" + RandomWindowsVersion() + ") AppleWebKit/537.36 (KHTML, like Gecko) " +
               $"Chrome/{num}.0.{num2}.{num3} Safari/537.36";
    }
    private static string RandomWindowsVersion()
    {
        var text = "Windows NT ";
        var num = rnd.Next(99) + 1;
        text += num switch
        {
            >= 1 and <= 45 => "10.0",
            > 45 and <= 80 => "6.1",
            > 80 and <= 95 => "6.3",
            _ => "6.2"
        };
        if (rnd.NextDouble() <= 0.65)
        {
            text += rnd.NextDouble() <= 0.5 ? "; WOW64" : "; Win64; x64";
        }
        return text;
    }
}