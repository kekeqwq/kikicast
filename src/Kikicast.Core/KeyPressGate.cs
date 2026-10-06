namespace Kikicast.Core;

// A press cycle belongs to this UI invocation, not WPF's process-wide previous key.
// Rearm from physical state so an already-held key cannot activate after opening.
public sealed class KeyPressGate
{
    private bool down;
    public void Rearm(bool physicallyDown) => down = physicallyDown;
    public bool Press()
    {
        if (down) return false;
        down = true;
        return true;
    }
    public void Release() => down = false;
}
