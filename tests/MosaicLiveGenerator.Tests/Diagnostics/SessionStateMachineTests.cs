using MosaicLiveGenerator.Diagnostics;
using Xunit;

namespace MosaicLiveGenerator.Tests.Diagnostics;

public class SessionStateMachineTests
{
    [Fact]
    public void Initial_IsStopped()
    {
        var sm = new SessionStateMachine();
        Assert.Equal(SessionState.Stopped, sm.Current);
    }

    [Fact]
    public void ValidTransition_FiresEvent()
    {
        var sm = new SessionStateMachine();
        SessionStateChangedEventArgs? captured = null;
        sm.Changed += (_, e) => captured = e;

        var transitioned = sm.TryTransition(SessionState.Stopped, SessionState.Starting);

        Assert.True(transitioned);
        Assert.Equal(SessionState.Starting, sm.Current);
        Assert.NotNull(captured);
        Assert.Equal(SessionState.Stopped, captured!.OldState);
        Assert.Equal(SessionState.Starting, captured.NewState);
    }

    [Fact]
    public void TryTransition_ReturnsFalseWhenFromDoesNotMatch()
    {
        var sm = new SessionStateMachine();
        var ok = sm.TryTransition(SessionState.Running, SessionState.Stopping);
        Assert.False(ok);
        Assert.Equal(SessionState.Stopped, sm.Current);
    }

    [Theory]
    [InlineData(SessionState.Stopped, SessionState.Starting, true)]
    [InlineData(SessionState.Starting, SessionState.Running, true)]
    [InlineData(SessionState.Starting, SessionState.Faulted, true)]
    [InlineData(SessionState.Running, SessionState.Faulted, true)]
    [InlineData(SessionState.Running, SessionState.Stopping, true)]
    [InlineData(SessionState.Starting, SessionState.Stopping, true)]
    [InlineData(SessionState.Faulted, SessionState.Stopping, true)]
    [InlineData(SessionState.Stopping, SessionState.Stopped, true)]
    [InlineData(SessionState.Stopped, SessionState.Running, false)]
    [InlineData(SessionState.Running, SessionState.Stopped, false)]
    public void TransitionRules(SessionState from, SessionState to, bool allowed)
    {
        Assert.Equal(allowed, SessionStateMachine.IsAllowed(from, to));
    }

    [Fact]
    public async Task ConcurrentTransitions_ExactlyOneSucceeds()
    {
        var sm = new SessionStateMachine();
        var wins = 0;

        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            if (sm.TryTransition(SessionState.Stopped, SessionState.Starting))
                Interlocked.Increment(ref wins);
        })).ToArray();

        await Task.WhenAll(tasks);
        Assert.Equal(1, wins);
    }
}
