using Avalonia.Media;

namespace DiSerial.App.Views;

// Define shared toolbar icons.
public static class ToolbarIcons
{
    public static readonly Geometry Connect = Geometry.Parse("M5.5,3.5 L12.5,8 L5.5,12.5 Z");

    public static readonly Geometry Disconnect =
        Geometry.Parse("M4.5,4.5 L11.5,11.5 M11.5,4.5 L4.5,11.5");

    public static readonly Geometry Clear =
        Geometry.Parse("M3,4 H13 M5,4 V13 H11 V4 M6.5,4 V2.5 H9.5 V4");

    public static readonly Geometry Pause = Geometry.Parse("M6,3.5 V12.5 M10,3.5 V12.5");

    public static readonly Geometry Resume = Geometry.Parse("M5.5,3.5 L12.5,8 L5.5,12.5 Z");

    public static readonly Geometry Record =
        Geometry.Parse("M3.5,8 A4.5,4.5 0 1,1 12.5,8 A4.5,4.5 0 1,1 3.5,8 Z");

    public static readonly Geometry StopRecord = Geometry.Parse("M4.5,4.5 H11.5 V11.5 H4.5 Z");

    public static readonly Geometry Export =
        Geometry.Parse("M8,2.5 V9.5 M5,6.5 L8,9.5 L11,6.5 M3,11 V13.5 H13 V11");

    public static readonly Geometry DisplaySettings =
        Geometry.Parse("M3,5 H13 M3,11 H13 M6,3 V7 M10,9 V13");
}
