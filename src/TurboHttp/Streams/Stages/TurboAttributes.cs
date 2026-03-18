using System;
using Akka.Streams;

namespace TurboHttp.Streams.Stages;

public static class TurboAttributes
{
    public sealed class MemoryBuffer : Attributes.IMandatoryAttribute, IEquatable<MemoryBuffer>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly int Initial;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly int Max;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initial">TBD</param>
        /// <param name="max">TBD</param>
        public MemoryBuffer(int initial, int max)
        {
            Initial = initial;
            Max = max;
        }
        public bool Equals(MemoryBuffer? other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return Initial == other.Initial && Max == other.Max;
        }

        public override bool Equals(object? obj) => obj is MemoryBuffer buffer && Equals(buffer);
        public override int GetHashCode()
        {
            unchecked
            {
                return (Initial * 397) ^ Max;
            }
        }
        public override string ToString() => $"MemoryBuffer(initial={Initial}, max={Max})";
    }
}