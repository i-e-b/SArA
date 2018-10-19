using NUnit.Framework;

namespace Sara.Tests
{
    [TestFixture]
    public class AllocatorTests
    {
        [Test]
        public void can_allocate_memory_from_a_pool_and_get_a_pointer()
        {
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Allocator(100, Mega.Bytes(10), mem);

            long ptr = subject.Alloc(byteCount: Kilo.Bytes(1)).Value;

            Assert.That(ptr, Is.GreaterThanOrEqualTo(100));
        }

        [Test]
        public void a_second_allocation_returns_different_memory()
        {
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Allocator(100, Mega.Bytes(10), mem);

            long ptr1 = subject.Alloc(byteCount: 256).Value;
            long ptr2 = subject.Alloc(byteCount: 256).Value;

            Assert.That(ptr1, Is.Not.EqualTo(ptr2));
        }

        [Test]
        public void can_directly_deallocate_a_pointer()
        {
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Allocator(100, Mega.Bytes(10), mem);

            long ptr = subject.Alloc(byteCount: 256).Value;
            subject.Deref(ptr);

            var ar = subject.CurrentArena();
            var refs = subject.ArenaRefCount(ar);

            Assert.That(refs.Value, Is.Zero);
        }

        [Test]
        public void deallocating_an_old_allocation_does_nothing ()
        {
            // Older items just hang around until the entire arena is abandoned
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Allocator(100, Mega.Bytes(10), mem);

            long ptr1 = subject.Alloc(byteCount: 256).Value;
            long ptr2 = subject.Alloc(byteCount: 256).Value;
            subject.Deref(ptr1);
            long ptr3 = subject.Alloc(byteCount: 512).Value;
            
            Assert.That(ptr3, Is.GreaterThan(ptr2));
        }

        [Test]
        public void can_add_and_remove_current_referenced_pointers ()
        {
            // This increments and decrements ref counts?
            // Free is the equivalent of directly setting ref count to zero? Or is it just a synonym for Deref?
            // We can keep an overall refcount for the arena and ignore the individual references (except for the head, as an optimisation)
            // We don't protect from double-free
            
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Allocator(100, Mega.Bytes(1), mem);
            
            long ptr = subject.Alloc(byteCount: 256).Value;
            subject.Reference(ptr);
            subject.Reference(ptr);
            subject.Deref(ptr);

            Assert.Pass();
        }

        [Test]
        public void allocating_enough_memory_changes_arena()
        {
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Allocator(100, Mega.Bytes(1), mem);

            int first = subject.CurrentArena();
            long ptr1 = subject.Alloc(Allocator.ArenaSize).Value;
            long ptr2 = subject.Alloc(Kilo.Bytes(1)).Value;
            int second = subject.CurrentArena();

            Assert.That(ptr1, Is.Not.EqualTo(ptr2));
            Assert.That(first, Is.Not.EqualTo(second));
        }

        [Test]
        public void deallocating_everything_in_an_arena_resets_it ()
        {
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Allocator(100, Mega.Bytes(1), mem);

            long ptr1 = subject.Alloc(512).Value;
            long ptr2 = subject.Alloc(512).Value;
            long ptr3 = subject.Alloc(512).Value;

            subject.Deref(ptr1);
            subject.Deref(ptr2);
            
            long ptr4 = subject.Alloc(512).Value;

            subject.Deref(ptr3);
            subject.Deref(ptr4);

            // should be reset now, and next alloc goes back to start
            long ptrFinal = subject.Alloc(512).Value;

            Assert.That(ptrFinal, Is.EqualTo(ptr1));
        }

        [Test]
        public void running_a_scan_when_any_pointers_are_referenced_keeps_the_arena()
        {
            // scan takes a list of known references, and assumes anything
            // that's not in the list in not referenced. Any arena with nothing
            // referenced is reset.
            
            var mem = new MemorySimulator(Mega.Bytes(1));
            var bump = (Allocator.ArenaSize / 4) + 1; // three fit in each arena, with some spare
            var subject = new Allocator(512, Mega.Bytes(1), mem);

            var x1 = subject.Alloc(bump).Value;
            var ar1 = subject.CurrentArena();

            subject.Alloc(bump);subject.Alloc(bump); // fill up first arena
            
            var x2 = subject.Alloc(bump).Value;
            var ar2 = subject.CurrentArena();

            Assert.That(ar1, Is.Not.EqualTo(ar2), "Failed to trigger a new arena");

            // Check that both arenas are non-empty:
            Assert.That(subject.GetArenaOccupation(ar1).Value, Is.Not.Zero);
            Assert.That(subject.GetArenaOccupation(ar2).Value, Is.Not.Zero);

            // Run the scan (note we don't need all pointers, just one from each arena)
            subject.ScanAndSweep(new []{ x1, x2 });
            
            // Check nothing has been cleared
            Assert.That(subject.GetArenaOccupation(ar1).Value, Is.Not.Zero);
            Assert.That(subject.GetArenaOccupation(ar2).Value, Is.Not.Zero);
        }

