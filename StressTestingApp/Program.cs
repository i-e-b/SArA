using System;
using Sara;

namespace StressTestingApp
{
    class Program
    {
        static void Main(string[] args)
        {
            var rnd = new Random();
            // we deliberately use a small initial size to stress the scaling.
            // if you can afford to oversize the map, that will make things a lot faster
            var subject = new TaggedHashMap(10000, new Allocator(0, Mega.Bytes(50)), new MemorySimulator(Mega.Bytes(50)));
            
            Console.WriteLine("running...");

            subject.Add(0, 1);
            for (int i = 0; i < 100000; i++) // 100'000 should have an acceptable run time. 25'000 should be well under a second
            {
                if (!subject.Put((ulong)rnd.Next(1, 1000000), (ulong)i, true)) break;//Assert.Fail("Put rejected the change");
                subject.Remove((ulong)rnd.Next(1, 1000000));
            }

            Console.WriteLine("done");
        }
    }
}
