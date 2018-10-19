using System;
using Sara;

namespace StressTestingApp
{
    class Program
    {
        public const int TestSize = 10_000_000; // large enough to keep the program running for a while
        static void Main(string[] args)
        {
            var RamSize = Giga.Bytes(1);
            var rnd = new Random();

            var subject = new TaggedHashMap(TestSize, new Allocator(0, RamSize), new MemorySimulator(RamSize));
            
            Console.WriteLine("running...");

            subject.Add(0, 1);
            for (int i = 0; i < TestSize; i++) // 100'000 should have an acceptable run time. 25'000 should be well under a second
            {
                if (!subject.Put((ulong)rnd.Next(1, 1000000), (ulong)i, true)) throw new Exception("Bad push");
                subject.Remove((ulong)rnd.Next(1, 1000000));
            }

            Console.WriteLine("done");
        }
    }
}
