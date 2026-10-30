using System;
using System.Collections.Generic;

namespace MissionPlanner.Controls.Scene3D
{
    /// <summary>
    /// Per-frame inputs to tile selection, in local scene coordinates.
    /// </summary>
    public sealed class SelectionParams
    {
        public double CameraX, CameraY, CameraZ;

        /// <summary>Unit view direction.</summary>
        public double ForwardX, ForwardY = 1, ForwardZ;

        public double CameraLat, CameraLng;
        public Frustum Frustum;

        /// <summary>Terrain is selected out to this distance and fully fogged just before it.</summary>
        public double ViewRadius = 40000;

        public int ViewportHeight = 600;
        public double FovY = Math.PI / 3;

        /// <summary>
        /// Finest imagery level. Screen-space error decides how much of it is used, so this only matters close to
        /// the ground; where a provider has nothing this deep, tiles report no data and the parent stays.
        /// </summary>
        public int MaxZoom = 20;

        /// <summary>
        /// The finest imagery level for a camera this high above the ground: 24 within 100 m, 22 up to 500 m, 20
        /// above. Each boundary has 10% of hysteresis around it, so a camera hovering at one doesn't flip the level
        /// (and the detail drawn) from frame to frame.
        /// </summary>
        public static int MaxZoomFor(double heightAboveGround, int current)
        {
            int Band(double height) => height < 100 ? 24 : height < 500 ? 22 : 20;

            // only change once the height is clearly past the boundary between the two levels
            int wanted = Band(heightAboveGround);
            if (wanted == current)
                return current;
            return Band(heightAboveGround * 1.1) == wanted && Band(heightAboveGround / 1.1) == wanted ? wanted : current;
        }

        /// <summary>Refine until one imagery texel covers at most this many screen pixels.</summary>
        public double MaxTexelPixels = 1.25;

        /// <summary>Ground height near the camera, used for tiles whose heights are not known yet.</summary>
        public double ReferenceHeight;
    }

    /// <summary>
    /// Chooses which terrain tiles to draw and which to load, Google Earth style:
    /// <list type="bullet">
    /// <item>A quadtree is refined from coarse root tiles until each tile's texels are about a pixel on
    /// screen, so detail follows what the camera actually sees.</item>
    /// <item>A tile is only replaced by its children once all four are loaded, so there are never holes or
    /// overlapping levels; the parent stands in while they load.</item>
    /// <item>Load requests are ranked by distance relative to tile size and by angle from the view centre,
    /// which loads coarse coverage first and then refines from the middle of the screen outwards.</item>
    /// </list>
    /// Pure logic with no GL calls; owned by the render thread.
    /// </summary>
    public sealed class TerrainQuadtree
    {
        public const int RootZoom = 9;

        /// <summary>Tiles outside the view are prefetched this many times coarser, so turning never shows holes.</summary>
        public const double PrefetchFactor = 4;

        /// <summary>Priority penalty for tiles outside the view.</summary>
        public const double OutOfViewPenalty = 8;

        /// <summary>Priority added per zoom level below the roots.</summary>
        public const double LevelBias = 0.1;

        public const int MaxRequests = 256;

        readonly WorldFrame _frame;
        readonly Dictionary<TileId, TerrainTile> _tiles = new Dictionary<TileId, TerrainTile>();
        readonly HashSet<TileId> _requested = new HashSet<TileId>();

        /// <summary>Tiles to draw this frame.</summary>
        public readonly List<TerrainTile> DrawList = new List<TerrainTile>();

        /// <summary>Tiles to load, best first.</summary>
        public readonly List<TileRequest> Requests = new List<TileRequest>();

        public int Frame { get; private set; }

        public TerrainQuadtree(WorldFrame frame)
        {
            _frame = frame;
        }

        public int Count
        {
            get { return _tiles.Count; }
        }

        public IEnumerable<TerrainTile> Tiles
        {
            get { return _tiles.Values; }
        }

        public bool TryGet(TileId id, out TerrainTile tile)
        {
            return _tiles.TryGetValue(id, out tile);
        }

        public TerrainTile GetOrCreate(TileId id)
        {
            TerrainTile tile;
            if (!_tiles.TryGetValue(id, out tile))
            {
                tile = new TerrainTile(id);
                _tiles[id] = tile;
            }

            return tile;
        }

        public void Remove(TileId id)
        {
            _tiles.Remove(id);
        }

        public void Clear()
        {
            _tiles.Clear();
            DrawList.Clear();
            Requests.Clear();
        }

