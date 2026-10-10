
using System.Linq;

namespace Una.Drawing;

public partial class Node
{
    public event Action<Node>? OnClick;
    public event Action<Node>? OnDoubleClick;
    public event Action<Node>? OnMiddleClick;
    public event Action<Node>? OnRightClick;
    public event Action<Node>? OnMouseEnter;
    public event Action<Node>? OnMouseLeave;
    public event Action<Node>? OnMouseDown;
    public event Action<Node>? OnMouseUp;
    public event Action<Node>? OnDelayedMouseEnter;
    public event Action<Node>? OnDragStart;
    public event Action<Node>? OnDragMove;
    public event Action<Node>? OnDragEnd;

    /// <summary>
    /// True if the element has any interactive event listeners attached to it.
    /// </summary>
    public bool IsInteractive =>
        !IsDisabled && (
            null != OnClick
            || null != OnMiddleClick
            || null != OnRightClick
            || null != OnMouseEnter
            || null != OnDelayedMouseEnter
            || null != OnMouseLeave
            || null != OnMouseDown
            || null != OnMouseUp
            || null != OnDragStart
            || null != OnDragMove
            || null != OnDragEnd
        );

    /// <summary>
    /// True if the element has any drag-related event listeners attached to it.
    /// </summary>
    public bool IsDraggable =>
        !IsDisabled && (
            null != OnDragStart
            || null != OnDragMove
            || null != OnDragEnd
        );

    /// <summary>
    /// Whether the element should have the "hover" tag added when the mouse is over it.
    /// </summary>
    public bool EnableHoverTag { get; set; } = true;

    /// <summary>
    /// True if the element has any primary interaction event listeners attached to it.
    /// </summary>
    public bool HasPrimaryInteraction =>
        !IsDisabled && (IsDraggable || null != OnClick || null != OnMouseUp || null != OnMouseDown);

    /// <summary>
    /// Set to true from an event listener to stop remaining event listeners from being called.
    /// </summary>
    public bool CancelEvent { get; set; }

    /// <summary>
    /// True if the mouse cursor is currently inside the bounding box of the element.
    /// </summary>
    public bool IsMouseOver { get; private set; }

    /// <summary>
    /// True if one of the mouse buttons in held down while the cursor is over the element.
    /// </summary>
    public bool IsMouseDown { get; private set; }

    /// <summary>
    /// True if one of the mouse buttons in held down while the cursor is over a different element.
    /// </summary>
    public bool IsMouseDownOverOtherNode { get; private set; }

    public bool IsMiddleMouseDown { get; private set; }
    public bool IsRightMouseDown  { get; private set; }

    /// <summary>
    /// True if this element currently has focus.
    /// </summary>
    public bool IsFocused { get; private set; }

    /// <summary>
    /// True if this node cannot be interacted with, regardless of bound event
    /// listeners.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    /// True if this node is currently being dragged.
    /// </summary>
    public bool IsDragging { get; private set; }

    /// <summary>
    /// The offset of the drag position from the original position of the node.
    /// </summary>
    public Vector2 DragDelta { get; private set; }

    /// <summary>
    /// The node that is currently being dragged by the user, or NULL if there
    /// is no drag operation in progress.
    /// </summary>
    public static Node? DraggedNode { get; private set; }

    /// <summary>
    /// The color that will be used as background when rendering a tooltip.
    /// </summary>
    public static uint TooltipBackgroundColor { get; set; } = 0xFF353535;

    /// <summary>
    /// The color that will be used as text color when rendering a tooltip.
    /// </summary>
    public static uint TooltipTextColor { get; set; } = 0xFFCACACA;
    
    private bool   _isInWindowOrInteractiveParent;
    private bool   _didStartInteractive;
    private bool   _didStartDelayedMouseEnter;
    private double _mouseOverStartTime;
    private double _isVisibleSince;
    private double _lastClickTime;

    // Hover-only interactive windows (see SetupInteractive).
    private bool    _wasItemActive;
    private bool    _skippedByGate;
    private bool    _descendantBusy;
    private string? _buttonLabel;
    private string? _buttonLabelOwner;

