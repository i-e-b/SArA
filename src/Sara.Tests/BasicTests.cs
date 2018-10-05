using NUnit.Framework;

namespace Sara.Tests
{
    [TestFixture]
    public class BasicTests
    {
        [Test]
        public void can_allocate_memory_from_a_pool_and_get_a_pointer()
        {
            var subject = new Allocator(100, Mega.Bytes(10));

            long ptr = subject.Alloc(byteCount: Kilo.Bytes(1));

            Assert.That(ptr, Is.GreaterThanOrEqualTo(100));
        }

        [Test]
        public void a_second_allocation_returns_different_memory()
        {
            var subject = new Allocator(100, Mega.Bytes(10));

            long ptr1 = subject.Alloc(byteCount: 256);
            long ptr2 = subject.Alloc(byteCount: 256);

            Assert.That(ptr1, Is.Not.EqualTo(ptr2));
        }

        [Test]
        public void can_directly_deallocate_a_pointer()
        {
            var subject = new Allocator(100, Mega.Bytes(10));

            long ptr = subject.Alloc(byteCount: 256);
            subject.Deref(ptr);

            var ar = subject.CurrentArena();
            var refs = subject.ArenaRefCount(ar);

            Assert.That(refs, Is.Zero);
        }

        /* This requires implementing back-step logic -- which I might do at some point
        [Test]
        public void deallocating_the_most_recently_allocated_block_returns_the_same_pointer()
        {
            // Each arena acts a bit like a stack
            var subject = new Allocator(100, Mega.Bytes(10));

            long ptr1 = subject.Alloc(byteCount: 256);
            subject.Deref(ptr1);
            long ptr2 = subject.Alloc(byteCount: 512);
            
            Assert.That(ptr1, Is.EqualTo(ptr2));
        }*/

        [Test]
        public void deallocating_an_old_allocation_does_nothing ()
        {
            // Older items just hang around until the entire arena is abandoned
            var subject = new Allocator(100, Mega.Bytes(10));

            long ptr1 = subject.Alloc(byteCount: 256);
            long ptr2 = subject.Alloc(byteCount: 256);
            subject.Deref(ptr1);
            long ptr3 = subject.Alloc(byteCount: 512);
            
            Assert.That(ptr3, Is.GreaterThan(ptr2));
        }

        [Test]
        public void can_add_and_remove_current_referenced_pointers ()
        {
            // This increments and decrements ref counts?
            // Free is the equivalent of directly setting ref count to zero? Or is it just a synonym for Deref?
            // We can keep an overall refcount for the arena and ignore the individual references (except for the head, as an optimisation)
            // We don't protect from double-free
            
            var subject = new Allocator(100, Mega.Bytes(1));
            
            long ptr = subject.Alloc(byteCount: 256);
            subject.Reference(ptr);
            subject.Reference(ptr);
            subject.Deref(ptr);

            Assert.Pass();
        }

        [Test]
        public void allocating_enough_memory_changes_arena()
        {
            var subject = new Allocator(100, Mega.Bytes(1));

            int first = subject.CurrentArena();
            long ptr1 = subject.Alloc(Allocator.ArenaSize);
            long ptr2 = subject.Alloc(Kilo.Bytes(1));
            int second = subject.CurrentArena();

            Assert.That(ptr1, Is.Not.EqualTo(ptr2));
            Assert.That(first, Is.Not.EqualTo(second));
        }

        [Test]
        public void deallocating_everything_in_an_arena_resets_it ()
        {
            var subject = new Allocator(100, Mega.Bytes(1));

            long ptr1 = subject.Alloc(512);
            long ptr2 = subject.Alloc(512);
            long ptr3 = subject.Alloc(512);

            subject.Deref(ptr1);
            subject.Deref(ptr2);
            
            long ptr4 = subject.Alloc(512);

            subject.Deref(ptr3);
            subject.Deref(ptr4);

            // should be reset now, and next alloc goes back to start
            long ptrFinal = subject.Alloc(512);

            Assert.That(ptrFinal, Is.EqualTo(ptr1));
        }

