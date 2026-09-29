using System;

internal static class Program
{
    private static int Main()
    {
        try { Console.WriteLine(YouYou.Framework.Validation.CoreChecks.Run()); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
