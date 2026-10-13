using System.Collections.Concurrent;

namespace VehicleService.Application.Common;

public interface IConcurrencyCoordinator
{
    Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken = default);
}

public class KeyedLockCoordinator : IConcurrencyCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _semaphore.Release();
            }
        }
    }
}
