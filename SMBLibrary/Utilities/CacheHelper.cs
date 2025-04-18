using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace SMBLibrary.Utilities
{
    public class CacheItem
    {
        public object CacheValue { get; set; }
        public DateTime AddTime { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiredTime { get; set; }
        public object LockObject { get; } = new object();
    }

    public class CacheHelper
    {
        private const int CacheClearTime = 60; // 缓存时间秒
        private const int MaxCacheCount = 10000;
        private static readonly ConcurrentDictionary<string, Lazy<CacheItem>> _caches = new ConcurrentDictionary<string, Lazy<CacheItem>>();
        private static readonly System.Timers.Timer _clearTimer;
        private static readonly SemaphoreSlim _clearSemaphore = new SemaphoreSlim(1, 1);
        private static bool _isClearing;

        static CacheHelper()
        {
            _clearTimer = new System.Timers.Timer(5000) { AutoReset = true };
            _clearTimer.Elapsed += async (sender, e) => await ClearExpiredCachesAsync();
            _clearTimer.Start();
        }

        public static int Count => _caches.Count;


        public static void Set(string key, object value, int expiredTime = CacheClearTime)
        {
            _caches[key] = new Lazy<CacheItem>(() => new CacheItem
            {
                CacheValue = value,
                ExpiredTime = DateTime.UtcNow.AddSeconds(expiredTime)
            });

            if (_caches.Count > MaxCacheCount && !_isClearing)
            {
                _ = ClearExpiredCachesAsync();
            }
        }

        public static object Get(string key, double expiredTime = CacheClearTime, bool enableSlidingExpiration = false)
        {
            if (!_caches.TryGetValue(key, out var lazyCacheItem))
            {
                return null;
            }

            var cacheItem = lazyCacheItem.Value;
            if (IsExpired(cacheItem, expiredTime))
            {
                RemoveCaches(new[] { key });
                return null;
            }

            if (enableSlidingExpiration)
            {
                lock (cacheItem.LockObject)
                {
                    cacheItem.AddTime = DateTime.UtcNow;
                    cacheItem.ExpiredTime = DateTime.UtcNow.AddSeconds(expiredTime);
                }
            }
            return cacheItem.CacheValue;
        }

        public static T TryGet<T>(string key, Func<object> func = null, double expiredTime = CacheClearTime, bool enableSlidingExpiration = false)
        {
            var cache = Get(key, expiredTime, enableSlidingExpiration);
            if (cache != null)
            {
                return (T)cache;
            }

            if (func != null)
            {
                var lazyValue = _caches.GetOrAdd(key, k => new Lazy<CacheItem>(() =>
                {
                    var value = func();
                    return new CacheItem
                    {
                        CacheValue = value,
                        ExpiredTime = DateTime.UtcNow.AddSeconds(expiredTime),
                        AddTime = DateTime.UtcNow
                    };
                }));
                cache = lazyValue.Value.CacheValue;
            }

            return (T)cache;
        }

        private static bool IsExpired(CacheItem cacheItem, double expiredTime = CacheClearTime)
        {
            if (cacheItem.ExpiredTime.HasValue)
            {
                return cacheItem.ExpiredTime.Value < DateTime.UtcNow;
            }
            return (DateTime.UtcNow - cacheItem.AddTime).TotalSeconds > expiredTime;
        }

        private static async Task ClearExpiredCachesAsync()
        {
            if (!await _clearSemaphore.WaitAsync(0))
            {
                return;
            }


            try
            {
                _isClearing = true;
                var keysToRemove = new List<string>();
                foreach (var kvp in _caches)
                {
                    var cacheItem = kvp.Value.Value;
                    if (IsExpired(cacheItem))
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
                RemoveCaches(keysToRemove);
            }
            finally
            {
                _isClearing = false;
                _clearSemaphore.Release();
            }
        }

        public static void RemoveCaches(IEnumerable<string> keys)
        {
            foreach (string key in keys)
            {
                _caches.TryRemove(key, out _);
            }
        }

        public static void RemovePreCaches(string[] keys)
        {
            var allKeysToRemove = new List<string>();
            foreach (string key in keys)
            {
                var cacheKeys = _caches.Keys.Where(cacheKey => cacheKey.StartsWith(key));
                allKeysToRemove.AddRange(cacheKeys);
            }
            RemoveCaches(allKeysToRemove);
        }
    }
}