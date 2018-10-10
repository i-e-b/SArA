using System;
using NUnit.Framework;

namespace Sara.Tests
{
    [TestFixture]
    public class VariableVectorTests {
        [Test]
        public void can_store_and_read_array_elements ()
        {
            var subject = new VariableVector(new Allocator(0, Mega.Bytes(1)), new MemorySimulator(Mega.Bytes(1)));

            Assert.That(subject.Length(), Is.Zero);

            subject.Push(123);
            subject.Push(234);

            Assert.That(subject.Length(), Is.EqualTo(2));

            var r = subject.Get(0);
            Assert.That(r, Is.EqualTo(123));
            Assert.That(subject.Get(1), Is.EqualTo(234));
        }

        [Test]
        public void can_create_a_vector_larger_than_the_arena_limit()
        {
            var memsize = Mega.Bytes(1);
            var subject = new VariableVector(new Allocator(0, memsize), new MemorySimulator(memsize));

            int full = (int) (Allocator.ArenaSize / sizeof(long)) * 2;

            var expected = (full * 8) + ((full / 32) * 8);
            Console.WriteLine("Expecting to allocate " + expected);
            if (expected >= memsize) Assert.Fail("memsize is not big enough for the test");

            for (int i = 0; i < full; i++)
            {
                subject.Push((ulong) i);
            }

            var res = subject.Get(full - 1);
            Assert.That(res, Is.EqualTo(full - 1));
        }

        [Test]
        public void popping_the_last_item_from_a_list_gives_its_value ()
        {
            var subject = new VariableVector(new Allocator(0, Mega.Bytes(1)), new MemorySimulator(Mega.Bytes(1)));

            Assert.That(subject.Length(), Is.Zero);

            subject.Push(123);
            subject.Push(234);

            Assert.That(subject.Length(), Is.EqualTo(2));

            Assert.That(subject.Pop(), Is.EqualTo(234));
            Assert.That(subject.Pop(), Is.EqualTo(123));
            Assert.That(subject.Length(), Is.Zero);
        }

        [Test]
        public void removing_elements_frees_chunks ()
        {
            // Setup, and allocate a load of entries
            var memsize = Mega.Bytes(1);
            var alloc = new Allocator(0, memsize);
            var subject = new VariableVector(alloc, new MemorySimulator(memsize));

            int full = (int) (Allocator.ArenaSize / sizeof(long)) * 2;

            var expected = (full * 8) + ((full / 32) * 8);
            Console.WriteLine("Expecting to allocate " + expected);
            if (expected >= memsize) Assert.Fail("memsize is not big enough for the test");

            for (int i = 0; i < full; i++)
            {
                subject.Push((ulong) i);
            }

            var pre_length = subject.Length();
            alloc.GetState(out _, out _, out var pre_occupied, out var pre_empty, out var pre_refCount, out _);

            // now remove some of the entries
            for (int i = full / 2; i < full; i++)
            {
                subject.Pop();
            }
            
            var post_length = subject.Length();
            alloc.GetState(out _, out _, out var post_occupied, out var post_empty, out var post_refCount, out _);
            
            Assert.That(post_length, Is.LessThan(pre_length));
            Assert.That(post_occupied, Is.LessThan(pre_occupied));
            Assert.That(post_refCount, Is.LessThan(pre_refCount));

            Assert.That(post_empty, Is.GreaterThan(pre_empty));
        }

        [Test]
        public void freeing_the_list_frees_all_chunks ()
        {
            Assert.Fail("not written");
        }

        [Test]
        public void can_add_elements_after_removing_them ()
        {
            var subject = new VariableVector(new Allocator(0, Mega.Bytes(1)), new MemorySimulator(Mega.Bytes(1)));

            Assert.That(subject.Length(), Is.Zero);

            subject.Push(123);
            subject.Push(234);

            Assert.That(subject.Length(), Is.EqualTo(2));
            
            subject.Pop();
            subject.Push(345);

            Assert.That(subject.Get(0), Is.EqualTo(123));
            Assert.That(subject.Get(1), Is.EqualTo(345));
        }
    }
}