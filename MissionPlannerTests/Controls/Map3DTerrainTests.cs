using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Utilities;
using OpenTK;

namespace MissionPlanner.Controls.Scene3D.Tests
{
    [TestClass]
    public class UtmProjectionTests
    {
        static readonly double[][] Places =
        {
            new[] { 37.4219, -122.0841 },  // Mountain View
            new[] { 40.0150, -105.2705 },  // Boulder
            new[] { 33.7490, -84.3880 },   // Atlanta
            new[] { 51.5072, -0.1276 },    // London
            new[] { -33.8688, 151.2093 },  // Sydney
            new[] { -37.8136, 144.9631 },  // Melbourne
            new[] { 1.3521, 103.8198 },    // Singapore
            new[] { 64.1466, -21.9426 }    // Reykjavik
        };

        [TestMethod]
        public void ForwardMatchesTheExistingProjNetConversion()
        {
            foreach (var place in Places)
            {
                var point = new PointLatLngAlt(place[0], place[1]);
                int zone = point.GetUTMZone();
                var expected = point.ToUTM(zone);

                double e, n;
                UtmProjection.Forward(place[0], place[1], zone, out e, out n);

                Assert.AreEqual(expected[0], e, 0.02, "easting at " + point);
                Assert.AreEqual(expected[1], n, 0.02, "northing at " + point);
                Assert.AreEqual(zone, UtmProjection.ZoneFor(place[0], place[1]));
            }
        }

        [TestMethod]
        public void InverseRoundTripsInsideAndWellOutsideTheZone()
        {
            foreach (var place in Places)
            {
                int zone = UtmProjection.ZoneFor(place[0], place[1]);
                // Up to ~5 degrees of longitude away from the zone (several hundred km).
                foreach (double offset in new[] { 0.0, 2.0, -3.0, 5.0 })
                {
                    double lng = place[1] + offset;
                    double e, n, lat2, lng2;
                    UtmProjection.Forward(place[0], lng, zone, out e, out n);
                    UtmProjection.Inverse(e, n, zone, out lat2, out lng2);
                    Assert.AreEqual(place[0], lat2, 1e-8);
                    Assert.AreEqual(lng, lng2, 1e-8);
                }
            }
        }

        [TestMethod]
        public void WorldFrameKeepsOriginNearTheCentreWithoutChangingZone()
        {
            var frame = new WorldFrame();
            Assert.IsTrue(frame.Update(40.0, -105.0));
            int zoneVersion = frame.ZoneVersion;
            int version = frame.Version;

            Assert.IsFalse(frame.Update(40.01, -105.0)); // ~1 km: no change
            Assert.IsTrue(frame.Update(40.2, -105.0));   // ~22 km: rebase
            Assert.AreEqual(version + 1, frame.Version);
            Assert.AreEqual(zoneVersion, frame.ZoneVersion);

            double x, y;
            frame.ToLocal(40.2, -105.0, out x, out y);
            Assert.AreEqual(0, x, 1e-6);
            Assert.AreEqual(0, y, 1e-6);

            var back = frame.ToLatLng(1000, 2000, 5);
            double bx, by;
            frame.ToLocal(back.Lat, back.Lng, out bx, out by);
            Assert.AreEqual(1000, bx, 1e-4);
            Assert.AreEqual(2000, by, 1e-4);
        }

        [TestMethod]
        public void WorldFrameChangesZoneDuringALongFlight()
        {
            // Fly east from the middle of zone 13 in ~4 km steps; the origin rebases every 10 km on the way.
            var frame = new WorldFrame();
            frame.Update(45.0, -105.0);
            Assert.AreEqual(13, frame.Zone);
            int zoneVersion = frame.ZoneVersion;

            double lng = -105.0;
            while (lng < -100.0)
            {
                lng += 0.05;
                frame.Update(45.0, lng);
                Assert.IsTrue(UtmProjection.LongitudeFromCentralMeridian(lng, frame.Zone) <= WorldFrame.ZoneChangeLongitude + 0.05);
            }

            Assert.AreEqual(14, frame.Zone);
            Assert.AreEqual(zoneVersion + 1, frame.ZoneVersion);
        }
    }