        [Test]
        public void running_a_scan_when_any_pointers_are_referenced_keeps_the_arena()
        {
            // scan takes a list of known references, and assumes anything
            // that's not in the list in not referenced. Any arena with nothing
            // referenced is reset.
            
            var bump = (Allocator.ArenaSize / 4) + 1; // three fit in each arena, with some spare
            var subject = new Allocator(512, Mega.Bytes(1));

            var x1 = subject.Alloc(bump);
            var ar1 = subject.CurrentArena();

            subject.Alloc(bump);subject.Alloc(bump); // fill up first arena
            
            var x2 = subject.Alloc(bump);
            var ar2 = subject.CurrentArena();

            Assert.That(ar1, Is.Not.EqualTo(ar2), "Failed to trigger a new arena");

            // Check that both arenas are non-empty:
            Assert.That(subject.GetArenaOccupation(ar1), Is.Not.Zero);
            Assert.That(subject.GetArenaOccupation(ar2), Is.Not.Zero);

            // Run the scan (note we don't need all pointers, just one from each arena)
            subject.ScanAndSweep(new []{ x1, x2 });
            
            // Check nothing has been cleared
            Assert.That(subject.GetArenaOccupation(ar1), Is.Not.Zero);
            Assert.That(subject.GetArenaOccupation(ar2), Is.Not.Zero);
        }

        [Test]
        public void running_a_scan_when_no_pointers_are_referenced_resets_the_arena()
        {
            var bump = (Allocator.ArenaSize / 4) + 1; // three fit in each arena, with some spare
            var subject = new Allocator(512, Mega.Bytes(1));

            subject.Alloc(bump);
            var ar1 = subject.CurrentArena();

            subject.Alloc(bump);subject.Alloc(bump); // fill up first arena
            
            var x2 = subject.Alloc(bump);
            var ar2 = subject.CurrentArena();

            Assert.That(ar1, Is.Not.EqualTo(ar2), "Failed to trigger a new arena");

            // Check that both arenas are non-empty:
            Assert.That(subject.GetArenaOccupation(ar1), Is.Not.Zero);
            Assert.That(subject.GetArenaOccupation(ar2), Is.Not.Zero);

            // Run the scan (only including the second arena)
            subject.ScanAndSweep(new []{ x2 });
            
            // Check nothing has been cleared
            Assert.That(subject.GetArenaOccupation(ar1), Is.Zero, "Unreferenced arena was not reset");
            Assert.That(subject.GetArenaOccupation(ar2), Is.Not.Zero);
        }

        [Test] // todo: do something better once the rest is working?
        public void requesting_a_block_larger_than_a_single_area_fails()
        {
            // Doing this to keep things very simple
            var subject = new Allocator(512, Mega.Bytes(1));

            var result = subject.Alloc(Allocator.ArenaSize * 2);

            Assert.That(result, Is.EqualTo(Allocator.INVALID_ALLOC));
        }

        [Test]
        public void memory_exhaustion_results_in_an_error_code ()
        {
            var subject = new Allocator(10, Mega.Bytes(1)); // big for an embedded system. 3GB total.

            long result = 0;

            for (int i = 0; i < 17; i++)
            {
                result = subject.Alloc(Allocator.ArenaSize - 1);
            }

            Assert.That(result, Is.EqualTo(Allocator.OUT_OF_MEMORY));
        }

        [Test] // a stress test of sorts
        public void can_handle_large_allocation_spaces ()
        {
            var subject = new Allocator(Giga.Bytes(1), Giga.Bytes(2)); // big for an embedded system. 3GB total.

            var result = subject.Alloc(Allocator.ArenaSize / 2);
            Assert.That(result, Is.EqualTo(Giga.Bytes(1))); // allocated at bottom

            for (int i = 0; i < 1000; i++)
            {
                result = subject.Alloc(Allocator.ArenaSize - 1);
            }

            Assert.That(subject.CurrentArena(), Is.GreaterThanOrEqualTo(1000));
            Assert.That(result, Is.GreaterThan(Giga.Bytes(1)));
        }
    }
}
