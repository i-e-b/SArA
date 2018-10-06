﻿using NUnit.Framework;

namespace Sara.Tests
{
    [TestFixture]
    public class VariableVectorTests {
        [Test]
        public void can_store_and_read_array_elements ()
        {
            var subject = new VariableVector(new Allocator(0, Mega.Bytes(1)), new MemorySimulator(Mega.Bytes(1)));

            Assert.That(subject.Length(), Is.Zero);

            subject.Add(123);
            subject.Add(234);

            Assert.That(subject.Length(), Is.EqualTo(2));

            var r = subject.Get(0);
            Assert.That(r, Is.EqualTo(123));
        }

        
        [Test]
        public void can_create_a_vector_larger_than_the_arena_limit()
        {
            var subject = new VariableVector(new Allocator(0, Mega.Bytes(10)), new MemorySimulator(Mega.Bytes(10)));

            int full = (int) (Allocator.ArenaSize / sizeof(long));

            for (int i = 0; i < full; i++)
            {
                subject.Add((ulong) i);
            }

            var res = subject.Get(full - 1);
            Assert.That(res, Is.EqualTo(full - 1));
        }
    }
}