    [TestClass]
    public class TileMathTests
    {
        [TestMethod]
        public void TileCoordinatesMatchGMapMercator()
        {
            var projection = GMap.NET.Projections.MercatorProjection.Instance;
            foreach (int zoom in new[] { 3, 9, 14, 17, 19 })
            {
                double x, y;
                TileMath.LatLngToTile(37.4219, -122.0841, zoom, out x, out y);
                var pixel = projection.FromLatLngToPixel(37.4219, -122.0841, zoom);
                Assert.AreEqual(pixel.X / 256.0, x, 0.01);
                Assert.AreEqual(pixel.Y / 256.0, y, 0.01);

                Assert.AreEqual(37.4219, TileMath.TileYToLat(y, zoom), 1e-9);
                Assert.AreEqual(-122.0841, TileMath.TileXToLng(x, zoom), 1e-9);
            }
        }

        [TestMethod]
        public void ChildrenAndParentsAreConsistent()
        {
            var tile = new TileId(1234, 5678, 14);
            for (int c = 0; c < 4; c++)
            {
                var child = tile.Child(c);
                Assert.AreEqual(15, child.Z);
                Assert.AreEqual(tile, child.Parent);
                Assert.AreEqual(tile, child.Child(3).AncestorAt(14));
            }

            Assert.AreNotEqual(tile.Child(0), tile.Child(3));
            Assert.AreEqual(new TileId(1234, 5678, 14), tile);
            Assert.AreNotEqual(new TileId(1234, 5678, 15), tile); // same x/y, different zoom
        }
    }

    [TestClass]
    public class TileMeshBuilderTests
    {
        [TestMethod]
        public void GridSizeFollowsElevationResolutionAtEveryLatitude()
        {
            // The previous loader passed degrees to Math.Cos, which gave 257x257 grids at these latitudes.
            foreach (double lat in new[] { 30.0, 33.7, 37.4, 40.0, 45.0, -33.9, 60.0 })
            {
                int previous = int.MaxValue;
                for (int zoom = 9; zoom <= 20; zoom++)
                {
                    double x, y;
                    TileMath.LatLngToTile(lat, 10, zoom, out x, out y);
                    var id = new TileId((int)x, (int)y, zoom);
                    int n = TileMeshBuilder.GridSizeFor(id);

                    Assert.IsTrue(n >= TileMeshBuilder.MinGrid && n <= TileMeshBuilder.MaxGrid, "grid " + n);
                    Assert.IsTrue(n <= previous, "finer tiles never get denser grids");
                    double cell = TileMath.TileSizeMeters(lat, zoom) / n;
                    Assert.IsTrue(n == TileMeshBuilder.MinGrid || cell >= TileMeshBuilder.DemResolutionMeters / 2,
                        "cells of " + cell + " m at zoom " + zoom);
                    previous = n;
                }
            }
        }

