﻿using System;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using Utils.VRageRIP.Lib.Extensions;
using VRageMath;
using World.GameActors;
using World.GameActors.Tiles;
using World.GameActors.Tiles.Obstacle;
using World.Physics;

namespace World.Atlas.Layers
{
    public class SimpleTileLayer : ITileLayer
    {
        private const int TILESETS_BITS = 12;
        private const int TILESETS_OFFSET = 1 << TILESETS_BITS; // Must be larger than the number of tiles in any tileset and must correspond to the BasicOffset.vert shader
        private const int BACKGROUND_TILE_NUMBER = 6;
        private const int OBSTACLE_TILE_NUMBER = 7;

        private readonly Random m_random;

        private float m_summer; // Local copy of the Atlas' summer
        private Vector3 m_summerCache;

        private int m_tileCount;
        private int[] m_tileTypes;

        public int Width { get; set; }
        public int Height { get; set; }


        public Tile[][] Tiles { get; set; }

        public byte[][] TileStates { get; set; }

        public bool Render { get; set; }

        public LayerType LayerType { get; set; }


        public SimpleTileLayer(LayerType layerType, int width, int height, Random random = null)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException("width", "Tile width has to be positive");
            if (height <= 0)
                throw new ArgumentOutOfRangeException("height", "Tile height has to be positive");
            Contract.EndContractBlock();

            m_random = random ?? new Random();
            m_tileTypes = new int[0];
            LayerType = layerType;
            m_summerCache.Z = m_random.Next();
            Height = height;
            Width = width;
            Tiles = ArrayCreator.CreateJaggedArray<Tile[][]>(width, height);
            TileStates = ArrayCreator.CreateJaggedArray<byte[][]>(width, height);
            Render = true;
        }


        public void UpdateTileStates(float summer, float gradient)
        {
            m_summer = summer;

            const float tileUpdateCountFactor = 0.002f;
            float weatherChangeIntensityFactor = 1.3f;

            bool isWinter = Math.Abs(summer * gradient) < 0.25f;

            if (isWinter) // It is winter -- start adding or removing snow
            {
                weatherChangeIntensityFactor = summer * 4; // It is Oct to Jan, strengthen intensity towards Jan

                if (gradient < 0)
                    weatherChangeIntensityFactor = 1 - weatherChangeIntensityFactor; // It is Jan to Mar, strenghten intensity towards Mar
            }

            int tileUpdateCount = (int)(m_tileCount * weatherChangeIntensityFactor * tileUpdateCountFactor) + 1;

            Debug.WriteLine(summer.ToString() + '\t' + gradient + '\t' + tileUpdateCount);

            for (int i = 0; i < tileUpdateCount; i++)
            {
                int x = m_random.Next(Width);
                int y = m_random.Next(Height);

                if (isWinter && gradient < 0) // It only snows from Oct to Jan
                    TileStates[x][y] = 1; // winter
                else
                    TileStates[x][y] = 0; // summer

                // TODO: more states defined by atlas
            }
        }


