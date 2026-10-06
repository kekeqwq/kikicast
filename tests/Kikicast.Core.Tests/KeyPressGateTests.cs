namespace Kikicast.Core.Tests;

public sealed class KeyPressGateTests
{
    [Fact]
    public void FreshPressRunsOnceUntilRelease()
    {
        var gate = new KeyPressGate();
        gate.Rearm(false);
        Assert.True(gate.Press());
        for (var i = 0; i < 10; i++) Assert.False(gate.Press());
        gate.Release();
        Assert.True(gate.Press());
        Assert.False(gate.Press());
    }
    [Fact]
    public void MissingReleaseInPreviousInvocationDoesNotConsumeNextFreshPress()
    {
        var gate = new KeyPressGate();
        gate.Rearm(false); Assert.True(gate.Press());
        gate.Rearm(false); // Key-up went to the restored source, not the palette.
        Assert.True(gate.Press()); Assert.False(gate.Press());
    }
    [Fact]
    public void KeyHeldAcrossOpeningCannotRunUntilReleasedAndPressedAgain()
    {
        var gate = new KeyPressGate();
        gate.Rearm(true);
        Assert.False(gate.Press()); Assert.False(gate.Press());
        gate.Release(); Assert.True(gate.Press());
    }
    [Fact]
    public void DuplicateReleaseDoesNotBlockFreshPress()
    {
        var gate = new KeyPressGate();
        gate.Release(); gate.Release(); Assert.True(gate.Press());
        gate.Release(); gate.Release(); Assert.True(gate.Press());
    }
    [Fact]
    public void RearmHeldAfterLostReleaseRemainsBlocked()
    {
        var gate = new KeyPressGate();
        Assert.True(gate.Press()); gate.Rearm(true);
        Assert.False(gate.Press());
        gate.Release(); Assert.True(gate.Press());
    }
    [Fact]
    public void RepeatedOpeningWithoutInputNeverCreatesExecutionOrConsumesNextPress()
    {
        var gate = new KeyPressGate();
        for (var i = 0; i < 10; i++) gate.Rearm(false);
        Assert.True(gate.Press()); Assert.False(gate.Press());
    }
}