        [TestMethod]
        public void MeshHasSkirtsAndConsistentIndices()
        {
            var id = new TileId(26943, 51200, 17); // Colorado-ish
            int n = TileMeshBuilder.GridSizeFor(id);
            double[] rowLats, colLngs;
            TileMeshBuilder.GridLatLngs(id, n, out rowLats, out colLngs);
            var heights = new float[(n + 1) * (n + 1)];
            for (int k = 0; k < heights.Length; k++)
                heights[k] = 1600 + k % 7;

            int zone = UtmProjection.ZoneFor(rowLats[0], colLngs[0]);
            var mesh = TileMeshBuilder.Build(id, n, rowLats, colLngs, heights, zone);

            Assert.AreEqual(TileMeshBuilder.VertexCount(n) * TileMeshBuilder.FloatsPerVertex, mesh.Vertices.Length);
            var indices = TileMeshBuilder.BuildIndices(n);
            Assert.AreEqual(TileMeshBuilder.IndexCount(n), indices.Length);
            Assert.IsTrue(indices.All(i => i < TileMeshBuilder.VertexCount(n)));

            // Every vertex is used.
            Assert.AreEqual(TileMeshBuilder.VertexCount(n), indices.Distinct().Count());

            // Skirt vertices hang below the surface; the reported range includes them.
            int skirtStart = (n + 1) * (n + 1);
            for (int v = skirtStart; v < TileMeshBuilder.VertexCount(n); v++)
                Assert.IsTrue(mesh.Vertices[v * 5 + 2] < 1600);
            Assert.IsTrue(mesh.MinHeight < 1600);
            Assert.AreEqual(1606, mesh.MaxHeight, 1e-3);

            // Positions are relative to the tile centre, so they stay small.
            for (int v = 0; v < skirtStart; v++)
            {
                Assert.IsTrue(Math.Abs(mesh.Vertices[v * 5]) < 400);
                Assert.IsTrue(Math.Abs(mesh.Vertices[v * 5 + 1]) < 400);
            }

            // Texture coordinates span the tile, north row first.
            Assert.AreEqual(0, mesh.Vertices[3]);
            Assert.AreEqual(0, mesh.Vertices[4]);
            Assert.AreEqual(1, mesh.Vertices[(skirtStart - 1) * 5 + 3], 1e-6);
            Assert.AreEqual(1, mesh.Vertices[(skirtStart - 1) * 5 + 4], 1e-6);
        }

        [TestMethod]
        public void AncestorHeightsAreInterpolatedIntoChildren()
        {
            // A parent sloping east: height = column index * 10.
            var parent = new TileId(100, 200, 12);
            int pn = 8;
            var parentHeights = new float[(pn + 1) * (pn + 1)];
            for (int j = 0; j <= pn; j++)
                for (int i = 0; i <= pn; i++)
                    parentHeights[j * (pn + 1) + i] = i * 10;

            var child = parent.Child(1); // north-east quarter
            int cn = 4;
            var heights = new float[(cn + 1) * (cn + 1)];
            ElevationSampler.SampleFromAncestor(child, cn, parent, parentHeights, pn, heights);

            // The child's west edge is the parent's middle column, its east edge the parent's east edge.
            Assert.AreEqual(40, heights[0], 1e-4);
            Assert.AreEqual(80, heights[cn], 1e-4);
            Assert.AreEqual(60, heights[cn / 2], 1e-4);
        }
    }

    [TestClass]
    public class TerrainQuadtreeTests
    {
        const double Lat = 40.0150, Lng = -105.2705;

        static SelectionParams View(WorldFrame frame, double heightAboveGround, double headingDeg, double pitchDeg,
            double lat = Lat, double lng = Lng, double ground = 1600)
        {
            double x, y;
            frame.ToLocal(lat, lng, out x, out y);
            double z = ground + heightAboveGround;

            double heading = headingDeg * Math.PI / 180, pitch = pitchDeg * Math.PI / 180;
            double fx = Math.Sin(heading) * Math.Cos(pitch), fy = Math.Cos(heading) * Math.Cos(pitch), fz = Math.Sin(pitch);
            double radius = Map3D.ViewRadius(heightAboveGround);

            var view = Matrix4.LookAt((float)x, (float)y, (float)z, (float)(x + fx * 100), (float)(y + fy * 100), (float)(z + fz * 100), 0, 0, 1);
            var projection = Matrix4.CreatePerspectiveFieldOfView((float)(Math.PI / 3), 1.5f, 1f, (float)(radius * 1.05));

            return new SelectionParams
            {
                CameraX = x,
                CameraY = y,
                CameraZ = z,
                ForwardX = fx,
                ForwardY = fy,
                ForwardZ = fz,
                CameraLat = lat,
                CameraLng = lng,
                Frustum = Frustum.FromViewProjection(Matrix4.Mult(view, projection)),
                ViewRadius = radius,
                ViewportHeight = 600,
                FovY = Math.PI / 3,
                MaxZoom = 18,
                ReferenceHeight = ground
            };
        }