    private void SetupInteractive(ImDrawListPtr drawList)
    {
        _didStartInteractive = false;
        _skippedByGate       = false;

        ToggleTag("disabled", IsDisabled);
        RenderTooltip();
        
        if (!InheritTags && HasTag("hover") && IsDisabled) {
            RemoveTag("hover");
        }

        if (IsDisabled || !IsInteractive || !IsVisible) {
            MouseCursor.RemoveMouseOver(this);
            return;
        }

        bool isPassthrough = IsPassthroughDrawList(drawList);

        // Only allow interaction if the window has focus.
        // TODO: Maybe make an option toggle for this behavior.
        if (!isPassthrough && IsInWindowDrawList(drawList) && !ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)) {
            MouseCursor.RemoveMouseOver(this);
            IsMouseOver = false;
            ToggleTag("hover", false);
            return;
        }

        long frameNow = InteractionFrame.NowMs;

        if (_isVisibleSince == 0) _isVisibleSince = frameNow;

        // Debounce the interactive state to prevent unintentional clicks
        // when toggling visibility on elements on the same position.
        if (frameNow - _isVisibleSince < 100) return;

        Node? interactiveParent = GetInteractiveParent(out bool interactiveParentSkipped);

        // Opening an ImGui window per interactive node per frame is the main
        // cost of the strip, and only matters near the mouse. A node that is
        // not near the mouse and has no interaction state left to settle does
        // not open its window at all; see InteractionGate.
        bool descendantBusy = _descendantBusy;
        _descendantBusy = false;

        if (interactiveParentSkipped || !MustOpenInteractiveWindow(descendantBusy)) {
            SettleSkippedInteractive();
            return;
        }

