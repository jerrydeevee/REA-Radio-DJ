// REA-Radio-DJ Studio - the desktop studio for the REA-Radio-DJ engine.
// Copyright (C) REA-Radio-DJ contributors. Licensed under the GNU GPL v3 or later; see LICENSE and NOTICE.md.

using Avalonia;

namespace ReaRadioDj.Studio;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