        static void LoadAll(TerrainQuadtree tree, IEnumerable<TileRequest> requests, float minHeight = 1550, float maxHeight = 1650)
        {
            foreach (var request in requests.ToList())
            {
                var tile = tree.GetOrCreate(request.Id);
                tile.State = TileState.Ready;
                tile.HeightsKnown = true;
                tile.MinHeight = minHeight;
                tile.MaxHeight = maxHeight;
            }
        }

        static bool Contains(TileId tile, double lat, double lng)
        {
            double x, y;
            TileMath.LatLngToTile(lat, lng, tile.Z, out x, out y);
            return (int)Math.Floor(x) == tile.X && (int)Math.Floor(y) == tile.Y;
        }

        [TestMethod]
        public void StartsWithCoarseCoverageAroundTheCamera()
        {
            var frame = new WorldFrame();
            frame.Update(Lat, Lng);
            var tree = new TerrainQuadtree(frame);
            tree.Select(View(frame, 100, 0, -10), 0);

            Assert.AreEqual(0, tree.DrawList.Count);
            Assert.IsTrue(tree.Requests.Count > 0);
            Assert.IsTrue(tree.Requests.All(r => r.Id.Z <= TerrainQuadtree.RootZoom + 1));

            // The root under the camera comes first.
            var first = tree.Requests[0].Id;
            Assert.AreEqual(TerrainQuadtree.RootZoom, first.Z);
            Assert.IsTrue(Contains(first, Lat, Lng));
        }

        [TestMethod]
        public void RefinesWithoutHolesOrOverlapsAndPutsDetailUnderTheCamera()
        {
            var frame = new WorldFrame();
            frame.Update(Lat, Lng);
            var tree = new TerrainQuadtree(frame);
            var view = View(frame, 100, 0, -20);

            for (int round = 0; round < 20; round++)
            {
                tree.Select(view, 0);
                if (tree.Requests.Count == 0)
                    break;
                LoadAll(tree, tree.Requests);
            }

            tree.Select(view, 0);
            var drawn = tree.DrawList.Select(t => t.Id).ToList();
            Assert.IsTrue(drawn.Count > 0);

            // No tile is drawn together with one of its ancestors.
            var set = new HashSet<TileId>(drawn);
            foreach (var id in drawn)
            {
                var ancestor = id;
                while (ancestor.Z > TerrainQuadtree.RootZoom)
                {
                    ancestor = ancestor.Parent;
                    Assert.IsFalse(set.Contains(ancestor), id + " overlaps " + ancestor);
                }
            }

            // Points just ahead of the camera are covered, by fine tiles.
            double x, y;
            frame.ToLocal(Lat, Lng, out x, out y);
            var ahead = frame.ToLatLng(x, y + 400, 0);
            var covering = drawn.Where(t => Contains(t, ahead.Lat, ahead.Lng)).ToList();
            Assert.AreEqual(1, covering.Count);
            Assert.IsTrue(covering[0].Z >= 16, "zoom " + covering[0].Z);

            // Far terrain is coarse.
            var far = frame.ToLatLng(x, y + 20000, 0);
            var farTile = drawn.Single(t => Contains(t, far.Lat, far.Lng));
            Assert.IsTrue(farTile.Z <= 13, "zoom " + farTile.Z);

            // The selection stays bounded.
            Assert.IsTrue(drawn.Count < 400, drawn.Count + " tiles");
        }

        [TestMethod]
        public void RefinesWhenSomeChildrenAreBeyondTheViewRadius()
        {
            // Miami Beach: the camera sits near the corner of its root tile, so some root children lie wholly
            // beyond the view radius and are never loaded. They must not stop the roots from refining.
            const double lat = 25.7851726, lng = -80.129338;
            var frame = new WorldFrame();
            frame.Update(lat, lng);
            var tree = new TerrainQuadtree(frame);
            var view = View(frame, 100, 0, 0, lat, lng, 2);
            view.MaxZoom = 17;

            for (int round = 0; round < 20; round++)
            {
                tree.Select(view, 0);
                if (tree.Requests.Count == 0)
                    break;
                LoadAll(tree, tree.Requests, -5, 20);
            }

            tree.Select(view, 0);
            double x, y;
            frame.ToLocal(lat, lng, out x, out y);
            var ahead = frame.ToLatLng(x, y + 300, 0);
            var covering = tree.DrawList.Select(t => t.Id).Where(t => Contains(t, ahead.Lat, ahead.Lng)).ToList();
            Assert.AreEqual(1, covering.Count);
            Assert.IsTrue(covering[0].Z >= 16, "zoom " + covering[0].Z);
        }

