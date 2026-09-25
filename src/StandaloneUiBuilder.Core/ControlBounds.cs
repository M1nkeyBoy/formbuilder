namespace StandaloneUiBuilder.Core;

/// <summary>A control's position and size in device-independent pixels (DIPs).</summary>
public readonly record struct ControlBounds(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}
