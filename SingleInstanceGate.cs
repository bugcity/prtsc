using System.Threading;

namespace Prtsc;

internal sealed class SingleInstanceGate : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle wake;
    private RegisteredWaitHandle? listener;
    private bool disposed;
    public bool IsFirst { get; }
    public SingleInstanceGate(string name)
    {
        mutex = new Mutex(true, name, out bool created); IsFirst = created;
        wake = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Wake");
        if (!IsFirst) wake.Set();
    }
    public void Listen(Action action) => listener = ThreadPool.RegisterWaitForSingleObject(wake, (_, _) => action(), null, Timeout.Infinite, false);
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        listener?.Unregister(null); wake.Dispose();
        if (IsFirst) mutex.ReleaseMutex(); mutex.Dispose();
    }
}
