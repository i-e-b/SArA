using System;
using NUnit.Framework;

namespace Sara.Tests
{
    [TestFixture]
    public class VariableContainerVectorTests {
        public struct SampleElement {
            public int a;
            public double b;
        }

        public SampleElement Sample1() { return new SampleElement { a = 1, b = 1.1 }; }
        public SampleElement Sample2() { return new SampleElement { a = 2, b = 2.2 }; }
        public SampleElement Sample3() { return new SampleElement { a = 3, b = 3.3 }; }
        public SampleElement Sample(int i) { return new SampleElement { a = i, b = i / 2.0 }; }

        [Test]
        public void can_store_and_read_array_elements ()
        {
            var subject = new Vector<SampleElement>(new Allocator(0, Mega.Bytes(1)), new MemorySimulator(Mega.Bytes(1)));

            Assert.That(subject.Length(), Is.Zero);

            subject.Push(Sample1());
            subject.Push(Sample2());

            Assert.That(subject.Length(), Is.EqualTo(2));

            var r = subject.Get(0);
            Assert.That(r, Is.EqualTo(Sample1()));
            Assert.That(subject.Get(1), Is.EqualTo(Sample2()));
        }

        [Test]
        public void can_create_a_vector_larger_than_the_arena_limit()
        {
            var memsize = Mega.Bytes(1);
            var subject = new Vector<SampleElement>(new Allocator(0, memsize), new MemorySimulator(memsize));

            uint full = (uint) (Allocator.ArenaSize / sizeof(long)) * 2;

            var expected = (full * 8) + ((full / 32) * 8);
            Console.WriteLine("Expecting to allocate " + expected);
            if (expected >= memsize) Assert.Fail("memsize is not big enough for the test");

            for (int i = 0; i < full; i++)
            {
                subject.Push(Sample(i));
            }

            var res = subject.Get(full - 1);
            Assert.That(res.a, Is.EqualTo(full - 1));
        }

        [Test]
        public void popping_the_last_item_from_a_list_gives_its_value ()
        {
            var subject = new Vector<SampleElement>(new Allocator(0, Mega.Bytes(1)), new MemorySimulator(Mega.Bytes(1)));

            Assert.That(subject.Length(), Is.Zero);

            subject.Push(Sample1());
            subject.Push(Sample2());

            Assert.That(subject.Length(), Is.EqualTo(2));

            Assert.That(subject.Pop(), Is.EqualTo(Sample2()));
            Assert.That(subject.Pop(), Is.EqualTo(Sample1()));
            Assert.That(subject.Length(), Is.Zero);
        }

        [Test]
        public void removing_elements_frees_chunks ()
        {
            // Setup, and allocate a load of entries
            var memsize = Mega.Bytes(1);
            var alloc = new Allocator(0, memsize);
            var subject = new Vector<SampleElement>(alloc, new MemorySimulator(memsize));

            int full = (int) (Allocator.ArenaSize / sizeof(long)) * 2;

            var expected = (full * 8) + ((full / 32) * 8);
            Console.WriteLine("Expecting to allocate " + expected);
            if (expected >= memsize) Assert.Fail("memsize is not big enough for the test");

            for (int i = 0; i < full; i++)
            {
                subject.Push(Sample(i));
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
            // Setup, and allocate a load of entries
            var memsize = Mega.Bytes(1);
            var alloc = new Allocator(0, memsize);
            var subject = new Vector<SampleElement>(alloc, new MemorySimulator(memsize));

            int full = (int) (Allocator.ArenaSize / sizeof(long)) * 2;

            var expected = (full * 8) + ((full / 32) * 8);
            Console.WriteLine("Expecting to allocate " + expected);
            if (expected >= memsize) Assert.Fail("memsize is not big enough for the test");

            for (int i = 0; i < full; i++) { subject.Push(Sample(i)); }

            // Now free the whole list at once (including base ptr)
            subject.Deallocate();
            
            // check we've emptied memory
            alloc.GetState(out var allocatedBytes, out _, out var post_occupied, out _, out var post_refCount, out _);
            Assert.That(allocatedBytes, Is.Zero, "Expected no bytes allocated");
            Assert.That(post_occupied, Is.Zero, "Expected no arenas occupied");
            Assert.That(post_refCount, Is.Zero, "Expected no references");
        }

        [Test]
        public void can_add_elements_after_removing_them ()
        {
            var subject = new Vector<SampleElement>(new Allocator(0, Mega.Bytes(1)), new MemorySimulator(Mega.Bytes(1)));

            Assert.That(subject.Length(), Is.Zero);

            subject.Push(Sample1());
            subject.Push(Sample2());

            Assert.That(subject.Length(), Is.EqualTo(2));
            
            subject.Pop();
            subject.Push(Sample3());

            Assert.That(subject.Get(0), Is.EqualTo(Sample1()));
            Assert.That(subject.Get(1), Is.EqualTo(Sample3()));
        }

        [Test]
        public void can_overwrite_entries_by_explicit_index ()
        {
            var subject = new Vector<SampleElement>(new Allocator(0, Mega.Bytes(1)), new MemorySimulator(Mega.Bytes(1)));

            Assert.That(subject.Length(), Is.Zero);

            subject.Push(Sample1());
            subject.Push(Sample2());

            Assert.That(subject.Length(), Is.EqualTo(2));
            
            subject.Set(0, Sample3());

            Assert.That(subject.Get(0), Is.EqualTo(Sample3()));
            Assert.That(subject.Get(1), Is.EqualTo(Sample2()));
        }

        [Test]
        public void can_preallocate_space_in_the_vector ()
        {
            var subject = new Vector<SampleElement>(new Allocator(0, Mega.Bytes(1)), new MemorySimulator(Mega.Bytes(1)));

            Assert.That(subject.Length(), Is.Zero);

            subject.Prealloc(20, Sample1());
            subject.Set(10, Sample3());

            Assert.That(subject.Length(), Is.EqualTo(20));

            Assert.That(subject.Get(0), Is.EqualTo(Sample1()));
            Assert.That(subject.Get(19), Is.EqualTo(Sample1()));

            Assert.That(subject.Get(10), Is.EqualTo(Sample3()));
        }
    }
}