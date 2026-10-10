using System.Numerics;

namespace Una.Drawing;

/// <summary>
/// The decision of whether an interactive node has to open its ImGui window
/// (and InvisibleButton) in the current frame.
/// </summary>
/// <remarks>
/// <para>
/// Opening an ImGui window per interactive node per frame is expensive, and it
/// only matters when the node can observe or change something: the mouse is on
/// it (or about to be), or the node still carries interaction state that the
/// window has to update (hovered, dragged, focused, pressed, the active ImGui
/// item), or a nested interactive node does.
/// </para>
/// <para>
/// This type has no dependency on ImGui or Dalamud on purpose, so the rule can
/// be checked headlessly (see tests/InteractionGateCheck).
/// </para>
/// </remarks>
internal static class InteractionGate
{
    /// <summary>
    /// Pixels around the node in which the window is already opened, so it
    /// is an active ImGui window by the time the pointer arrives. ImGui decides
    /// which window is hovered (and so whether the game still gets the click)
    /// from the windows that were active in the previous frame.
    /// </summary>
    internal const float BaseMargin = 12f;

    /// <summary>
    /// How many frames of pointer movement are added to the margin, so a fast
    /// pointer cannot cross the margin in a single frame.
    /// </summary>
    internal const float DeltaFactor = 2f;

    /// <summary>
    /// Upper bound of the margin, so a pointer jump does not open every window.
    /// </summary>
    internal const float MaxMargin = 160f;

    /// <summary>
    /// The interaction state of a node as left by its previous frame.
    /// </summary>
    internal readonly record struct State(
        bool IsMouseOver,
        bool IsDragging,
        bool IsFocused,
        bool IsItemActive,
        bool IsMouseDown,
        bool IsMiddleMouseDown,
        bool IsRightMouseDown,
        bool HasBusyDescendant
    ) {
        /// <summary>
        /// True if the node must keep running its window regardless of where
        /// the mouse is, because leaving it now would lose or delay an event
        /// or leave a tag or ImGui item state behind.
        /// </summary>
        internal bool IsBusy =>
            IsMouseOver
            || IsDragging
            || IsFocused
            || IsItemActive
            || IsMouseDown
            || IsMiddleMouseDown
            || IsRightMouseDown
            || HasBusyDescendant;
    }

    /// <summary>
    /// Returns the margin around a node in which the mouse opens its window.
    /// </summary>
    internal static float MarginFor(float mouseDeltaAxis)
    {
        float margin = BaseMargin + DeltaFactor * MathF.Abs(mouseDeltaAxis);

        // NaN compares false everywhere; treat it as no movement.
        if (float.IsNaN(margin)) return BaseMargin;

        return MathF.Min(margin, MaxMargin);
    }

    /// <summary>
    /// True if the mouse is inside the given rectangle grown by the margin.
    /// An invalid mouse position (ImGui uses -FLT_MAX) is never near anything.
    /// </summary>
    internal static bool IsMouseNear(Vector2 topLeft, Vector2 bottomRight, Vector2 mouse, Vector2 mouseDelta)
    {
        float mx = MarginFor(mouseDelta.X);
        float my = MarginFor(mouseDelta.Y);

        return mouse.X >= topLeft.X - mx
            && mouse.X <= bottomRight.X + mx
            && mouse.Y >= topLeft.Y - my
            && mouse.Y <= bottomRight.Y + my;
    }

    /// <summary>
    /// True if the node has to open its window this frame.
    /// </summary>
    internal static bool MustProcess(
        Vector2 topLeft,
        Vector2 bottomRight,
        Vector2 mouse,
        Vector2 mouseDelta,
        in State state
    ) {
        return state.IsBusy || IsMouseNear(topLeft, bottomRight, mouse, mouseDelta);
    }
}
