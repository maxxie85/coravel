using System;
using System.Collections.Generic;
using Coravel.Scheduling.Schedule.Interfaces;
using Coravel.Scheduling.Schedule.UtcTime;

namespace Coravel.Scheduling.Schedule.Mutex
{
    public class InMemoryMutex : IMutex
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, MutexItem> _mutexCollection = new Dictionary<string, MutexItem>();
        private IUtcTime _utcTime;

        public InMemoryMutex()
        {
            _utcTime = new SystemUtcTime();
        }

        /// <summary>
        ///     Used to override the default usage of DateTime.UtcNow.
        /// </summary>
        /// <param name="time"></param>
        public void Using(IUtcTime time)
        {
            _utcTime = time;
        }

        public void Release(string key)
        {
            lock (_lock)
            {
                if (!_mutexCollection.TryGetValue(key, out var mutex))
                {
                    return;
                }

                mutex.Locked = false;
                mutex.ExpiresAt = null;
            }
        }

        public bool TryGetLock(string key, int timeoutMinutes)
        {
            lock (_lock)
            {
                if (!_mutexCollection.TryGetValue(key, out var mutex))
                {
                    return CreateLockedMutex(key, timeoutMinutes);
                }

                if (!mutex.Locked)
                {
                    return CreateLockedMutex(key, timeoutMinutes);
                }

                return _utcTime.Now >= mutex.ExpiresAt && CreateLockedMutex(key, timeoutMinutes);
            }
        }

        private bool CreateLockedMutex(string key, int timeoutMinutes)
        {
            DateTime? expiresAt = _utcTime.Now.AddMinutes(timeoutMinutes);

            if (_mutexCollection.TryGetValue(key, out var mutex))
            {
                mutex.Locked = true;
                mutex.ExpiresAt = expiresAt;
            }
            else
            {
                _mutexCollection.Add(key,
                                     new MutexItem
                                     {
                                         Locked = true,
                                         ExpiresAt = expiresAt
                                     });
            }

            return true;
        }

        private class MutexItem
        {
            public DateTime? ExpiresAt { get; set; }
            public bool Locked { get; set; }
        }
    }
}