using System;
using NUnit.Framework;

namespace Sara.Tests
{
    [TestFixture]
    public class HashMapTests{
        [Test]
        public void can_store_and_retrieve_items()
        {
            var subject = new TaggedHashMap(64, new Allocator(0, Mega.Bytes(1)), new MemorySimulator(Mega.Bytes(1)));
            subject.Add(50000000, 123);
            subject.Add(1, 456);

            var ok1 = subject.TryGetValue(1, out var val1);
            var ok2 = subject.TryGetValue(50000000, out var val2);
            var ok3 = subject.TryGetValue(50, out _);

            Assert.That(ok1, Is.True);
            Assert.That(ok2, Is.True);
            Assert.That(ok3, Is.False);

            Assert.That(val1, Is.EqualTo(456));
            Assert.That(val2, Is.EqualTo(123));
        }

        [Test]
        public void can_remove_values ()
        {
            var subject = new TaggedHashMap(64, new Allocator(0, Mega.Bytes(1)), new MemorySimulator(Mega.Bytes(1)));
            subject.Add(50000000, 123);
            subject.Add(1, 456);

            var ok1 = subject.TryGetValue(1, out _);
            var ok2 = subject.TryGetValue(50000000, out _);
            var ok3 = subject.TryGetValue(50, out _);

            Assert.That(ok1, Is.True);
            Assert.That(ok2, Is.True);
            Assert.That(ok3, Is.False);

            // remove one value at beginning
            subject.Remove(1);
            
            ok1 = subject.TryGetValue(1, out _);
            ok2 = subject.TryGetValue(50000000, out _);
            ok3 = subject.TryGetValue(50, out _);

            Assert.That(ok1, Is.False);
            Assert.That(ok2, Is.True);
            Assert.That(ok3, Is.False);
        }

        [Test]
        public void stress_test()
        {
            var rnd = new Random();
            var subject = new TaggedHashMap(32000, new Allocator(0, Mega.Bytes(20)), new MemorySimulator(Mega.Bytes(20)));

            subject.Add(0, 1);
            for (int i = 0; i < 20000; i++) // performance falls off a cliff between 10K and 100K ops
                                            // probably due to the vector impl
            {
                subject.Add((ulong) rnd.Next(1, 1000000), (ulong) i);
                subject.Remove((ulong) rnd.Next(1, 1000000));
            }

            Assert.That(subject.Count, Is.GreaterThan(1000)); // there will probably be key collisions
            
            var ok = subject.TryGetValue(0, out var val);
            Assert.That(ok, Is.True);
            Assert.That(val, Is.EqualTo(1));
        }
    }
}