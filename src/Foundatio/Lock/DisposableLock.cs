﻿using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Foundatio.AsyncEx;
using Foundatio.Utility;
using Microsoft.Extensions.Logging;

namespace Foundatio.Lock;

internal class DisposableLock : ILock
{
    private readonly ILockProvider _lockProvider;
    private readonly ILogger _logger;
    private bool _isReleased;
    private int _renewalCount;
    private readonly AsyncLock _lock = new();
    private readonly Stopwatch _duration;
    private readonly bool _shouldReleaseOnDispose;

    public DisposableLock(string resource, string lockId, TimeSpan timeWaitedForLock, ILockProvider lockProvider, ILogger logger, bool shouldReleaseOnDispose)
    {
        Resource = resource;
        LockId = lockId;
        TimeWaitedForLock = timeWaitedForLock;
        AcquiredTimeUtc = SystemClock.UtcNow;
        _duration = Stopwatch.StartNew();
        _logger = logger;
        _lockProvider = lockProvider;
        _shouldReleaseOnDispose = shouldReleaseOnDispose;
    }

    public string LockId { get; }
    public string Resource { get; }
    public DateTime AcquiredTimeUtc { get; }
    public TimeSpan TimeWaitedForLock { get; }
    public int RenewalCount => _renewalCount;

    public async ValueTask DisposeAsync()
    {
        if (!_shouldReleaseOnDispose)
            return;

        bool isTraceLogLevelEnabled = _logger.IsEnabled(LogLevel.Trace);
        if (isTraceLogLevelEnabled)
            _logger.LogTrace("Disposing lock ({LockId}) {Resource}", LockId, Resource);

        try
        {
            await ReleaseAsync().AnyContext();
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
                _logger.LogError(ex, "Unable to release lock ({LockId}) {Resource}", LockId, Resource);
        }

        if (isTraceLogLevelEnabled)
            _logger.LogTrace("Disposed lock ({LockId}) {Resource}", LockId, Resource);
    }

    public async Task RenewAsync(TimeSpan? lockExtension = null)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Renewing lock ({LockId}) {Resource}", LockId, Resource);

        await _lockProvider.RenewAsync(Resource, LockId, lockExtension).AnyContext();
        _renewalCount++;

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Renewed lock ({LockId}) {Resource}", LockId, Resource);
    }

    public async Task ReleaseAsync()
    {
        if (_isReleased)
            return;

        using (await _lock.LockAsync().AnyContext())
        {
            if (_isReleased)
                return;

            _isReleased = true;
            _duration.Stop();

            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Releasing lock ({LockId}) {Resource} after {Duration:g}", LockId, Resource, _duration.Elapsed);

            await _lockProvider.ReleaseAsync(Resource, LockId);
        }
    }
}
