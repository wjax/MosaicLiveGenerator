namespace MosaicLiveGenerator.Diagnostics;

internal sealed class SessionStateMachine
{
    private readonly object _lock = new();
    private SessionState _current = SessionState.Stopped;

    public event EventHandler<SessionStateChangedEventArgs>? Changed;

    public SessionState Current
    {
        get { lock (_lock) return _current; }
    }

    public bool TryTransition(SessionState expected, SessionState next)
    {
        if (!IsAllowed(expected, next))
            return false;

        SessionStateChangedEventArgs? args = null;
        lock (_lock)
        {
            if (_current != expected) return false;
            _current = next;
            args = new SessionStateChangedEventArgs { OldState = expected, NewState = next };
        }
        Changed?.Invoke(this, args);
        return true;
    }

    public static bool IsAllowed(SessionState from, SessionState to)
    {
        return (from, to) switch
        {
            (SessionState.Stopped,  SessionState.Starting) => true,
            (SessionState.Starting, SessionState.Running)  => true,
            (SessionState.Starting, SessionState.Faulted)  => true,
            (SessionState.Starting, SessionState.Stopping) => true,
            (SessionState.Running,  SessionState.Faulted)  => true,
            (SessionState.Running,  SessionState.Stopping) => true,
            (SessionState.Faulted,  SessionState.Stopping) => true,
            (SessionState.Stopping, SessionState.Stopped)  => true,
            _ => false,
        };
    }
}