        [TestMethod]
        public void LoadsNearAndCentreOfViewBeforeTheEdges()
        {
            var frame = new WorldFrame();
            frame.Update(Lat, Lng);
            var tree = new TerrainQuadtree(frame);
            var view = View(frame, 150, 90, -15); // looking east

            // Load a few rounds, then inspect the order of the next requests at one zoom level.
            for (int round = 0; round < 6; round++)
            {
                tree.Select(view, 0);
                LoadAll(tree, tree.Requests.Take(tree.Requests.Count / 2 + 1));
            }

            tree.Select(view, 0);
            var requests = tree.Requests;
            Assert.IsTrue(requests.Count > 10);

            double cx, cy;
            frame.ToLocal(Lat, Lng, out cx, out cy);
            Func<TileId, double> distance = id =>
            {
                double ex, ey;
                frame.ToLocal(TileMath.CenterLat(id), TileMath.CenterLng(id), out ex, out ey);
                return Math.Sqrt((ex - cx) * (ex - cx) + (ey - cy) * (ey - cy));
            };

            // Tiles out of view are deliberately loaded last, so compare those in front of the camera (east).
            Func<TileId, bool> inFront = id =>
            {
                double ex, ey;
                frame.ToLocal(TileMath.CenterLat(id), TileMath.CenterLng(id), out ex, out ey);
                double dx = ex - cx, dy = ey - cy;
                return dx > 0 && Math.Abs(dy) < dx;
            };

            foreach (var zoomGroup in requests.Where(r => r.Kind == TileJobKind.Full && inFront(r.Id)).GroupBy(r => r.Id.Z))
            {
                var ordered = zoomGroup.ToList();
                if (ordered.Count < 4)
                    continue;
                // Within a level, the first quarter is on average nearer than the last quarter.
                int quarter = ordered.Count / 4;
                double nearAverage = ordered.Take(quarter).Average(r => distance(r.Id));
                double farAverage = ordered.Skip(ordered.Count - quarter).Average(r => distance(r.Id));
                Assert.IsTrue(nearAverage <= farAverage, "zoom " + zoomGroup.Key);
            }
        }

        [TestMethod]
        public void MissingImageryStopsRefinementInsteadOfLeavingHoles()
        {
            var frame = new WorldFrame();
            frame.Update(Lat, Lng);
            var tree = new TerrainQuadtree(frame);
            var view = View(frame, 100, 0, -20);
            view.MaxZoom = 20;

            for (int round = 0; round < 25; round++)
            {
                tree.Select(view, 0);
                foreach (var request in tree.Requests.ToList())
                {
                    var tile = tree.GetOrCreate(request.Id);
                    if (request.Id.Z >= 17)
                    {
                        tile.State = TileState.NoData;
                        tile.RetryAtTicks = long.MaxValue;
                    }
                    else
                    {
                        tile.State = TileState.Ready;
                    }
                }
            }

            tree.Select(view, 0);
            Assert.IsTrue(tree.DrawList.Count > 0);
            Assert.IsTrue(tree.DrawList.All(t => t.Id.Z <= 16));
            Assert.IsTrue(tree.DrawList.Any(t => t.Id.Z == 16));
        }