        public void Select(SelectionParams p, long nowTicks)
        {
            Frame++;
            DrawList.Clear();
            Requests.Clear();
            _requested.Clear();

            if (!_frame.IsValid)
                return;

            // Root tiles covering the view radius around the camera.
            double dLat = p.ViewRadius / 111320.0;
            double dLng = p.ViewRadius / (111320.0 * Math.Max(0.05, Math.Cos(p.CameraLat * Math.PI / 180.0)));
            double x0, y0, x1, y1;
            TileMath.LatLngToTile(p.CameraLat + dLat, p.CameraLng - dLng, RootZoom, out x0, out y0);
            TileMath.LatLngToTile(p.CameraLat - dLat, p.CameraLng + dLng, RootZoom, out x1, out y1);
            int count = TileMath.TileCount(RootZoom);
            int minX = (int)Math.Floor(x0);
            int maxX = Math.Min(minX + count - 1, (int)Math.Floor(x1));
            int minY = Math.Max(0, (int)Math.Floor(y0));
            int maxY = Math.Min(count - 1, (int)Math.Floor(y1));

            // X wraps across the antimeridian; the projection handles longitudes either side of it.
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                    Visit(new TileId(((x % count) + count) % count, y, RootZoom), p, nowTicks);

            Requests.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            if (Requests.Count > MaxRequests)
                Requests.RemoveRange(MaxRequests, Requests.Count - MaxRequests);
        }

        void Visit(TileId id, SelectionParams p, long now)
        {
            var tile = GetOrCreate(id);
            tile.LastVisitedFrame = Frame;
            RefreshState(tile, now);

            double dist, tileMeters;
            bool inView;
            if (!Measure(tile, p, out dist, out inView, out tileMeters))
                return;

            double texelPixels = tileMeters / TileMath.TileSize * p.ViewportHeight /
                                 (2 * Math.Max(1.0, dist) * Math.Tan(p.FovY / 2));
            double threshold = inView ? p.MaxTexelPixels : p.MaxTexelPixels * PrefetchFactor;
            bool refine = id.Z < p.MaxZoom && texelPixels > threshold && !ChildrenUnavailable(id, now);

            if (refine)
            {
                // Children wholly beyond the view radius are never drawn (they'd be fully fogged), so they
                // are never loaded and must not hold up replacing this tile. Children missing from the offline
                // cache don't either when this tile can't be drawn itself: descending loses nothing and may
                // reach cached tiles further down.
                bool allChildrenReady = true;
                for (int c = 0; c < 4; c++)
                {
                    var child = GetOrCreate(id.Child(c));
                    child.LastVisitedFrame = Frame;
                    RefreshState(child, now);
                    if (child.State == TileState.Ready)
                        continue;
                    if (child.State == TileState.Unavailable && tile.State != TileState.Ready)
                        continue;

                    double childDist, childMeters;
                    bool childInView;
                    if (Measure(child, p, out childDist, out childInView, out childMeters))
                        allChildrenReady = false;
                }

                if (allChildrenReady)
                {
                    for (int c = 0; c < 4; c++)
                        Visit(id.Child(c), p, now);
                    return;
                }

                // This tile stands in for its children while they load.
                Use(tile, p, inView, dist, tileMeters, now);

                for (int c = 0; c < 4; c++)
                {
                    TerrainTile child;
                    _tiles.TryGetValue(id.Child(c), out child);
                    if (child.State == TileState.Ready)
                        continue;

                    double childDist, childMeters;
                    bool childInView;
                    if (Measure(child, p, out childDist, out childInView, out childMeters))
                        Request(child, p, childInView, childDist, childMeters, now);
                }

                return;
            }

            Use(tile, p, inView, dist, tileMeters, now);
        }

        /// <summary>Draw the tile if loaded (and visible), otherwise ask for it.</summary>
        void Use(TerrainTile tile, SelectionParams p, bool inView, double dist, double tileMeters, long now)
        {
            if (tile.State == TileState.Ready)
            {
                if (inView)
                    DrawList.Add(tile);

                if (tile.HeightsAreFallback && now >= tile.RemeshAtTicks)
                    Request(tile, p, inView, dist, tileMeters, now);
            }
            else
            {
                Request(tile, p, inView, dist, tileMeters, now);
            }
        }

        static bool IsBackingOff(TerrainTile tile)
        {
            return tile.State == TileState.Failed || tile.State == TileState.NoData || tile.State == TileState.Unavailable;
        }

        void RefreshState(TerrainTile tile, long now)
        {
            if (IsBackingOff(tile) && now >= tile.RetryAtTicks)
                tile.State = TileState.Unloaded;
        }

