namespace Una.Drawing;

/// <summary>
/// Values that are constant for the duration of an ImGui frame and are read by
/// every interactive node: the wall clock (as Unix milliseconds) and the mouse.
/// They are fetched once per frame instead of once per node.
/// </summary>
internal static class InteractionFrame
{
    private static int     _frame = -1;
    private static long    _nowMs;
    private static Vector2 _mousePos;
    private static Vector2 _mouseDelta;

    /// <summary>
    /// The time of the first read in the current frame, in Unix milliseconds.
    /// </summary>
    internal static long NowMs {
        get {
            Refresh();
            return _nowMs;
        }
    }

    internal static Vector2 MousePos {
        get {
            Refresh();
            return _mousePos;
        }
    }

    internal static Vector2 MouseDelta {
        get {
            Refresh();
            return _mouseDelta;
        }
    }

    private static void Refresh()
    {
        int frame = ImGui.GetFrameCount();
        if (frame == _frame) return;

        _frame      = frame;
        _nowMs      = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _mousePos   = ImGui.GetMousePos();
        _mouseDelta = ImGui.GetIO().MouseDelta;
    }
}