        [TestMethod]
        public void OfflineRefinesPastUncachedLevelsToCachedOnes()
        {
            // An offline map pack that starts at zoom 13: nothing is cached above it.
            var frame = new WorldFrame();
            frame.Update(Lat, Lng);
            var tree = new TerrainQuadtree(frame);
            var view = View(frame, 100, 0, -20);

            for (int round = 0; round < 25; round++)
            {
                tree.Select(view, 0);
                foreach (var request in tree.Requests.ToList())
                {
                    var tile = tree.GetOrCreate(request.Id);
                    if (request.Id.Z < 13)
                    {
                        tile.State = TileState.Unavailable;
                        tile.RetryAtTicks = long.MaxValue;
                    }
                    else
                    {
                        tile.State = TileState.Ready;
                        tile.HeightsKnown = true;
                        tile.MinHeight = 1550;
                        tile.MaxHeight = 1650;
                    }
                }
            }

            tree.Select(view, 0);
            double x, y;
            frame.ToLocal(Lat, Lng, out x, out y);
            var ahead = frame.ToLatLng(x, y + 400, 0);
            var covering = tree.DrawList.Where(t => Contains(t.Id, ahead.Lat, ahead.Lng)).ToList();
            Assert.AreEqual(1, covering.Count);
            Assert.IsTrue(covering[0].Id.Z >= 16, "zoom " + covering[0].Id.Z);
            Assert.IsTrue(tree.DrawList.All(t => t.Id.Z >= 13));
        }

        [TestMethod]
        public void OfflineKeepsACachedParentRatherThanLeavingHoles()
        {
            // Only the roots are cached.
            var frame = new WorldFrame();
            frame.Update(Lat, Lng);
            var tree = new TerrainQuadtree(frame);
            var view = View(frame, 100, 0, -20);

            for (int round = 0; round < 5; round++)
            {
                tree.Select(view, 0);
                foreach (var request in tree.Requests.ToList())
                {
                    var tile = tree.GetOrCreate(request.Id);
                    tile.State = request.Id.Z == TerrainQuadtree.RootZoom ? TileState.Ready : TileState.Unavailable;
                    tile.RetryAtTicks = long.MaxValue;
                }
            }

            tree.Select(view, 0);
            Assert.IsTrue(tree.DrawList.Count > 0);
            Assert.IsTrue(tree.DrawList.All(t => t.Id.Z == TerrainQuadtree.RootZoom));
            Assert.IsTrue(tree.DrawList.Any(t => Contains(t.Id, Lat, Lng)));
        }

        [TestMethod]
        public void PruningKeepsTilesThatAreWaitingToRetry()
        {
            var frame = new WorldFrame();
            frame.Update(Lat, Lng);
            var tree = new TerrainQuadtree(frame);
            var noData = tree.GetOrCreate(new TileId(1, 2, 19));
            noData.State = TileState.NoData;
            noData.RetryAtTicks = 1000;
            tree.GetOrCreate(new TileId(3, 4, 19));

            for (int i = 0; i < 10; i++)
                tree.Select(View(frame, 100, 0, -20), 0);

            var removed = new List<TileId>();
            tree.PruneStale(5, 500, removed);
            TerrainTile tile;
            Assert.IsTrue(tree.TryGet(new TileId(1, 2, 19), out tile), "still backing off");
            Assert.IsFalse(tree.TryGet(new TileId(3, 4, 19), out tile));

            tree.PruneStale(5, 2000, removed);
            Assert.IsFalse(tree.TryGet(new TileId(1, 2, 19), out tile), "retry due");
        }

        [TestMethod]
        public void TerrainContinuesAcrossTheAntimeridian()
        {
            // Taveuni, Fiji, right next to 180 degrees.
            const double lat = -16.85, lng = 179.95;
            var frame = new WorldFrame();
            frame.Update(lat, lng);
            var tree = new TerrainQuadtree(frame);
            var view = View(frame, 300, 90, -5, lat, lng, 0); // looking east, across the line
            view.MaxZoom = 14;

            for (int round = 0; round < 20; round++)
            {
                tree.Select(view, 0);
                if (tree.Requests.Count == 0)
                    break;
                LoadAll(tree, tree.Requests, -10, 50);
            }

            tree.Select(view, 0);
            var across = tree.DrawList.Where(t => Contains(t.Id, lat, -179.9)).ToList();
            Assert.AreEqual(1, across.Count, "terrain on the far side");
        }

