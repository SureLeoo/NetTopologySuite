using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NetTopologySuite.Geometries.Implementation
{
    /// <summary>
    /// An implementation of <see cref="CoordinateSequence"/> that packs its contents in a way that
    /// can be customized by the creator.
    /// </summary>
    public sealed class RawCoordinateSequence : CoordinateSequence
    {
        private readonly (Memory<double> Array, int DimensionCount)[] _rawData;

        private readonly (int RawDataIndex, int DimensionIndex)[] _dimensionMap;

        /// <summary>
        /// Initializes a new instance of the <see cref="RawCoordinateSequence"/> class.
        /// </summary>
        /// <param name="rawData">
        /// Contains the raw data for this array.
        /// </param>
        /// <param name="dimensionMap">
        /// Contains a pair of indexes to tell us, for each dimension, where to find its data in
        /// <paramref name="rawData"/>.
        /// </param>
        /// <param name="measures">
        /// The value for <see cref="CoordinateSequence.Measures"/>.
        /// </param>
        public RawCoordinateSequence(Memory<double>[] rawData, (int RawDataIndex, int DimensionIndex)[] dimensionMap, int measures)
            : base(GetCountIfValid(rawData, dimensionMap), GetDimensionIfValid(rawData, dimensionMap), measures)
        {
            _rawData = new (Memory<double> Array, int DimensionCount)[rawData.Length];
            for (int i = 0; i < rawData.Length; i++)
            {
                _rawData[i].Array = rawData[i];
                _rawData[i].DimensionCount = rawData[i].Length / Count;
            }

            _dimensionMap = dimensionMap;
        }

        private RawCoordinateSequence(int count, int dimension, int measures, (Memory<double> Array, int DimensionCount)[] rawData, (int RawDataIndex, int DimensionIndex)[] dimensionMap)
            : base(count, dimension, measures)
        {
            _rawData = rawData;
            _dimensionMap = dimensionMap;
        }

        /// <summary>
        /// Gets the underlying <see cref="Memory{T}"/> for the ordinates at the given index, along
        /// with a "stride" value that represents how many slots there are between elements.
        /// </summary>
        /// <param name="ordinateIndex">
        /// The index of the ordinate whose values to get.
        /// </param>
        /// <returns>
        /// The underlying <see cref="Memory{T}"/> and stride.
        /// </returns>
        public (Memory<double> Array, int Stride) GetRawCoordinatesAndStride(int ordinateIndex)
        {
            if ((uint)ordinateIndex >= (uint)_dimensionMap.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(ordinateIndex), ordinateIndex, "Must be less than Dimension.");
            }

            (int sourceIndex, int offset) = _dimensionMap[ordinateIndex];
            (var array, int stride) = _rawData[sourceIndex];
            return (array.Slice(offset), stride);
        }

        /// <inheritdoc />
        public override double GetOrdinate(int index, int ordinateIndex)
        {
            return ValueRef(index, ordinateIndex);
        }

        /// <inheritdoc />
        public override void SetOrdinate(int index, int ordinateIndex, double value)
        {
            ValueRef(index, ordinateIndex) = value;
        }

        /// <inheritdoc />
        public override CoordinateSequence Copy()
        {
            var newRawData = _rawData.AsSpan().ToArray();

            for (int i = 0; i < newRawData.Length; i++)
            {
                newRawData[i].Array = newRawData[i].Array.ToArray();
            }

            var newDimensionMap = _dimensionMap.AsSpan().ToArray();
            return new RawCoordinateSequence(Count, Dimension, Measures, newRawData, newDimensionMap);
        }

        /// <inheritdoc />
        public override CoordinateSequence Reversed()
        {
            var result = (RawCoordinateSequence)Copy();

            // reverse all the individual arrays.
            foreach (var (array, _) in result._rawData)
            {
                array.Span.Reverse();
            }

            // that reversed the order of the ordinate values within each coordinate, so update the
            // map to mark them in reversed order.
            foreach (ref var entry in result._dimensionMap.AsSpan())
            {
                entry.DimensionIndex = result._rawData[entry.RawDataIndex].DimensionCount - entry.DimensionIndex - 1;
            }

            return result;
        }

        /// <inheritdoc />
        public override Envelope ExpandEnvelope(Envelope env)
        {
            if (env is null)
            {
                throw new ArgumentNullException(nameof(env));
            }

            (var xsMem, int strideX) = GetRawCoordinatesAndStride(0);
            (var ysMem, int strideY) = GetRawCoordinatesAndStride(1);
            var xs = xsMem.Span;
            var ys = ysMem.Span;
            for (int x = 0, y = 0; x < xs.Length; x += strideX, y += strideY)
            {
                env.ExpandToInclude(xs[x], ys[y]);
            }

            return env;
        }

        public override Coordinate[] ToCoordinateArray()
        {
            var raw = new (ReadOnlyMemory<double> Memory, int Stride)[Dimension];
            var rawArrays = new (double[] Array, int Offset, int Stride)[Dimension];
            for (int i = 0; i < raw.Length; i++)
            {
                raw[i] = GetRawCoordinatesAndStride(i);

                if (rawArrays is null)
                {
                    continue;
                }

                if (MemoryMarshal.TryGetArray(raw[i].Memory, out var arraySegment))
                {
                    rawArrays = null;
                    continue;
                }

                rawArrays[i].Array = arraySegment.Array;
                rawArrays[i].Offset = arraySegment.Offset;
                rawArrays[i].Stride = raw[i].Stride;
            }

            var result = new Coordinate[Count];
            if (rawArrays != null)
            {
                raw = null;
                for (int i = 0; i < result.Length; i++)
                {
                    var coord = result[i] = CreateCoordinate();
                    for (int j = 0; j < rawArrays.Length; j++)
                    {
                        ref var nxt = ref rawArrays[j];
                        coord[j] = nxt.Array[nxt.Offset];
                        nxt.Offset += nxt.Stride;
                    }
                }
            }
            else
            {
                // xs and ys can be special
                var xs = raw[0].Memory.Span;
                int strideX = raw[0].Stride;
                var ys = raw[1].Memory.Span;
                int strideY = raw[1].Stride;

                for (int i = 0; i < result.Length; i++)
                {
                    var coord = result[i] = CreateCoordinate();
                    coord.X = xs[0];
                    xs = xs.Slice(strideX);
                    coord.Y = ys[0];
                    ys = ys.Slice(strideY);

                    for (int j = 2; j < raw.Length; j++)
                    {
                        ref var nxt = ref raw[j];
                        coord[j] = nxt.Memory.Span[0];
                        nxt.Memory = nxt.Memory.Slice(nxt.Stride);
                    }
                }
            }

            return result;
        }

        private static int GetCountIfValid(Memory<double>[] rawData, (int RawDataIndex, int DimensionIndex)[] dimensionMap)
        {
            if (rawData is null)
            {
                throw new ArgumentNullException(nameof(rawData));
            }

            if (dimensionMap is null)
            {
                throw new ArgumentNullException(nameof(dimensionMap));
            }

            int dimensionCount = dimensionMap.Length;
            if (dimensionCount == 0)
            {
                // base class requires at least 2 spatial dimensions, so it'll throw for us.
                return 0;
            }

            int? inferredCount = null;
            foreach (var array in rawData)
            {
                int currentInferredCount = Math.DivRem(array.Length, dimensionCount, out int remainder);
                if (remainder != 0)
                {
                    throw new ArgumentException("Arrays must all have exactly enough room for all their elements.", nameof(rawData));
                }

                if (inferredCount is int prevInferredCount)
                {
                    if (prevInferredCount != currentInferredCount)
                    {
                        throw new ArgumentException("All arrays must have the same count.", nameof(rawData));
                    }
                }
                else
                {
                    inferredCount = currentInferredCount;
                }
            }

            return inferredCount.GetValueOrDefault();
        }

        private static int GetDimensionIfValid(Memory<double>[] rawData, (int RawDataIndex, int DimensionIndex)[] dimensionMap)
        {
            int dimensionCount = dimensionMap.Length;

            if (rawData.Length == 0)
            {
                return dimensionMap.Length;
            }

            Span<int> scratchIntBuffer = stackalloc int[0];
            if (rawData.Length < 10)
            {
                scratchIntBuffer = stackalloc int[rawData.Length * 2];
                scratchIntBuffer.Clear();
            }
            else
            {
                scratchIntBuffer = new int[rawData.Length * 2];
            }

            var dimensionsBefore = scratchIntBuffer.Slice(0, rawData.Length);
            var dimensionsIn = scratchIntBuffer.Slice(rawData.Length);

            int dimensionsSoFar = 0;
            for (int i = 0; i < rawData.Length; i++)
            {
                dimensionsBefore[i] = dimensionsSoFar;
                dimensionsSoFar += dimensionsIn[i] = rawData[i].Length / dimensionCount;
            }

            if (dimensionsSoFar != dimensionCount)
            {
                throw new ArgumentException("Inferred dimension count from raw data does not match the number of entries in dimension map.");
            }

            Span<bool> slotIsUsed = stackalloc bool[0];
            if (dimensionCount < 20)
            {
                slotIsUsed = stackalloc bool[dimensionCount];
                slotIsUsed.Clear();
            }
            else
            {
                slotIsUsed = new bool[dimensionCount];
            }

            foreach ((int rawDataIndex, int dimensionIndex) in dimensionMap)
            {
                if ((uint)rawDataIndex >= (uint)dimensionsIn.Length)
                {
                    throw new ArgumentException("Raw data index in dimension map must be less than the length of raw data.");
                }

                if ((uint)dimensionIndex >= (uint)dimensionsIn[rawDataIndex])
                {
                    throw new ArgumentException("Dimension index in dimension map must be less than the number of dimensions in the corresponding raw data slot.");
                }

                int slotIndex = dimensionsBefore[rawDataIndex] + dimensionIndex;
                if (slotIsUsed[slotIndex])
                {
                    throw new ArgumentException("Dimension map contains duplicate values.", nameof(dimensionMap));
                }

                slotIsUsed[slotIndex] = true;
            }

            foreach (bool flag in slotIsUsed)
            {
                if (!flag)
                {
                    throw new ArgumentException("Dimension map does not cover all slots in raw data.");
                }
            }

            return dimensionCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref double ValueRef(int index, int ordinateIndex)
        {
            (int sourceIndex, int offset) = _dimensionMap[ordinateIndex];
            (var array, int stride) = _rawData[sourceIndex];
            return ref array.Span[(index * stride) + offset];
        }
    }
}
