using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace lcache
{
    public class LMemoryCache<T>
    {
        public class CacheItem
        {
            public T Value { get; set; }
            public DateTimeOffset AbsoluteExpiration { get; set; }
        }

        private IDictionary<string, object> _cache;

        public CacheItem Result(T value, DateTimeOffset absoluteExpiration)
        {
            return new CacheItem() { Value = value, AbsoluteExpiration = absoluteExpiration };
        }

        private Action<string, Exception> _logger;

        public LMemoryCache(IDictionary<string, object> cache = null, Action<string, Exception> logger = null)
        {
            _cache = cache ?? new Dictionary<string, object>();
            _logger = logger ?? ((s, ex) => {});
        }

        private object GetLock(string key)
        {
            return string.Intern(key);
        }

        public T GetOrAdd(string key, Func<string, CacheItem> create)
        {
            return GetOrAdd(key, k => Task.FromResult(create(k)));
        }

        public T GetOrAdd(string key, Func<string, Task<CacheItem>> create)
        {
            CacheItem item = Get(key);

            if (item != null)
            {
                _logger("item found", null);

                if (IsStale(item))
                {
                    _logger("item stale: " + item.AbsoluteExpiration, null);

                    lock (GetLock(key))
                    {
                        if (IsStale(item))
                        {
                            _logger("item stale, 2nd try", null);

                            // Mark NOT stale so no other thread will try to do the update
                            item.AbsoluteExpiration = DateTimeOffset.MaxValue;

                            // Kick off background reload
                            _logger("kicking off background update", null);
                            Task.Run(() => {
                                lock (GetLock(key))
                                {
                                    Set(key, item, create);
                                    _logger("background update complete", null);
                                }
                            });
                        }
                    }
                }
                else
                {
                    _logger("item valid: " + item.AbsoluteExpiration.ToString(), null);
                }

                return item.Value;
            }

            _logger("item missing", null);

            lock (GetLock(key))
            {
                item = Get(key);
                if (item == null)
                {
                    _logger("item missing, 2nd try", null);
                    item = Set(key, null, create).Result;
                }

                return item.Value;
            }
        }

        private Task<CacheItem> Set(string key, CacheItem existingItem, Func<string, Task<CacheItem>> create)
        {
            _logger("loading item", null);

            try
            {
                return create(key)
                    .ContinueWith(t => {

                        if (t.IsFaulted)
                        {
                            return HandleError(existingItem, t.Exception);
                        }
                        _logger("loaded item with expiration: " + t.Result.AbsoluteExpiration, null);

                        _cache[key] = t.Result;
                        return t.Result;
                    });
            }
            catch (Exception ex)
            {
                // TODO define this in ctor
                return Task.FromResult(HandleError(existingItem, ex));
            }
        }

        private CacheItem HandleError(CacheItem item, Exception ex)
        {
            _logger("Loading item failed", ex);
            if (item == null)
            {
                item = new CacheItem();
            }
            item.AbsoluteExpiration = DateTimeOffset.Now.AddSeconds(10);
            return item;
        }

        private static bool IsStale(CacheItem item)
        {
            return item.AbsoluteExpiration.CompareTo(DateTimeOffset.Now) <= 0;
        }

        private CacheItem Get(string key)
        {
            object item;
            if (_cache.TryGetValue(key, out item) && item != null)
            {
                return item as CacheItem;
            }

            return null;
        }
    }
}