        [TestMethod]
        public void ViewRadiusGrowsWithAltitudeAndHidesTheEdgeInFog()
        {
            double previous = 0;
            foreach (double height in new[] { 1.0, 30, 100, 500, 1000, 5000, 20000 })
            {
                double radius = Map3D.ViewRadius(height);
                Assert.IsTrue(radius >= previous);
                Assert.IsTrue(radius >= 30000 && radius <= 150000);

                float fogStart, fogEnd;
                TerrainEngine.FogRange(radius, out fogStart, out fogEnd);
                Assert.IsTrue(fogStart < fogEnd);
                Assert.IsTrue(fogEnd < radius, "terrain must reach past full fog");
                previous = radius;
            }
        }
    }

    [TestClass]
    public class MaxZoomTests
    {
        [TestMethod]
        public void TheFinestLevelFollowsTheHeightAboveTheGround()
        {
            Assert.AreEqual(24, SelectionParams.MaxZoomFor(30, 20));
            Assert.AreEqual(22, SelectionParams.MaxZoomFor(300, 20));
            Assert.AreEqual(20, SelectionParams.MaxZoomFor(2000, 24));
        }

        [TestMethod]
        public void ALevelHoldsUntilTheHeightIsClearlyPastItsBoundary()
        {
            // climbing out of 24
            Assert.AreEqual(24, SelectionParams.MaxZoomFor(105, 24));
            Assert.AreEqual(22, SelectionParams.MaxZoomFor(115, 24));
            // and back down into it
            Assert.AreEqual(22, SelectionParams.MaxZoomFor(95, 22));
            Assert.AreEqual(24, SelectionParams.MaxZoomFor(85, 22));

            // the same around 500 m
            Assert.AreEqual(22, SelectionParams.MaxZoomFor(520, 22));
            Assert.AreEqual(20, SelectionParams.MaxZoomFor(560, 22));
            Assert.AreEqual(20, SelectionParams.MaxZoomFor(480, 20));
            Assert.AreEqual(22, SelectionParams.MaxZoomFor(440, 20));
        }

        [TestMethod]
        public void HoveringAtABoundaryNeverFlipsTheLevel()
        {
            int level = 24;
            var random = new Random(1);
            for (int i = 0; i < 1000; i++)
            {
                int next = SelectionParams.MaxZoomFor(100 + (random.NextDouble() - 0.5) * 6, level);
                Assert.AreEqual(level, next, "at frame " + i);
            }
        }
    }

    [TestClass]
    public class FrameStatsTests
    {
        [TestMethod]
        public void KeepsTheLatestFramesOldestFirst()
        {
            var stats = new FrameStats(4);
            Assert.AreEqual(0, stats.Count);

            for (int i = 1; i <= 6; i++)
                stats.Add(10 * i, i);

            Assert.AreEqual(4, stats.Count);
            CollectionAssert.AreEqual(new[] { 30f, 40f, 50f, 60f }, Enumerable.Range(0, 4).Select(stats.Interval).ToArray());
            CollectionAssert.AreEqual(new[] { 3f, 4f, 5f, 6f }, Enumerable.Range(0, 4).Select(stats.Cpu).ToArray());

            Assert.AreEqual(1000 / 45.0, stats.AverageFps(), 1e-6);
            Assert.AreEqual(1000 / 60.0, stats.LowFps(), 1e-6);
            Assert.AreEqual(1000 / 55.0, stats.CurrentFps(2), 1e-6);
            Assert.AreEqual(5.5, stats.MeanCpu(2), 1e-6);
        }
    }

    [TestClass]
    public class TileLoaderTests
    {
        sealed class FakeImagery : ITileImagerySource
        {
            public readonly List<TileId> Fetched = new List<TileId>();
            public readonly ManualResetEventSlim Gate = new ManualResetEventSlim(true);

            public ImageryResult Fetch(TileId id)
            {
                Gate.Wait();
                lock (Fetched)
                    Fetched.Add(id);
                if (id.Z > 15)
                    return new ImageryResult { Outcome = TileLoadOutcome.NoData };
                return new ImageryResult { Outcome = TileLoadOutcome.Loaded, Pixels = new byte[4], Width = 1, Height = 1 };
            }
        }

