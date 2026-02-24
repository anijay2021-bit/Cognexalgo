using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cognexalgo.Core.Services
{
    /// <summary>
    /// Rate limiter to prevent API throttling
    /// Ensures we don't exceed Angel One's rate limits
    /// </summary>
    public class ApiRateLimiter
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly int _maxRequestsPerSecond;
        private readonly Queue<DateTime> _requestTimestamps = new();
        private readonly object _lock = new();

        public ApiRateLimiter(int maxRequestsPerSecond = 10)
        {
            _maxRequestsPerSecond = maxRequestsPerSecond;
            _semaphore = new SemaphoreSlim(1, 1);
        }

        /// <summary>
        /// Wait if necessary to comply with rate limits
        /// </summary>
        public async Task WaitAsync()
        {
            await _semaphore.WaitAsync();

            try
            {
                var now = DateTime.UtcNow;
                var oneSecondAgo = now.AddSeconds(-1);

                lock (_lock)
                {
                    // Remove timestamps older than 1 second
                    while (_requestTimestamps.Count > 0 && _requestTimestamps.Peek() < oneSecondAgo)
                    {
                        _requestTimestamps.Dequeue();
                    }

                    // Check if we've hit the limit
                    if (_requestTimestamps.Count >= _maxRequestsPerSecond)
                    {
                        var oldestTimestamp = _requestTimestamps.Peek();
                        var waitTime = oldestTimestamp.AddSeconds(1) - now;

                        if (waitTime > TimeSpan.Zero)
                        {
                            // Use synchronous wait to avoid async in lock
                            Task.Delay(waitTime).Wait();
                        }
                    }

                    // Record this request
                    _requestTimestamps.Enqueue(DateTime.UtcNow);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Get current request count in the last second
        /// </summary>
        public int GetCurrentRequestCount()
        {
            lock (_lock)
            {
                var oneSecondAgo = DateTime.UtcNow.AddSeconds(-1);
                while (_requestTimestamps.Count > 0 && _requestTimestamps.Peek() < oneSecondAgo)
                {
                    _requestTimestamps.Dequeue();
                }
                return _requestTimestamps.Count;
            }
        }
    }
}
