using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;

namespace Sara.Tests
{
    /// <summary>
    /// Note that we don't expect to match performance, as our implementations
    /// are much simpler and in fully managed code
    /// </summary>
    [TestFixture]
    public class PerformanceComparison
    {
        [Test]
        public void TaggedHashMap__vs__Dictionary()
        {
            var rnd = new Random();
            var sara_time = new Stopwatch();
            var dotnet_time = new Stopwatch();

            var sara = new TaggedHashMap(0, new Allocator(0, Mega.Bytes(50)), new MemorySimulator(Mega.Bytes(50)));
            var dotnet = new Dictionary<ulong, ulong>(0);

            sara_time.Start();
            for (ulong i = 0; i < 2500; i++)
            {
                var insKey = (ulong)rnd.Next(1, 1000000);
                var remKey = (ulong)rnd.Next(1, 1000000);

                if (!sara.Put(insKey, i, true)) Assert.Fail("Put rejected the change at "+i);
                sara.Remove(remKey);
            }
            sara_time.Stop();

            
            dotnet_time.Start();
            for (ulong i = 0; i < 2500; i++)
            {
                var insKey = (ulong)rnd.Next(1, 1000000);
                var remKey = (ulong)rnd.Next(1, 1000000);

                if (dotnet.ContainsKey(insKey)) dotnet[insKey] = i;
                else dotnet.Add(insKey, i);

                dotnet.Remove(remKey);
            }
            dotnet_time.Stop();

            Assert.Pass("SArA: " + sara_time.Elapsed + "; dotnet: " + dotnet_time.Elapsed);
        }

        [Test]
        public void Vector__vs__List()
        {
            var sara_time = new Stopwatch();
            var dotnet_time = new Stopwatch();

            var sara = new Vector<int>(new Allocator(0, Mega.Bytes(50)), new MemorySimulator(Mega.Bytes(50)));
            var dotnet = new List<int>();

            sara_time.Start();
            int saraSum = 0;
            for (int i = 0; i < 2500; i++)
            {
                saraSum += i;
                sara.Push(i);
            }
            for (int i = 0; i < 2500; i++)
            {
                saraSum -= sara.Get((uint)i).Value;
            }
            for (int i = 0; i < 2500; i++)
            {
                sara.Pop();
            }
            sara_time.Stop();

            
            dotnet_time.Start();
            int dotnetSum = 0;
            for (int i = 0; i < 2500; i++)
            {
                dotnetSum += i;
                dotnet.Add(i);
            }
            for (int i = 0; i < 2500; i++)
            {
                dotnetSum -= dotnet[i];
            }
            for (int i = 0; i < 2500; i++)
            {
                dotnet.RemoveAt(dotnet.Count - 1);
            }
            dotnet_time.Stop();

            Assert.Pass("SArA: " + sara_time.Elapsed + " result = " + saraSum + "; dotnet: " + dotnet_time.Elapsed + " result = " + dotnetSum);
        }

        [Test]
        public void Vector__vs__Stack()
        {
            var sara_time = new Stopwatch();
            var dotnet_time = new Stopwatch();

            var sara = new Vector<int>(new Allocator(0, Mega.Bytes(50)), new MemorySimulator(Mega.Bytes(50)));
            var dotnet = new Stack<int>();

            sara_time.Start();
            int saraSum = 0;
            for (int i = 0; i < 2500; i++)
            {
                saraSum += i;
                sara.Push(i);
            }
            for (int i = 0; i < 2500; i++)
            {
                saraSum -= sara.Pop().Value;
            }
            sara_time.Stop();

            
            dotnet_time.Start();
            int dotnetSum = 0;
            for (int i = 0; i < 2500; i++)
            {
                dotnetSum += i;
                dotnet.Push(i);
            }
            for (int i = 0; i < 2500; i++)
            {
                dotnetSum -= dotnet.Pop();
            }
            dotnet_time.Stop();

            Assert.That(dotnetSum, Is.Zero, "dotnet result is unexpected");
            Assert.That(saraSum, Is.Zero, "SArA result is unexpected");
            Assert.Pass("SArA: " + sara_time.Elapsed + " result = " + saraSum + "; dotnet: " + dotnet_time.Elapsed + " result = " + dotnetSum);
        }

        [Test]
        public void Vector__vs__LinkedList()
        {
            var sara_time = new Stopwatch();
            var dotnet_time = new Stopwatch();

            var sara = new Vector<int>(new Allocator(0, Mega.Bytes(50)), new MemorySimulator(Mega.Bytes(50)));
            var dotnet = new LinkedList<int>();

            sara_time.Start();
            for (int i = 0; i < 2500; i++)
            {
                sara.Push(i);
            }
            for (int i = 0; i < 2500; i++)
            {
                sara.Pop();
            }
            sara_time.Stop();

            
            dotnet_time.Start();
            for (int i = 0; i < 2500; i++)
            {
                dotnet.AddLast(i);
            }
            for (int i = 0; i < 2500; i++)
            {
                dotnet.RemoveLast();
            }
            dotnet_time.Stop();

            Assert.Pass("SArA: " + sara_time.Elapsed + " ; dotnet: " + dotnet_time.Elapsed );
        }
    }
}