        // Tiles around Boulder, CO (UTM zone 13), numbered by x offset.
        static TileRequest Request(int x, double priority, int zoom = 12)
        {
            int scale = 1 << (zoom - 12);
            return new TileRequest { Id = new TileId((800 + x) * scale, 1550 * scale, zoom), Priority = priority, GridN = 4, Zone = 13, FallbackHeight = 42 };
        }

        static int[] Fetched(FakeImagery imagery)
        {
            lock (imagery.Fetched)
                return imagery.Fetched.Select(t => (t.X >> (t.Z - 12)) - 800).ToArray();
        }

        static List<TileLoadResult> Collect(TileLoader loader, int count)
        {
            var results = new List<TileLoadResult>();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (results.Count < count && DateTime.UtcNow < deadline)
            {
                TileLoadResult result;
                if (loader.TryGetResult(out result))
                    results.Add(result);
                else
                    Thread.Sleep(5);
            }

            return results;
        }

        [TestMethod]
        public void TakesTheBestRequestFirstAndBuildsMeshes()
        {
            var imagery = new FakeImagery();
            imagery.Gate.Reset();
            using (var loader = new TileLoader(imagery, 1, (lats, lngs, heights) => DemStatus.Pending))
            {
                loader.SetRequests(new[] { Request(1, 5), Request(2, 1), Request(3, 3) });
                imagery.Gate.Set();

                var results = Collect(loader, 3);
                Assert.AreEqual(3, results.Count);
                CollectionAssert.AreEqual(new[] { 2, 3, 1 }, Fetched(imagery));

                foreach (var result in results)
                {
                    Assert.AreEqual(TileLoadOutcome.Loaded, result.Outcome);
                    Assert.IsTrue(result.HeightsAreFallback);
                    Assert.IsTrue(result.Mesh.Heights.All(h => h == 42));
                }
            }
        }

        [TestMethod]
        public void DropsRequestsThatAreNoLongerWanted()
        {
            var imagery = new FakeImagery();
            imagery.Gate.Reset();
            using (var loader = new TileLoader(imagery, 1, (lats, lngs, heights) => DemStatus.Ready))
            {
                loader.SetRequests(new[] { Request(1, 1), Request(2, 2), Request(3, 3) });
                Thread.Sleep(100); // the worker picks up tile 1 and blocks
                loader.SetRequests(new[] { Request(3, 3), Request(1, 1) });
                imagery.Gate.Set();

                var results = Collect(loader, 2);
                Assert.AreEqual(2, results.Count);
                Thread.Sleep(100);
                CollectionAssert.AreEquivalent(new[] { 1, 3 }, Fetched(imagery));
            }
        }

        [TestMethod]
        public void ReportsMissingImagery()
        {
            var imagery = new FakeImagery();
            using (var loader = new TileLoader(imagery, 2, (lats, lngs, heights) => DemStatus.Ready))
            {
                loader.SetRequests(new[] { Request(1, 1, 17) });
                var results = Collect(loader, 1);
                Assert.AreEqual(TileLoadOutcome.NoData, results.Single().Outcome);
                Assert.IsNull(results.Single().Mesh);
            }
        }

        [TestMethod]
        public void DisposeLetsTheWorkersFinishAndKeepsNoResults()
        {
            var imagery = new FakeImagery();
            imagery.Gate.Reset();
            var loader = new TileLoader(imagery, 2, (lats, lngs, heights) => DemStatus.Ready);
            loader.SetRequests(new[] { Request(1, 1), Request(2, 2) });
            Thread.Sleep(100); // both workers block in Fetch

            loader.Dispose();
            Assert.IsFalse(loader.WaitForWorkers(50), "workers are still inside Fetch");

            imagery.Gate.Set();
            Assert.IsTrue(loader.WaitForWorkers(5000));

            TileLoadResult result;
            Assert.IsFalse(loader.TryGetResult(out result), "jobs finished after Dispose must not be kept");
        }
    }
}
