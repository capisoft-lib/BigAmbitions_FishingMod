using System;
using System.Collections.Generic;

namespace FishingMod.StaticWater
{
    /// <summary>Horizontal open-water candidates, not depth, collision or navigation proof.</summary>
    internal sealed class StaticWaterAtlas
    {
        public struct Point
        {
            public readonly double X, Z;
            public Point(double x, double z) { X = x; Z = z; }
        }

        public sealed class Zone
        {
            public readonly string Id;
            public readonly double Height;
            private readonly Point[][] rings;
            private readonly double minX, minZ, maxX, maxZ;

            /// <param name="rings">Outer ring first, then excluded holes; no repeated last vertex needed.</param>
            public Zone(string id, double height, Point[][] rings)
            {
                if (String.IsNullOrEmpty(id) || !Finite(height) || rings == null || rings.Length == 0)
                    throw new ArgumentException("Invalid polygon metadata");
                Id = id; Height = height;
                this.rings = new Point[rings.Length][];
                minX = minZ = Double.PositiveInfinity;
                maxX = maxZ = Double.NegativeInfinity;
                for (int r = 0; r < rings.Length; r++)
                {
                    if (rings[r] == null || rings[r].Length < 3) throw new ArgumentException("Invalid ring");
                    this.rings[r] = (Point[])rings[r].Clone();
                    foreach (Point p in rings[r])
                    {
                        if (!Finite(p.X) || !Finite(p.Z)) throw new ArgumentException("Non-finite coordinate");
                        if (r == 0)
                        {
                            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                            minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
                        }
                    }
                }
            }

            internal double MinX { get { return minX; } }
            internal double MinZ { get { return minZ; } }
            internal double MaxX { get { return maxX; } }
            internal double MaxZ { get { return maxZ; } }

            internal bool Contains(double x, double z)
            {
                if (x < minX || x > maxX || z < minZ || z > maxZ || !InRing(rings[0], x, z)) return false;
                // Hole boundaries are excluded; outer boundaries included.
                for (int i = 1; i < rings.Length; i++) if (InRing(rings[i], x, z)) return false;
                return true;
            }

            internal double DistanceSquared(double x, double z, double best)
            {
                double bx = Math.Max(Math.Max(minX-x, 0), x-maxX);
                double bz = Math.Max(Math.Max(minZ-z, 0), z-maxZ);
                if (bx*bx+bz*bz >= best) return best;
                if (Contains(x,z)) return 0;
                // Outside the polygon, including inside an excluded island/hole:
                // measure to segments, not vertices or the bounding rectangle.
                foreach (Point[] ring in rings)
                    for (int i=0,j=ring.Length-1;i<ring.Length;j=i++)
                    {
                        Point a=ring[j], b=ring[i];
                        double dx=b.X-a.X, dz=b.Z-a.Z, length=dx*dx+dz*dz;
                        double t=length>0 ? Math.Max(0,Math.Min(1,((x-a.X)*dx+(z-a.Z)*dz)/length)) : 0;
                        double px=x-(a.X+t*dx), pz=z-(a.Z+t*dz);
                        best=Math.Min(best,px*px+pz*pz);
                    }
                return best;
            }

            private static bool InRing(Point[] ring, double x, double z)
            {
                bool inside = false;
                for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                {
                    Point a = ring[j], b = ring[i];
                    double dx = b.X - a.X, dz = b.Z - a.Z;
                    double cross = (x-a.X)*dz - (z-a.Z)*dx;
                    if (Math.Abs(cross) <= 1e-9 * Math.Max(1, Math.Abs(dx)+Math.Abs(dz)) &&
                        x >= Math.Min(a.X,b.X) && x <= Math.Max(a.X,b.X) &&
                        z >= Math.Min(a.Z,b.Z) && z <= Math.Max(a.Z,b.Z)) return true;
                    if ((a.Z > z) != (b.Z > z) && x < a.X + (z-a.Z)*dx/dz) inside = !inside;
                }
                return inside;
            }
        }

        private readonly double cellSize;
        private readonly Zone[] zones;
        private readonly Dictionary<long, List<Zone>> cells = new Dictionary<long, List<Zone>>();
        public StaticWaterAtlas(double cellSize, Zone[] zones)
        {
            if (!Finite(cellSize) || cellSize <= 0 || zones == null) throw new ArgumentException();
            this.cellSize = cellSize;
            this.zones = (Zone[])zones.Clone();
            foreach (Zone zone in zones)
            {
                if (zone == null) throw new ArgumentException("Null zone");
                int x0 = Cell(zone.MinX), x1 = Cell(zone.MaxX), z0 = Cell(zone.MinZ), z1 = Cell(zone.MaxZ);
                if ((long)x1-x0 > 4096 || (long)z1-z0 > 4096 || ((long)x1-x0+1)*((long)z1-z0+1)>65536)
                    throw new ArgumentException("Polygon covers too many cells");
                for (long x = x0; x <= x1; x++) for (long z = z0; z <= z1; z++)
                {
                    long key = Key((int)x,(int)z);
                    List<Zone> bucket;
                    if (!cells.TryGetValue(key,out bucket)) cells.Add(key,bucket = new List<Zone>());
                    bucket.Add(zone);
                }
            }
        }

        /// <summary>Allocation-free X/Z lookup. False includes both dry and unmapped coordinates.
        /// Check the cast target, not the player's feet. Verify occlusion/shore/hull separately.</summary>
        public bool TryGetCandidate(double x, double z, out Zone zone)
        {
            zone = null;
            if (!Finite(x) || !Finite(z) || x/cellSize <= Int32.MinValue || x/cellSize >= Int32.MaxValue ||
                z/cellSize <= Int32.MinValue || z/cellSize >= Int32.MaxValue) return false;
            List<Zone> bucket;
            if (!cells.TryGetValue(Key(Cell(x),Cell(z)),out bucket)) return false;
            foreach (Zone candidate in bucket)
                if (candidate.Contains(x,z)) { zone = candidate; return true; }
            return false;
        }

        /// <summary>Shortest horizontal distance to the water area. Zero inside
        /// water or on its boundary; holes measure to their shoreline. Invalid
        /// coordinates or an empty atlas return infinity. No managed allocations.</summary>
        public double DistanceToWater(double x, double z)
        {
            if (!Finite(x) || !Finite(z)) return Double.PositiveInfinity;
            double best=Double.PositiveInfinity;
            foreach (Zone zone in zones)
            {
                best=zone.DistanceSquared(x,z,best);
                if (best==0) return 0;
            }
            return Math.Sqrt(best);
        }

        private int Cell(double coordinate)
        {
            double c = Math.Floor(coordinate/cellSize);
            if (c <= Int32.MinValue || c >= Int32.MaxValue) throw new ArgumentException("Coordinate outside index range");
            return (int)c;
        }
        private static long Key(int x, int z) { return ((long)x << 32) | (uint)z; }
        private static bool Finite(double x) { return !Double.IsNaN(x) && !Double.IsInfinity(x); }
    }
}
