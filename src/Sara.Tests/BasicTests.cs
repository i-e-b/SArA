using NUnit.Framework;

namespace Sara.Tests
{
    [TestFixture]
    public class BasicTests
    {
        [Test]
        public void can_allocate_memory_from_a_pool_and_get_a_pointer (){
            var subject = new Allocator(100, 10000);

            long ptr = subject.Alloc(byteCount: 1000);

            Assert.That(ptr, Is.GreaterThanOrEqualTo(100));
        }

        [Test]
        public void a_second_allocation_returns_different_memory (){ }

        [Test]
        public void can_directly_deallocate_a_pointer (){ }

        [Test]
        public void deallocating_the_most_recently_allocated_block_returns_the_same_pointer (){ }

        [Test]
        public void deallocating_an_old_allocation_does_nothing (){ }

        [Test]
        public void can_add_and_remove_current_referenced_pointers (){ }

        [Test]
        public void allocating_enough_memory_changes_arena (){ }

        [Test]
        public void deallocating_everything_in_an_arena_resets_it (){ }

        [Test]
        public void running_a_scan_when_any_pointers_are_referenced_keeps_the_arena (){ }

        [Test]
        public void running_a_scan_when_no_pointers_are_referenced_resets_the_arena (){ }

        [Test] // todo: do something better once the rest is working
        public void requesting_a_block_larger_than_a_single_area_fails (){ }
    }
}