        _didStartInteractive = true;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0);

        string  imGuiId           = InternalId;
        Vector2 boundingBoxSize   = Bounds.PaddingSize.ToVector2();
        _isInWindowOrInteractiveParent = (!isPassthrough && IsInWindowDrawList(drawList)) || interactiveParent != null;

        // Disabled to allow for multi-monitor support. Leaving this here in case something breaks.
        // ImGui.SetNextWindowViewport(ImGui.GetMainViewport().ID);

        if (_isInWindowOrInteractiveParent) {
            ImGui.SetCursorScreenPos(Bounds.PaddingRect.TopLeft);
            ImGui.BeginChild(imGuiId, boundingBoxSize, false, InteractiveWindowFlags);
            ImGui.SetCursorScreenPos(Bounds.PaddingRect.TopLeft);
        } else {
            ImGui.SetNextWindowPos(Bounds.PaddingRect.TopLeft, ImGuiCond.Always);
            ImGui.SetNextWindowSize(boundingBoxSize, ImGuiCond.Always);
            ImGui.Begin(imGuiId, InteractiveWindowFlags);
        }

        bool wasHovered  = IsMouseOver;
        bool wasDragging = IsDragging;

        ImGui.SetCursorScreenPos(Bounds.PaddingRect.TopLeft);
        if (!ReferenceEquals(_buttonLabelOwner, imGuiId)) {
            _buttonLabelOwner = imGuiId;
            _buttonLabel      = $"{imGuiId}##Button";
        }

        ImGui.InvisibleButton(_buttonLabel, Bounds.PaddingSize.ToVector2());
        IsMouseOver    = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenOverlapped);
        IsFocused      = ImGui.IsItemFocused();
        _wasItemActive = ImGui.IsItemActive();
        IsDragging  = IsDragging || (IsMouseOver && ImGui.IsMouseDragging(ImGuiMouseButton.Left));

        var isHovered = ImGui.IsItemHovered();
        
        if (isHovered && HasPrimaryInteraction && EnableHoverTag) {
            MouseCursor.RegisterMouseOver(this);
        } else {
            MouseCursor.RemoveMouseOver(this);
        }

        if (IsDraggable && IsDragging) {
            if (!wasDragging) {
                RaiseEvent(OnDragStart);
                ToggleTag("dragging", true);
                ToggleTag("hover", false);
                IsMouseOver = false;
                IsDragging  = true;
                DragDelta   = ImGui.GetIO().MouseDelta;
                DraggedNode = this;
            } else {
                DragDelta   += ImGui.GetIO().MouseDelta;
                DraggedNode =  this;
                RaiseEvent(OnDragMove);
                RenderDragGhost();
            }
        }

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && IsDragging) {
            RaiseEvent(OnDragEnd);
            ToggleTag("dragging", false);
            DraggedNode = null;
            IsDragging  = false;
            DragDelta   = Vector2.Zero;
        }

        switch (!IsDragging && IsMouseOver && HasPrimaryInteraction && EnableHoverTag) {
            case true when !HasTag("hover"):
                AddTag("hover");
                break;
            case false when HasTag("hover"):
                RemoveTag("hover");
                break;
        }

        switch (IsFocused) {
            case true when !HasTag("focus"):
                AddTag("focus");
                break;
            case false when HasTag("focus"):
                RemoveTag("focus");
                break;
        }

        switch (isHovered && !IsDragging) {
            case true when !HasTag("active"):
                AddTag("active");
                break;
            case false when HasTag("active"):
                RemoveTag("active");
                break;
        }

        switch (wasHovered) {
            case false when IsMouseOver:
                RaiseEvent(OnMouseEnter);
                _mouseOverStartTime = frameNow;
                break;
            case true when !IsMouseOver:
                RaiseEvent(OnMouseLeave);
                _didStartDelayedMouseEnter = false;
                IsMouseDown                = false;
                IsMiddleMouseDown          = false;
                IsRightMouseDown           = false;
                break;
        }

        if (isHovered) {
            if (HasPrimaryInteraction) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            if (_mouseOverStartTime < frameNow - 50) {
                if (!_didStartDelayedMouseEnter) {
                    RaiseEvent(OnDelayedMouseEnter);
                    _didStartDelayedMouseEnter = true;
                }
            }

            if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                if (!IsMouseDown) {
                    RaiseEvent(OnMouseDown);
                    IsMouseDown = true;
                }
            } else if (ImGui.IsMouseDown(ImGuiMouseButton.Middle)) {
                if (!IsMiddleMouseDown) {
                    IsMiddleMouseDown = true;
                }
            } else if (ImGui.IsMouseDown(ImGuiMouseButton.Right)) {
                if (!IsRightMouseDown) {
                    IsRightMouseDown = true;
                }
            } else {
                if (IsMouseDown && !IsMouseDownOverOtherNode) {
                    RaiseEvent(OnMouseUp);
                    RaiseEvent(OnClick);
                    IsMouseDown = false;
                    
                    var now = frameNow;
                    if (_lastClickTime > 0 && now - _lastClickTime < 250) {
                        RaiseEvent(OnDoubleClick);
                    }
                    
                    _lastClickTime = now;
                }

                if (IsMiddleMouseDown) {
                    RaiseEvent(OnMiddleClick);
                    IsMiddleMouseDown = false;
                }

                if (IsRightMouseDown) {
                    RaiseEvent(OnRightClick);
                    IsRightMouseDown = false;
                }

                if (IsMouseDownOverOtherNode) {
                    RaiseEvent(OnMouseUp);
                }
            }
        } else {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                IsMouseDownOverOtherNode = true;
            }
        }

        if (IsMouseDownOverOtherNode && !ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
            IsMouseDownOverOtherNode = false;
        }

        // A node that still has state to settle keeps every interactive
        // ancestor running its window too, because this node nests its own
        // window in theirs (BeginChild).
        if (CreateInteractionState(false).IsBusy) MarkInteractiveAncestorsBusy();
    }

    private InteractionGate.State CreateInteractionState(bool descendantBusy)
    {
        return new InteractionGate.State(
            IsMouseOver,
            IsDragging,
            IsFocused,
            _wasItemActive,
            IsMouseDown,
            IsMiddleMouseDown,
            IsRightMouseDown,
            descendantBusy
        );
    }

    private bool MustOpenInteractiveWindow(bool descendantBusy)
    {
        InteractionGate.State state = CreateInteractionState(descendantBusy);

        // Cheap exit first: no need to touch the mouse for a node that is busy.
        if (state.IsBusy) return true;

        return InteractionGate.MustProcess(
            Bounds.PaddingRect.TopLeft,
            Bounds.PaddingRect.BottomRight,
            InteractionFrame.MousePos,
            InteractionFrame.MouseDelta,
            in state
        );
    }

    /// <summary>
    /// Does what the full path of <see cref="SetupInteractive"/> does to a node
    /// that the mouse is not on and that has no pending interaction state:
    /// removes the hover, focus and active tags and the cursor registration,
    /// and tracks whether the left mouse button is held down elsewhere.
    /// </summary>
    private void SettleSkippedInteractive()
    {
        _skippedByGate = true;

        MouseCursor.RemoveMouseOver(this);

        if (HasTag("hover")) RemoveTag("hover");
        if (HasTag("focus")) RemoveTag("focus");
        if (HasTag("active")) RemoveTag("active");

        // Same as the "not hovered" tail of the full path: set while the button
        // is down over anything else, cleared as soon as it is released.
        IsMouseDownOverOtherNode = ImGui.IsMouseDown(ImGuiMouseButton.Left);
    }

    private void MarkInteractiveAncestorsBusy()
    {
        Node? ancestor = ParentNode;

        while (ancestor != null) {
            if (ancestor.IsInteractive) ancestor._descendantBusy = true;
            ancestor = ancestor.ParentNode;
        }
    }

    private void EndInteractive()
    {
        if (!_didStartInteractive) return;

        if (_isInWindowOrInteractiveParent) {
            ImGui.EndChild();
        } else {
            ImGui.End();
        }

        ImGui.PopStyleVar(5);
    }

    /// <summary>
    /// Returns the top-most interactive parent element.
    /// </summary>
    /// <returns></returns>
    private Node? GetInteractiveParent(out bool anySkipped)
    {
        Node? immediateParent   = ParentNode;
        Node? interactiveParent = null;

        // True if an interactive ancestor skipped opening its window this
        // frame. This node would nest its window in that window, so it has
        // to skip as well (the mouse cannot be on it: it is clipped by the
        // ancestor's window).
        anySkipped = false;

        while (immediateParent != null) {
            if (immediateParent.IsInteractive) {
                interactiveParent = immediateParent;
                if (immediateParent._skippedByGate) anySkipped = true;
            }

            immediateParent = immediateParent.ParentNode;
        }

        return interactiveParent;
    }

    /// <summary>
    /// Returns true if the given drawList is not the foreground or background drawList.
    /// </summary>
    private static unsafe bool IsInWindowDrawList(ImDrawListPtr drawList)
    {
        return
            drawList.Handle != ImGui.GetForegroundDrawList().Handle
            && drawList.Handle != ImGui.GetBackgroundDrawList().Handle;
    }

    private static unsafe bool IsPassthroughDrawList(ImDrawListPtr drawList)
    {
        return PassthroughDrawListHandles.Contains((nint)drawList.Handle);
    }

    private void RaiseEvent(Action<Node>? action)
    {
        if (action == null) return;

        CancelEvent = false;
        foreach (var handler in action.GetInvocationList().Reverse()) {
            if (!CancelEvent) handler.DynamicInvoke(this);
            if (CancelEvent) break;
        }

        CancelEvent = false;
    }

    private void DisposeEventHandlersOf<T>(Action<T>? action)
    {
        if (action == null) return;

        foreach (Delegate handler in action.GetInvocationList()) {
            if (handler is Action<T> del) action -= del;
        }
    }

    private void DisposeEventHandlersOf<T, K>(Action<T, K>? action)
    {
        if (action == null) return;

        foreach (Delegate handler in action.GetInvocationList()) {
            if (handler is Action<T, K> del) action -= del;
        }
    }

    private void DisposeEventHandlersOf(Action? action)
    {
        if (action == null) return;

        foreach (Delegate handler in action.GetInvocationList()) {
            if (handler is Action del) action -= del;
        }
    }

    private double _tooltipHoverStartTime;
    
    private void RenderTooltip()
    {
        if (Tooltip.IsEmpty) return;
        
        if (!IsMouseInNodeBounds(this, new(), false)) {
            _tooltipHoverStartTime = 0;
            return;
        }
        
        var now = InteractionFrame.NowMs;
        if (_tooltipHoverStartTime == 0) {
            _tooltipHoverStartTime = now;
        }
        
        if (_tooltipHoverStartTime < now - 500) {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, TooltipBackgroundColor);
            ImGui.PushStyleColor(ImGuiCol.Text, TooltipTextColor);
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(420.0f);
            ImGui.TextUnformatted(Tooltip.ToString());
            ImGui.EndTooltip();
            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar(2);
        }
    }

    private static ImGuiWindowFlags InteractiveWindowFlags =>
        ImGuiWindowFlags.NoTitleBar
        | ImGuiWindowFlags.NoBackground
        | ImGuiWindowFlags.NoDecoration
        | ImGuiWindowFlags.NoResize
        | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoScrollbar
        | ImGuiWindowFlags.NoScrollWithMouse
        | ImGuiWindowFlags.NoCollapse
        | ImGuiWindowFlags.NoSavedSettings
        | ImGuiWindowFlags.NoDocking
        | ImGuiWindowFlags.NoNavFocus
        | ImGuiWindowFlags.NoNavInputs
        | ImGuiWindowFlags.NoFocusOnAppearing;
}