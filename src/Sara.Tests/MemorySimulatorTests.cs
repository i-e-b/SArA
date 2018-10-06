using NUnit.Framework;

namespace Sara.Tests
{
    [TestFixture]
    public class MemorySimulatorTests {
        [Test]
        public void reading_and_writing_types ()
        {
            var subject = new MemorySimulator(128);

            subject.Write(location: 0, value: (byte)0x88);
            subject.Write(location: 1, value: (byte)0x77);
            subject.Write(location: 2, value: (byte)0x00);
            subject.Write(location: 3, value: (byte)0x00);
            subject.Write(location: 4, value: (byte)0x00);

            var a = subject.Read<short>(1);
            Assert.That(a, Is.EqualTo(0x0077)); // due to endian style

            var b = subject.Read<uint>(0);
            Assert.That(b, Is.EqualTo(0x00007788));

            var c = subject.Read<uint>(1);
            Assert.That(c, Is.EqualTo(0x00000077));
        }
    }
}