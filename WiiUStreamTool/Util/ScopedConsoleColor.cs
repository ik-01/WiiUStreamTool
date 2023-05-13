using System;

namespace WiiUStreamTool.Util;

public sealed class ScopedConsoleColor : IDisposable {
    public readonly ConsoleColor? PreviousForeground;
    public readonly ConsoleColor? PreviousBackground;

    public ScopedConsoleColor(ConsoleColor? foreground, ConsoleColor? background) {
        if (foreground is { } c1) {
            PreviousForeground = Console.ForegroundColor;
            Console.ForegroundColor = c1;
        }

        if (background is { } c2) {
            PreviousBackground = Console.BackgroundColor;
            Console.BackgroundColor = c2;
        }
    }

    public void Dispose() {
        if (PreviousForeground is { } c1)
            Console.ForegroundColor = c1;
        if (PreviousBackground is { } c2)
            Console.BackgroundColor = c2;
    }

    public static ScopedConsoleColor Foreground(ConsoleColor color) => new(color, null);
    
    public static ScopedConsoleColor Background(ConsoleColor color) => new(null, color);
}
