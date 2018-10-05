using System;
using NUnit.Framework;

namespace Sara.Tests
{
    [TestFixture]
    public class UnsafeArrayTests {
        [Test]
        public unsafe void casting_byte_arrays_to_doubles (){
            // TODO: make a helper class to use with the allocator
            var allocd = new byte[32];

            fixed (byte* basePtr = &allocd[0]) {
                //var tgtPtr = basePtr + 8;

                var dPtr = (double*)basePtr;

                dPtr++;

                (*dPtr) = 123.456;
            }

            // Now read it back
            for (int i = 0; i < allocd.Length; i++)
            {
                Console.Write(allocd[i].ToString("X2"));
                Console.Write(" ");
            }
        }
    }
}