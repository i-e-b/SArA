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

            var ok1 = subject.Get(1, out var val1);
            var ok2 = subject.Get(50000000, out var val2);
            var ok3 = subject.Get(50, out _);

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

            var ok1 = subject.Get(1, out _);
            var ok2 = subject.Get(50000000, out _);
            var ok3 = subject.Get(50, out _);

            Assert.That(ok1, Is.True);
            Assert.That(ok2, Is.True);
            Assert.That(ok3, Is.False);

            // remove one value at beginning
            subject.Remove(1);
            
            ok1 = subject.Get(1, out _);
            ok2 = subject.Get(50000000, out _);
            ok3 = subject.Get(50, out _);

            Assert.That(ok1, Is.False);
            Assert.That(ok2, Is.True);
            Assert.That(ok3, Is.False);
        }

        [Test]
        public void stress_test()
        {
            var rnd = new Random();
            // we deliberately use a small initial size to stress the scaling.
            // if you can afford to oversize the map, that will make things a lot faster
            var subject = new TaggedHashMap(10000, new Allocator(0, Mega.Bytes(50)), new MemorySimulator(Mega.Bytes(50)));

            subject.Add(0, 1);
            for (int i = 0; i < /*100000*/ 25000; i++) // 25'000 should be under a second
            {
                if (!subject.Put((ulong)rnd.Next(1, 1000000), (ulong)i, true)) break;//Assert.Fail("Put rejected the change");
                subject.Remove((ulong)rnd.Next(1, 1000000));
            }

            Assert.That(subject.Count, Is.GreaterThan(1000)); // there will probably be key collisions

            var ok = subject.Get(0, out var val);
            Assert.That(ok, Is.True);
            Assert.That(val, Is.EqualTo(1));
        }

        [Test]
        public void deallocating_the_hash_map_releases_memory ()
        {
            var alloc = new Allocator(Mega.Bytes(10), Mega.Bytes(20));
            var subject = new TaggedHashMap(256, alloc, new MemorySimulator(Mega.Bytes(20)));

            for (ulong i = 0; i < 128; i++)
            {
                subject.Add(i, i * 2);
            }

            
            // Check that memory is used...
            alloc.GetState(out var allocatedBytes, out var unallocatedBytes, out var occupiedArenas, out var emptyArenas, out var totalReferenceCount, out var largestContiguous);
            Assert.That(allocatedBytes, Is.GreaterThanOrEqualTo(6000), "Allocated bytes looks too small");
            Assert.That(unallocatedBytes, Is.LessThan(Mega.Bytes(10)), "Unallocated bytes looks too big");
            Assert.That(occupiedArenas, Is.EqualTo(1), "Occupied arenas looks wrong");
            Assert.That(emptyArenas, Is.GreaterThanOrEqualTo(100), "Empty arenas looks wrong");
            Assert.That(totalReferenceCount, Is.GreaterThan(2), "Reference count looks wrong");
            Assert.That(largestContiguous, Is.EqualTo(Allocator.ArenaSize), "Should not have exhausted memory!");

            // Release the hash map
            subject.Deallocate();

            // Check that everything is released
            alloc.GetState(out allocatedBytes, out unallocatedBytes, out occupiedArenas, out emptyArenas, out totalReferenceCount, out largestContiguous);
            Assert.That(allocatedBytes, Is.EqualTo(0), "Memory was not freed");
            Assert.That(unallocatedBytes, Is.GreaterThan(Mega.Bytes(9)), "Unallocated bytes not correct");
            Assert.That(occupiedArenas, Is.EqualTo(0), "Some arenas still occupied");
            Assert.That(emptyArenas, Is.GreaterThanOrEqualTo(100), "Empty arenas looks wrong");
            Assert.That(totalReferenceCount, Is.EqualTo(0), "Some references still dangling");
            Assert.That(largestContiguous, Is.EqualTo(Allocator.ArenaSize), "Should not have exhausted memory!");
        }

        [Test]
        public void a_hashmap_with_contents_can_be_cleared_and_reused ()
        {
            var subject = new TaggedHashMap(64, new Allocator(0, Mega.Bytes(1)), new MemorySimulator(Mega.Bytes(1)));
            Assert.That(subject.Count, Is.Zero, "New hash map not empty?");
            
            // first run
            subject.Add(1, 123);
            subject.Add(2, 456);
            subject.Add(3, 456);
            Assert.That(subject.Count, Is.Not.Zero, "Failed to write test data");

            // wipe out
            subject.Clear();
            Assert.That(subject.Count, Is.Zero, "Hashmap not cleared");

            // second run to show it still works
            subject.Add(4, 123);
            subject.Add(3, 456);
            subject.Add(2, 456);

            Assert.That(subject.Count, Is.Not.Zero, "Failed to write data after clearing");
        }

        [Test]
        public void can_check_for_presence_of_a_key ()
        {
            var subject = new TaggedHashMap(64, new Allocator(0, Mega.Bytes(1)), new MemorySimulator(Mega.Bytes(1)));

            subject.Add(123, 0);
            subject.Add(1, 456);

            Assert.That(subject.ContainsKey(123), Is.True);
            Assert.That(subject.ContainsKey(321), Is.False);
        }

        [Test]
        public void put_can_replace_an_existing_value ()
        {
            var subject = new TaggedHashMap(64, new Allocator(0, Mega.Bytes(1)), new MemorySimulator(Mega.Bytes(1)));

            subject.Put(1, 1, true);
            subject.Put(1, 2, true);  // overwrite
            subject.Put(1, 3, false); // silently abort

            subject.Get(1, out var result);
            Assert.That(result, Is.EqualTo(2));
        }

    }
}