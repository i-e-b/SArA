using System;
using NUnit.Framework;

namespace Sara.Tests
{
    [TestFixture]
    public class VectorTests {

        public SampleElement Sample1() { return new SampleElement { a = 1, b = 1.1 }; }
        public SampleElement Sample2() { return new SampleElement { a = 2, b = 2.2 }; }
        public SampleElement Sample3() { return new SampleElement { a = 3, b = 3.3 }; }
        public SampleElement Sample(int i) { return new SampleElement { a = i, b = i / 2.0 }; }

        [Test]
        public void can_store_and_read_array_elements ()
        {
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Vector<SampleElement>(new Allocator(0, Mega.Bytes(1), mem), mem);

            Assert.That(subject.Length(), Is.Zero);

            subject.Push(Sample1());
            subject.Push(Sample2());

            Assert.That(subject.Length(), Is.EqualTo(2));

            var r = subject.Get(0).Value;
            Assert.That(r, Is.EqualTo(Sample1()));
            Assert.That(subject.Get(1).Value, Is.EqualTo(Sample2()));
        }

        [Test]
        public void results_are_stable_over_get ()
        {
            var mem = new MemorySimulator(Mega.Bytes(50));
            var sara = new Vector<int>(new Allocator(0, Mega.Bytes(50), mem), mem);

            long saraSum = 0;
            const int size = 500_000;//40*(3 + Vector<int>.SECOND_LEVEL_SKIPS) * Vector<int>.TARGET_ELEMS_PER_CHUNK;
            Console.WriteLine("Size = " + size);

            for (int i = 0; i < size; i++)
            {
                saraSum += i;
                var res = sara.Push(i);
                if (!res.Success) Assert.Fail("Push failed at "+i);
            }
            for (int i = 0; i < size; i++)
            {
                var res = sara.Get((uint)i);
                if (!res.Success) Assert.Fail("Get failed at "+i);
                if (res.Value != i) Assert.Fail("Wrong value at "+i);

                saraSum -= res.Value;
            }

            Assert.That(saraSum, Is.Zero);
        }
        
        [Test]
        public void results_are_stable_over_pop ()
        {
            var mem = new MemorySimulator(Mega.Bytes(50));
            var sara = new Vector<int>(new Allocator(0, Mega.Bytes(50), mem), mem);

            long saraSum = 0;
            const int size = 500_000;//40*(3 + Vector<int>.SECOND_LEVEL_SKIPS) * Vector<int>.TARGET_ELEMS_PER_CHUNK;
            Console.WriteLine("Size = " + size);

            for (int i = 0; i < size; i++)
            {
                saraSum += i;
                var res = sara.Push(i);
                if (!res.Success) Assert.Fail("Push failed at "+i);
            }
            for (int i = size-1; i >= 0; i--)
            {
                var res = sara.Pop();
                if (!res.Success) Assert.Fail("Pop failed at " + i);
                if (res.Value != i) Assert.Fail("Wrong value at " + i);

                saraSum -= res.Value;
            }

            Assert.That(saraSum, Is.Zero);
        }

        [Test]
        public void can_create_a_vector_larger_than_the_arena_limit()
        {
            var memsize = Mega.Bytes(1);
            var mem = new MemorySimulator(memsize);
            var subject = new Vector<SampleElement>(new Allocator(0, memsize, mem), mem);

            uint full = (uint) (Allocator.ArenaSize / sizeof(long)) * 2;

            var expected = (full * 8) + ((full / 32) * 8);
            Console.WriteLine("Expecting to allocate " + expected);
            if (expected >= memsize) Assert.Fail("memsize is not big enough for the test");

            for (int i = 0; i < full; i++)
            {
                subject.Push(Sample(i));
            }

            var res = subject.Get(full - 1);
            Assert.That(res.Value.a, Is.EqualTo(full - 1));
        }
       
        [Test]
        public void popping_the_last_item_from_a_list_gives_its_value ()
        {
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Vector<SampleElement>(new Allocator(0, Mega.Bytes(1), mem), mem);

            Assert.That(subject.Length(), Is.Zero);

            subject.Push(Sample1());
            subject.Push(Sample2());

            Assert.That(subject.Length(), Is.EqualTo(2));

            Assert.That(subject.Pop().Value, Is.EqualTo(Sample2()));
            Assert.That(subject.Pop().Value, Is.EqualTo(Sample1()));
            Assert.That(subject.Length(), Is.Zero);
        }

        [Test]
        public void removing_elements_frees_chunks ()
        {
            // Setup, and allocate a load of entries
            var memsize = Mega.Bytes(1);
            var mem = new MemorySimulator(memsize);
            var alloc = new Allocator(0, memsize, mem);
            var subject = new Vector<SampleElement>(alloc, mem);

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
            var mem = new MemorySimulator(memsize);
            var alloc = new Allocator(0, memsize, mem);
            var subject = new Vector<SampleElement>(alloc, mem);

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
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Vector<SampleElement>(new Allocator(0, Mega.Bytes(1), mem), mem);

            Assert.That(subject.Length(), Is.Zero);

            subject.Push(Sample1());
            subject.Push(Sample2());

            Assert.That(subject.Length(), Is.EqualTo(2));
            
            subject.Pop();
            subject.Push(Sample3());

            Assert.That(subject.Get(1).Value, Is.EqualTo(Sample3()), "Get 1 is wrong");
            Assert.That(subject.Get(0).Value, Is.EqualTo(Sample1()), "Get 0 is wrong");
        }