        [Test]
        public void running_a_scan_when_no_pointers_are_referenced_resets_the_arena()
        {
            var bump = (Allocator.ArenaSize / 4) + 1; // three fit in each arena, with some spare
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Allocator(512, Mega.Bytes(1), mem);

            subject.Alloc(bump);
            var ar1 = subject.CurrentArena();

            subject.Alloc(bump);subject.Alloc(bump); // fill up first arena
            
            var x2 = subject.Alloc(bump).Value;
            var ar2 = subject.CurrentArena();

            Assert.That(ar1, Is.Not.EqualTo(ar2), "Failed to trigger a new arena");

            // Check that both arenas are non-empty:
            Assert.That(subject.GetArenaOccupation(ar1).Value, Is.Not.Zero);
            Assert.That(subject.GetArenaOccupation(ar2).Value, Is.Not.Zero);

            // Run the scan (only including the second arena)
            subject.ScanAndSweep(new []{ x2 });
            
            // Check nothing has been cleared
            Assert.That(subject.GetArenaOccupation(ar1).Value, Is.Zero, "Unreferenced arena was not reset");
            Assert.That(subject.GetArenaOccupation(ar2).Value, Is.Not.Zero);
        }

        [Test] // The vector class can be used to store larger chunks of data
        public void requesting_a_block_larger_than_a_single_area_fails()
        {
            // Doing this to keep things very simple
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Allocator(512, Mega.Bytes(1), mem);

            var result = subject.Alloc(Allocator.ArenaSize * 2);

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void memory_exhaustion_results_in_an_error_code ()
        {
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Allocator(10, Mega.Bytes(1), mem);

            Result<long> result = default;

            for (int i = 0; i < 17; i++)
            {
                result = subject.Alloc(Allocator.ArenaSize - 1);
            }
            
            Assert.That(result.Success, Is.False);
        }

        [Test] // a stress test of sorts
        public void can_handle_large_allocation_spaces ()
        {
            var mem = new OffsetMemorySimulator(Mega.Bytes(1), Giga.Bytes(1)); // only need enough room for arena tables
            var subject = new Allocator(Giga.Bytes(1), Giga.Bytes(2), mem); // big for an embedded system. 3GB total.

            var result = subject.Alloc(Allocator.ArenaSize / 2);
            Assert.That(result.Value - Giga.Bytes(1), Is.EqualTo(131072)); // allocated at bottom of given space, excluding arena tables

            for (int i = 0; i < 1000; i++)
            {
                result = subject.Alloc(Allocator.ArenaSize - 1);
            }

            Assert.That(subject.CurrentArena(), Is.GreaterThanOrEqualTo(1000));
            Assert.That(result.Value, Is.GreaterThan(Giga.Bytes(1)));
            
            // Test a scan
            subject.ScanAndSweep(new long[0]);
            Assert.That(subject.CurrentArena(), Is.Zero);
        }

        [Test]
        public void can_read_the_current_allocation_pressure ()
        {
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Allocator(10, Mega.Bytes(1), mem); // this is 1048576, which doesn't divide nicely into arenas...

            // Check the empty state is sane
            subject.GetState(out var allocatedBytes, out var unallocatedBytes, out var occupiedArenas, out var emptyArenas, out var refCount, out var largestBlock);

            Assert.That(allocatedBytes, Is.Zero);
            Assert.That(unallocatedBytes, Is.EqualTo(1048560)); // ... so we end up with slightly less space that's usable. Max loss is < 64K
            Assert.That(largestBlock, Is.EqualTo(Allocator.ArenaSize));

            Assert.That(occupiedArenas, Is.Zero);
            Assert.That(emptyArenas, Is.EqualTo(16)); // arenas per megabyte
            Assert.That(refCount, Is.Zero);

            // Do some allocation
            var allocd = 0L;
            var size = Allocator.ArenaSize / 6;
            for (int i = 0; i < 14; i++)
            {
                allocd += size;
                subject.Alloc(size);
            }

            // Check filled state is sane
            subject.GetState(out allocatedBytes, out unallocatedBytes, out occupiedArenas, out emptyArenas, out refCount, out largestBlock);

            Assert.That(allocatedBytes, Is.EqualTo(allocd));
            Assert.That(unallocatedBytes, Is.EqualTo(1048560 - allocd));
            Assert.That(largestBlock, Is.EqualTo(Allocator.ArenaSize));

            Assert.That(occupiedArenas, Is.EqualTo(3));
            Assert.That(emptyArenas, Is.EqualTo(13));
            Assert.That(refCount, Is.EqualTo(14));
        }
    }
}
