namespace Kikicast.Core;

// Policy adapted from Tinycast DoubleTapDetector.swift; see NOTICE.md (AGPL-3.0).
public sealed class DoubleTapRecognizer
{
    public const double MaxHold = .250;
    public const double MaxGap = .300;
    private int? pressed;
    private double pressAt;
    private int? pending;
    private double releasedAt;

    public void Cancel() { pressed = null; pending = null; }

    // Caller supplies monotonic seconds and physical modifier identity; other input cancels.
    public bool Press(int key, double now)
    {
        if (pressed != null) { Cancel(); return false; }
        pressed = key;
        pressAt = now;
        return false;
    }

    public bool Release(int key, double now)
    {
        if (pressed != key || now < pressAt || now > pressAt + MaxHold)
        { Cancel(); return false; }
        pressed = null;
        if (pending == key && pressAt >= releasedAt && pressAt <= releasedAt + MaxGap)
        { pending = null; return true; }
        pending = key;
        releasedAt = now;
        return false;
    }
}
