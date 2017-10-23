using System;
using System.Threading;
using System.Threading.Tasks;
using lcache;

namespace lcache_console
{
    class Program
    {
        private static LMemoryCache<string> _cache = new LMemoryCache<string>(null, (s, ex) => Console.WriteLine(s + (ex != null ? " " + ex.Message : "")));

        static void Main(string[] args)
        {
            Console.WriteLine(DateTimeOffset.Now);

            while (true)
            {
                Console.WriteLine(GetKey("1"));

                Thread.Sleep(3000);
            }
        }

        static string GetKey(string key)
        {
            return _cache.GetOrAdd(key, async k => {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                return _cache.Result("1 value", new DateTimeOffset(DateTime.Now.AddSeconds(5)));
            });
        }
    }
}