        [Test]
        public void add_and_removing_elements_works_with_pre_existing_allocations ()
        {
            var mem = new MemorySimulator(Mega.Bytes(1));
            var alloc = new Allocator(0, Mega.Bytes(1), mem);
            alloc.Alloc(174);
            var subject = new Vector<SampleElement>(alloc, mem);

            Assert.That(subject.Length(), Is.Zero);

            subject.Push(Sample1());
            subject.Push(Sample2());

            Assert.That(subject.Length(), Is.EqualTo(2));
            
            subject.Pop();
            subject.Push(Sample3());

            Assert.That(subject.Get(1).Value, Is.EqualTo(Sample3()), "Get 1 is wrong");
            Assert.That(subject.Get(0).Value, Is.EqualTo(Sample1()), "Get 0 is wrong");
        }

        [Test]
        public void can_overwrite_entries_by_explicit_index ()
        {
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Vector<SampleElement>(new Allocator(0, Mega.Bytes(1), mem), mem);

            Assert.That(subject.Length(), Is.Zero);

            subject.Push(Sample1());
            subject.Push(Sample2());

            Assert.That(subject.Length(), Is.EqualTo(2));
            
            subject.Set(0, Sample3());

            Assert.That(subject.Get(0).Value, Is.EqualTo(Sample3()));
            Assert.That(subject.Get(1).Value, Is.EqualTo(Sample2()));
        }

        [Test]
        public void can_preallocate_space_in_the_vector ()
        {
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Vector<SampleElement>(new Allocator(0, Mega.Bytes(1), mem), mem);

            Assert.That(subject.Length(), Is.Zero);

            subject.Prealloc(20);
            subject.Set(0, Sample1());
            subject.Set(10, Sample3());
            subject.Set(19, Sample1());

            Assert.That(subject.Length(), Is.EqualTo(20));

            Assert.That(subject.Get(0).Value, Is.EqualTo(Sample1()));
            Assert.That(subject.Get(19).Value, Is.EqualTo(Sample1()));

            Assert.That(subject.Get(10).Value, Is.EqualTo(Sample3()));
        }

        [Test]
        public void elements_per_chunk_is_reasonable ()
        {
            Console.WriteLine(Allocator.ArenaSize);


            var mem_a = new MemorySimulator(Kilo.Bytes(80));
            var mem_b = new MemorySimulator(Kilo.Bytes(80));
            var mem_c = new MemorySimulator(Kilo.Bytes(80));

            var structVector    = new Vector<SampleElement>(new Allocator(0, Kilo.Bytes(80), mem_a), mem_a);
            var bigStructVector = new Vector<HugeStruct>   (new Allocator(0, Kilo.Bytes(80), mem_b), mem_b);
            var byteVector      = new Vector<byte>         (new Allocator(0, Kilo.Bytes(80), mem_c), mem_c);

            Assert.That(byteVector.ElemsPerChunk, Is.LessThan(70));   // Not too big on small elements
            Assert.That(structVector.ElemsPerChunk, Is.GreaterThan(30));
            Assert.That(bigStructVector.ElemsPerChunk, Is.LessThan(32)); // scales down to fit arenas
        }

        [Test]
        public void can_swap_two_elements_by_index ()
        {
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Vector<SampleElement>(new Allocator(0, Mega.Bytes(1), mem), mem);
            
            uint length = (uint) (subject.ElemsPerChunk * 2);

            subject.Prealloc(length);
            var end = length - 1;

            subject.Set(1, Sample3());
            subject.Set(end, Sample2());

            Assert.That(subject.Length(), Is.EqualTo(length), "Wrong length");
            Assert.That(subject.Get(1).Value, Is.EqualTo(Sample3()), "Wrong initial element at start");
            Assert.That(subject.Get(end).Value, Is.EqualTo(Sample2()), "Wrong initial element at end");

            // the main event:
            subject.Swap(1, end);


            Assert.That(subject.Get(1).Value, Is.EqualTo(Sample2()), "Wrong final element at start");
            Assert.That(subject.Get(end).Value, Is.EqualTo(Sample3()), "Wrong final element at end");
        }

        [Test] // stress test
        public void can_handle_a_large_number_of_pushes_in_reasonable_time ()
        {
            var mem = new MemorySimulator(Mega.Bytes(10));
            var alloc = new Allocator(0, Mega.Bytes(10), mem);
            var subject = new Vector<uint>(alloc, mem);

            Assert.That(subject.Length(), Is.Zero);

            // Note: Internally, the Vector should decide to use extra skip levels on larger arrays

            uint count = 500_000;
            for (uint i = 0; i < count; i++)
            {
                subject.Push(i);
                subject.Get(i);
            }

            alloc.GetState(out var bytesAllocated, out _, out _, out _, out _, out _);
            Console.WriteLine("Allocated " + bytesAllocated + " bytes");

            Assert.That(subject.Length(), Is.EqualTo(count));
        }


        // ReSharper disable UnusedMember.Global
        public struct SampleElement {
            public int a;
            public double b;

            public override string ToString()
            {
                return "a="+a+"; b="+b;
            }
        }
        public struct HugeStruct {
            public ReallyBigStruct a;
            public ReallyBigStruct b;
            public ReallyBigStruct c;
        }
        public struct ReallyBigStruct {
            public BigStruct a;
            public BigStruct b;
            public BigStruct c;
            public BigStruct d;
            public BigStruct e;
            public BigStruct f;
            public BigStruct a2;
            public BigStruct b2;
            public BigStruct c2;
            public BigStruct d2;
            public BigStruct e2;
            public BigStruct f2;
        }
        public struct BigStruct {
            public double a;
            public double b;
            public double c;
            public double d;
            public double e;
            public double f;
            public double g;
            public double h;
            public double i;
            public double j;
            public double k;
            public double l;
            public double m;
        }
        // ReSharper restore UnusedMember.Global
    }
}