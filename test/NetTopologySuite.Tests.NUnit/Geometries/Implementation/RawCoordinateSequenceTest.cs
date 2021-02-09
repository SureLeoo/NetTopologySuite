using System;

using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Implementation;

namespace NetTopologySuite.Tests.NUnit.Geometries.Implementation
{
    public sealed class RawCoordinateSequenceTest : CoordinateSequenceTestBase
    {
        private Ordinates[] _ordinateGroups = Array.Empty<Ordinates>();

        protected override CoordinateSequenceFactory CsFactory => new RawCoordinateSequenceFactory(_ordinateGroups);
    }
}
