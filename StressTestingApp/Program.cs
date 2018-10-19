using System;
using Sara;

namespace StressTestingApp
{
    class Program
    {
        static void Main(string[] args)
        {
            var rnd = new Random();
            var subject = new TaggedHashMap(100000, new Allocator(0, Mega.Bytes(50)), new MemorySimulator(Mega.Bytes(50)));
            
            Console.WriteLine("running...");

            subject.Add(0, 1);
            for (int i = 0; i < 100000; i++) // 100'000 should have an acceptable run time. 25'000 should be well under a second
            {
                if (!subject.Put((ulong)rnd.Next(1, 1000000), (ulong)i, true)) throw new Exception("Bad push");
                subject.Remove((ulong)rnd.Next(1, 1000000));
            }

            Console.WriteLine("done");
        }
    }
}
