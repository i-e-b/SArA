using System;

namespace Sara.Tests
{
    public struct SampleElement {
        public int a;
        public double b;

        public override string ToString()
        {
            return "a="+a+"; b="+b;
        }

        public override bool Equals(object obj)
        {
            var other = obj as SampleElement?;
            if (other == null) return false;
            return (a == other.Value.a) && (Math.Abs(b - other.Value.b) < 0.0001);
        }
    }
}