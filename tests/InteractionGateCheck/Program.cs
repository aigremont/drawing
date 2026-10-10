// Headless checks for the rule that decides whether an interactive node opens its ImGui window this frame
// (Una.Drawing/src/Node/InteractionGate.cs, used by Node.Interaction.cs SetupInteractive).
using System.Numerics;
using Una.Drawing;

int failures = 0;

void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {name}");
    if (!ok) failures++;
}

InteractionGate.State Idle = default;

// A 100x30 node at (500, 100).
Vector2 tl = new(500, 100);
Vector2 br = new(600, 130);

bool Process(Vector2 mouse, Vector2 delta, InteractionGate.State state) =>
    InteractionGate.MustProcess(tl, br, mouse, delta, in state);

// ---- Settled node, mouse far away: no window. ----
Check("far mouse, idle node: skipped", !Process(new(100, 600), Vector2.Zero, Idle));
Check("invalid mouse position (-FLT_MAX): skipped", !Process(new(-float.MaxValue, -float.MaxValue), Vector2.Zero, Idle));
Check("NaN mouse position: skipped", !Process(new(float.NaN, float.NaN), Vector2.Zero, Idle));

// ---- Mouse on the node or near it: window. ----
Check("mouse inside: processed", Process(new(550, 115), Vector2.Zero, Idle));
Check("mouse on the corner: processed", Process(tl, Vector2.Zero, Idle) && Process(br, Vector2.Zero, Idle));
Check("mouse inside the base margin: processed", Process(new(500 - InteractionGate.BaseMargin, 115), Vector2.Zero, Idle));
Check("mouse just outside the base margin: skipped", !Process(new(500 - InteractionGate.BaseMargin - 1, 115), Vector2.Zero, Idle));
Check("margin applies on every side",
    Process(new(550, 100 - InteractionGate.BaseMargin), Vector2.Zero, Idle)
    && Process(new(550, 130 + InteractionGate.BaseMargin), Vector2.Zero, Idle)
    && Process(new(600 + InteractionGate.BaseMargin, 115), Vector2.Zero, Idle)
    && !Process(new(550, 130 + InteractionGate.BaseMargin + 1), Vector2.Zero, Idle));

// ---- Margin grows with pointer speed and is capped. ----
Check("margin without movement is the base margin", InteractionGate.MarginFor(0) == InteractionGate.BaseMargin);
Check("margin grows with movement (either direction)",
    InteractionGate.MarginFor(20) == InteractionGate.MarginFor(-20) && InteractionGate.MarginFor(20) > InteractionGate.BaseMargin);
Check("margin is capped", InteractionGate.MarginFor(100000) == InteractionGate.MaxMargin);
Check("NaN movement counts as none", InteractionGate.MarginFor(float.NaN) == InteractionGate.BaseMargin);
Check("a fast pointer opens the window earlier", Process(new(420, 115), new(40, 0), Idle) && !Process(new(420, 115), Vector2.Zero, Idle));

// ---- Any pending state keeps the window, wherever the mouse is. ----
Vector2 far = new(100, 600);
Check("hovered node keeps its window so mouse-leave fires", Process(far, Vector2.Zero, Idle with { IsMouseOver = true }));
Check("dragged node keeps its window so drag-end fires", Process(far, Vector2.Zero, Idle with { IsDragging = true }));
Check("focused node keeps its window so the focus tag clears", Process(far, Vector2.Zero, Idle with { IsFocused = true }));
Check("active ImGui item keeps its window until release", Process(far, Vector2.Zero, Idle with { IsItemActive = true }));
Check("pressed node keeps its window", Process(far, Vector2.Zero, Idle with { IsMouseDown = true }));
Check("middle-pressed node keeps its window", Process(far, Vector2.Zero, Idle with { IsMiddleMouseDown = true }));
Check("right-pressed node keeps its window", Process(far, Vector2.Zero, Idle with { IsRightMouseDown = true }));
Check("busy descendant keeps the window it nests in", Process(far, Vector2.Zero, Idle with { HasBusyDescendant = true }));

// ---- Simulated pointer paths: events are never missed, the window is open before the pointer arrives. ----
// A node reproduces the parts of SetupInteractive the gate touches: IsMouseOver follows the real rect, enter/leave fire
// on the transition, and the full path only runs while the gate says so.
(int windows, int enters, int leaves, bool openedBeforeEntering) Simulate(float speed, float startX, float endX)
{
    InteractionGate.State state = Idle;
    int   windows = 0, enters = 0, leaves = 0;
    bool  everInside = false, openedBeforeEntering = true;
    float x = startX;
    float step = MathF.Sign(endX - startX) * speed;
    float previousX = x;
    bool  lastProcess = false;

    for (int frame = 0; frame < 2000; frame++) {
        Vector2 mouse = new(x, 115);
        Vector2 delta = new(x - previousX, 0);
        bool inside = mouse.X >= tl.X && mouse.X <= br.X && mouse.Y >= tl.Y && mouse.Y <= br.Y;

        bool process = InteractionGate.MustProcess(tl, br, mouse, delta, in state);

        if (process) {
            windows++;
            bool was = state.IsMouseOver;
            state = state with { IsMouseOver = inside };
            if (!was && inside) enters++;
            if (was && !inside) leaves++;
        } else if (inside) {
            openedBeforeEntering = false; // the pointer is on the node and no window exists
        }

        if (inside && !everInside) {
            everInside = true;
            // ImGui hit-tests the windows that were active in the previous frame, so the window must already have
            // been open in the frame before the pointer first reached the node.
            if (!lastProcess) openedBeforeEntering = false;
        }

        lastProcess = process;
        previousX = x;
        x += step;
        if ((step > 0 && x > endX) || (step < 0 && x < endX)) break;
    }

    return (windows, enters, leaves, openedBeforeEntering);
}

foreach (float speed in new[] { 1f, 5f, 12f, 30f, 60f }) {
    var (windows, enters, leaves, ok) = Simulate(speed, 100, 1000);
    Check($"pointer crossing at {speed}px/frame: one enter, one leave, the window is open before the pointer arrives",
        enters == 1 && leaves == 1 && ok);
    Check($"pointer crossing at {speed}px/frame: the window is open only for the crossing ({windows} of the frames)",
        windows < (1000 - 100) / speed / 2);
}

// A pointer that stays on the node keeps it open; one that leaves for good closes it again.
{
    InteractionGate.State s = Idle with { IsMouseOver = true };
    Check("stays open while hovered", Process(new(550, 115), Vector2.Zero, s));
    s = s with { IsMouseOver = false };
    Check("closes once the leave has been delivered", !Process(far, Vector2.Zero, s));
}

Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
return failures == 0 ? 0 : 1;
