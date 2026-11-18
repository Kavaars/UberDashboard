using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace UberDashboard.App.Hosting;

internal sealed class ProfileInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private readonly bool _ownsProfile;
    private RegisteredWaitHandle? _listener;

    public ProfileInstance(string profile)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile)).ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        var name = @"Global\UberDashboard-" + key;
        _mutex = new Mutex(false, name);
        try
        {
            _activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-activate");
            try { _ownsProfile = _mutex.WaitOne(0); }
            catch (AbandonedMutexException) { _ownsProfile = true; }
            catch { _activation.Dispose(); throw; }
        }
        catch { _mutex.Dispose(); throw; }
    }

    public bool IsPrimary => _ownsProfile;

    public void RequestActivation() => _activation.Set();

    public void Listen(Action activate) =>
        _listener = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) => activate(), null, Timeout.Infinite, false);

    public void Dispose()
    {
        _listener?.Unregister(null);
        _activation.Dispose();
        if (_ownsProfile) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