        bool ChildrenUnavailable(TileId id, long now)
        {
            for (int c = 0; c < 4; c++)
            {
                TerrainTile child;
                if (_tiles.TryGetValue(id.Child(c), out child) && child.State == TileState.NoData &&
                    now < child.RetryAtTicks)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Distance from the camera to the tile's bounding box, whether it intersects the view, and its size.
        /// Returns false when the tile is beyond the view radius.
        /// </summary>
        bool Measure(TerrainTile tile, SelectionParams p, out double dist, out bool inView, out double tileMeters)
        {
            EnsureBounds(tile);

            double minX = tile.MinEasting - _frame.OriginEasting;
            double maxX = tile.MaxEasting - _frame.OriginEasting;
            double minY = tile.MinNorthing - _frame.OriginNorthing;
            double maxY = tile.MaxNorthing - _frame.OriginNorthing;
            double minZ, maxZ;
            HeightRange(tile, p, out minZ, out maxZ);

            double dx = Math.Max(0, Math.Max(minX - p.CameraX, p.CameraX - maxX));
            double dy = Math.Max(0, Math.Max(minY - p.CameraY, p.CameraY - maxY));
            double dz = Math.Max(0, Math.Max(minZ - p.CameraZ, p.CameraZ - maxZ));
            dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            tileMeters = Math.Max(maxX - minX, maxY - minY);

            if (dist > p.ViewRadius)
            {
                inView = false;
                return false;
            }

            inView = p.Frustum == null || p.Frustum.IntersectsBox(minX, minY, minZ, maxX, maxY, maxZ);
            return true;
        }

        void EnsureBounds(TerrainTile tile)
        {
            if (tile.BoundsZoneVersion == _frame.ZoneVersion)
                return;

            TileMeshBuilder.EstimateBounds(tile.Id, _frame.Zone, out tile.MinEasting, out tile.MaxEasting,
                out tile.MinNorthing, out tile.MaxNorthing);
            tile.BoundsZoneVersion = _frame.ZoneVersion;
        }

        /// <summary>The tile's own height range, else its nearest known ancestor's, else a guess around the camera's ground.</summary>
        void HeightRange(TerrainTile tile, SelectionParams p, out double minZ, out double maxZ)
        {
            var source = tile;
            var id = tile.Id;
            while (!source.HeightsKnown && id.Z > RootZoom)
            {
                id = id.Parent;
                if (!_tiles.TryGetValue(id, out source))
                    break;
            }

            if (source != null && source.HeightsKnown)
            {
                minZ = source.MinHeight;
                maxZ = source.MaxHeight;
            }
            else
            {
                minZ = p.ReferenceHeight - 300;
                maxZ = p.ReferenceHeight + 300;
            }
        }

        void Request(TerrainTile tile, SelectionParams p, bool inView, double dist, double tileMeters, long now)
        {
            if (IsBackingOff(tile))
                return;
            if (!_requested.Add(tile.Id))
                return;

            // Distance in units of the tile's own size: coarse tiles near the camera come before fine ones,
            // and within a level nearer tiles come first. The level term breaks ties toward coarse coverage.
            double priority = dist / Math.Max(1.0, tileMeters);
            if (inView)
            {
                double cx = (tile.MinEasting + tile.MaxEasting) / 2 - _frame.OriginEasting - p.CameraX;
                double cy = (tile.MinNorthing + tile.MaxNorthing) / 2 - _frame.OriginNorthing - p.CameraY;
                double cz = (tile.HeightsKnown ? (tile.MinHeight + tile.MaxHeight) / 2 : p.ReferenceHeight) - p.CameraZ;
                double len = Math.Sqrt(cx * cx + cy * cy + cz * cz);
                if (len > 1e-3)
                {
                    double cos = (cx * p.ForwardX + cy * p.ForwardY + cz * p.ForwardZ) / len;
                    double offAxis = Math.Acos(Math.Max(-1, Math.Min(1, cos))) / Math.Max(0.1, p.FovY);
                    priority *= 1 + 0.75 * Math.Min(2, offAxis);
                }
            }
            else
            {
                priority += OutOfViewPenalty;
            }

            priority += LevelBias * (tile.Id.Z - RootZoom);

            var request = new TileRequest
            {
                Id = tile.Id,
                Kind = tile.State == TileState.Ready ? TileJobKind.Remesh : TileJobKind.Full,
                Priority = priority,
                Zone = _frame.Zone,
                ZoneVersion = _frame.ZoneVersion,
                GridN = TileMeshBuilder.GridSizeFor(tile.Id),
                FallbackHeight = (float)p.ReferenceHeight
            };

            // Start from the closest ancestor with heights, so a tile never pops from flat to terrain twice.
            var id = tile.Id;
            while (id.Z > 0)
            {
                id = id.Parent;
                TerrainTile ancestor;
                if (_tiles.TryGetValue(id, out ancestor) && ancestor.Heights != null)
                {
                    request.FallbackHeights = ancestor.Heights;
                    request.FallbackGridN = ancestor.GridN;
                    request.FallbackSource = id;
                    break;
                }

                if (id.Z <= RootZoom)
                    break;
            }

            Requests.Add(request);
        }

        /// <summary>
        /// Tiles not visited for a while that hold no GPU resources and are not waiting out a retry: safe to forget.
        /// </summary>
        public void PruneStale(int maxAgeFrames, long nowTicks, List<TileId> removed)
        {
            removed.Clear();
            foreach (var tile in _tiles.Values)
            {
                if (tile.State == TileState.Ready || (IsBackingOff(tile) && nowTicks < tile.RetryAtTicks))
                    continue;
                if (Frame - tile.LastVisitedFrame > maxAgeFrames)
                    removed.Add(tile.Id);
            }

            foreach (var id in removed)
                _tiles.Remove(id);
        }
    }
}