        public Tile GetActorAt(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                return new Obstacle(0);
            }
            return Tiles[x][y];
        }

        public Tile GetActorAt(Shape shape)
        {
            Vector2I position = new Vector2I(Vector2.Floor(shape.Position));
            return Tiles[position.X][position.Y];
        }

        public Tile GetActorAt(Vector2I coordinates)
        {
            return GetActorAt(coordinates.X, coordinates.Y);
        }

        public int[] GetRectangle(Vector2I topLeft, Vector2I size)
        {
            Vector2I intBotRight = topLeft + size;
            Rectangle rectangle = new Rectangle(topLeft, intBotRight - topLeft);
            return GetRectangle(rectangle);
        }

        public int[] GetRectangle(Rectangle rectangle)
        {
            if (m_tileTypes.Length < rectangle.Size.Size())
                m_tileTypes = new int[rectangle.Size.Size()];

            // Use cached getter value
            int viewRight = rectangle.Right;

            int left = Math.Max(rectangle.Left, Math.Min(0, rectangle.Left + rectangle.Width));
            int right = Math.Min(viewRight, Math.Max(Tiles.Length, viewRight - rectangle.Width));
            // Rectangle origin is in top-left; it's top is thus our bottom
            int bot = Math.Max(rectangle.Top, Math.Min(0, rectangle.Top + rectangle.Height));
            int top = Math.Min(rectangle.Bottom, Math.Max(Tiles[0].Length, rectangle.Bottom - rectangle.Height));


            // TODO : Move to properties
            int idx = 0;
            int defaultTileOffset = 0;
            if (LayerType == LayerType.Background)
            {
                defaultTileOffset = BACKGROUND_TILE_NUMBER;
            }
            else if (LayerType == LayerType.Obstacle)
            {
                defaultTileOffset = OBSTACLE_TILE_NUMBER;
            }

            // Rows before start of map
            for (int j = rectangle.Top; j < bot; j++)
            {
                for (int i = rectangle.Left; i < viewRight; i++)
                    m_tileTypes[idx++] = GetDefaultTileOffset(i, j, defaultTileOffset);
            }

            // Rows inside of map
            for (var j = bot; j < top; j++)
            {
                // Tiles before start of map
                for (int i = rectangle.Left; i < left; i++)
                    m_tileTypes[idx++] = GetDefaultTileOffset(i, j, defaultTileOffset);

                // Tiles inside of map
                for (var i = left; i < right; i++)
                {
                    var tile = Tiles[i][j];
                    if (tile != null)
                        m_tileTypes[idx++] = tile.TilesetId + TileStates[i][j] * TILESETS_OFFSET;
                    else
                        m_tileTypes[idx++] = 0; // inside map: must be always 0
                }

                // Tiles after end of map
                for (int i = right; i < viewRight; i++)
                    m_tileTypes[idx++] = GetDefaultTileOffset(i, j, defaultTileOffset);
            }

            // Rows after end of map
            for (int j = top; j < rectangle.Bottom; j++)
            {
                for (int i = rectangle.Left; i < viewRight; i++)
                    m_tileTypes[idx++] = GetDefaultTileOffset(i, j, defaultTileOffset);
            }

            return m_tileTypes;
        }

        private int GetDefaultTileOffset(int x, int y, int defaultTileOffset)
        {
            m_summerCache.X = x;
            m_summerCache.Y = y;

            double hash = (Math.Abs(m_summerCache.GetHash()) % (double)int.MaxValue) / int.MaxValue; // Should be uniformly distributed between 0, 1
            const float offset = 0.2f;
            // Scale to (offset, 1-offset)
            hash *= 1 - offset * 2;
            hash += offset;

            if (hash >= m_summer)
                return defaultTileOffset;

            return defaultTileOffset + TILESETS_OFFSET;
        }

        public bool ReplaceWith<T>(GameActorPosition original, T replacement)
        {
            int x = (int)Math.Floor(original.Position.X);
            int y = (int)Math.Floor(original.Position.Y);
            Tile item = GetActorAt(x, y);

            if (item != original.Actor) return false;

            Tiles[x][y] = null;
            Tile tileReplacement = replacement as Tile;

            if (replacement == null)
            {
                m_tileCount--;
                return true;
            }

            if (Tiles[x][y] == null)
                m_tileCount++;

            Tiles[x][y] = tileReplacement;
            return true;
        }

        public bool Add(GameActorPosition gameActorPosition)
        {
            int x = (int)gameActorPosition.Position.X;
            int y = (int)gameActorPosition.Position.Y;

            if (Tiles[x][y] != null)
                return false;

            Tile actor = gameActorPosition.Actor as Tile;
            Tiles[x][y] = actor;

            if (actor != null)
                m_tileCount++;

            return true;
        }

        public void AddInternal(int x, int y, Tile tile)
        {
            Tiles[x][y] = tile;
            m_tileCount++;
        }
    }
}
