// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;

/// <summary>
/// Helper functions for loading, rendering and saving images.
/// </summary>
static class Graphics
{
    /// <summary>
    /// Loads a bitmap from a file, as a flat array of packed 32-bit ints. Call
    /// <see cref="Graphics.RGB(int)">Graphics.RGB</see> to unpack the
    /// (r, g, b) values.
    /// </summary>
    /// <returns>A tuple of (bitmap, width, height, 1), or <c>(null, -1, -1, -1)</c> if the loading fails.</returns>
    public static (int[], int, int, int) LoadBitmap(string filename)
    {
        try
        {
            Bitmap bitmap = new(filename);
            int width = bitmap.Width, height = bitmap.Height;
            var bits = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int[] result = new int[bitmap.Width * bitmap.Height];
            System.Runtime.InteropServices.Marshal.Copy(bits.Scan0, result, 0, result.Length);
            bitmap.UnlockBits(bits);
            bitmap.Dispose();
            return (result, width, height, 1);
        }
        catch (Exception) { return (null, -1, -1, -1); }
    }

    /// <summary>
    /// Saves a bitmap as a file. The bitmap data must be packed as 32-bit
    /// ints; call <see cref="Graphics.Int(byte, byte, byte)">Graphics.Int</see>
    /// to pack the (r, g, b) values.
    /// </summary>
    public static void SaveBitmap(int[] data, int width, int height, string filename)
    {
        Bitmap result = new(width, height);
        var bits = result.LockBits(new Rectangle(0, 0, result.Width, result.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        System.Runtime.InteropServices.Marshal.Copy(data, 0, bits.Scan0, data.Length);
        result.UnlockBits(bits);
        result.Save(filename);
    }

    /// <summary>
    /// Renders a grid state to an image. The rendering will be orthogonal for
    /// a 2D grid, or isometric for a 3D grid.
    /// </summary>
    /// <returns>A tuple of (bitmap, width, height).</returns>
    public static (int[], int, int) Render(byte[] state, int MX, int MY, int MZ, int[] colors, int pixelsize, int MARGIN) => MZ == 1 ? BitmapRender(state, MX, MY, colors, pixelsize, MARGIN) : IsometricRender(state, MX, MY, MZ, colors, pixelsize, MARGIN);

    /// <summary>
    /// Renders a 2D grid state to an image.
    /// </summary>
    /// <returns><inheritdoc cref="Graphics.Render(byte[], int, int, int, int[], int, int)" path="/returns"/></returns>
    public static (int[], int, int) BitmapRender(byte[] state, int MX, int MY, int[] colors, int pixelsize, int MARGIN)
    {
        int WIDTH = MARGIN + MX * pixelsize, HEIGHT = MY * pixelsize;
        int TOTALWIDTH = WIDTH, TOTALHEIGHT = HEIGHT;
        //int TOTALWIDTH = 189 + MARGIN, TOTALHEIGHT = 189;
        int[] bitmap = new int[TOTALWIDTH * TOTALHEIGHT];
        for (int i = 0; i < bitmap.Length; i++) bitmap[i] = GUI.BACKGROUND;
        //for (int i = 0; i < bitmap.Length; i++) bitmap[i] = 255 << 24;

        int DX = (TOTALWIDTH - WIDTH) / 2;
        int DY = (TOTALHEIGHT - HEIGHT) / 2;

        for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
            {
                int c = colors[state[x + y * MX]];
                for (int dy = 0; dy < pixelsize; dy++) for (int dx = 0; dx < pixelsize; dx++)
                    {
                        int SX = DX + x * pixelsize + dx;
                        int SY = DY + y * pixelsize + dy;
                        if (SX < 0 || SX >= TOTALWIDTH - MARGIN || SY < 0 || SY >= TOTALHEIGHT) continue;
                        bitmap[MARGIN + SX + SY * TOTALWIDTH] = c;
                    }
            }
        return (bitmap, TOTALWIDTH, TOTALHEIGHT);
    }

    /// <summary>Cache for sprites, keyed by block size.</summary>
    static readonly Dictionary<int, Sprite> sprites = new();
    
    /// <summary>
    /// Renders a 3D grid state to an image, using an isometric projection.
    /// </summary>
    /// <returns><inheritdoc cref="Graphics.Render(byte[], int, int, int, int[], int, int)" path="/returns"/></returns>
    public static (int[], int, int) IsometricRender(byte[] state, int MX, int MY, int MZ, int[] colors, int blocksize, int MARGIN)
    {
        var voxels = new List<Voxel>[MX + MY + MZ - 2];
        var visibleVoxels = new List<Voxel>[MX + MY + MZ - 2];
        for (int i = 0; i < voxels.Length; i++)
        {
            voxels[i] = new List<Voxel>();
            visibleVoxels[i] = new List<Voxel>();
        }
        // нужен для быстрой работы с transparent
        // needed for fast work with transparent
        bool[] visible = new bool[MX * MY * MZ];

        for (int z = 0; z < MZ; z++) for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
                {
                    int i = x + y * MX + z * MX * MY;
                    byte value = state[i];
                    visible[i] = value != 0;
                    if (value != 0) voxels[x + y + z].Add(new Voxel(colors[value], x, y, z));
                }

        bool[][] hash = AH.Array2D(MX + MY - 1, MX + MY + 2 * MZ - 3, false);
        for (int i = voxels.Length - 1; i >= 0; i--)
        {
            List<Voxel> voxelsi = voxels[i];
            for (int j = 0; j < voxelsi.Count; j++)
            {
                Voxel s = voxelsi[j];
                int u = s.x - s.y + MY - 1, v = s.x + s.y - 2 * s.z + 2 * MZ - 2;
                if (!hash[u][v])
                {
                    bool X = s.x == 0 || !visible[(s.x - 1) + s.y * MX + s.z * MX * MY];
                    bool Y = s.y == 0 || !visible[s.x + (s.y - 1) * MX + s.z * MX * MY];
                    bool Z = s.z == 0 || !visible[s.x + s.y * MX + (s.z - 1) * MX * MY];

                    s.edges[0] = s.y == MY - 1 || !visible[s.x + (s.y + 1) * MX + s.z * MX * MY];
                    s.edges[1] = s.x == MX - 1 || !visible[s.x + 1 + s.y * MX + s.z * MX * MY];
                    s.edges[2] = X || (s.y != MY - 1 && visible[s.x - 1 + (s.y + 1) * MX + s.z * MX * MY]);
                    s.edges[3] = X || (s.z != MZ - 1 && visible[s.x - 1 + s.y * MX + (s.z + 1) * MX * MY]);
                    s.edges[4] = Y || (s.x != MX - 1 && visible[s.x + 1 + (s.y - 1) * MX + s.z * MX * MY]);
                    s.edges[5] = Y || (s.z != MZ - 1 && visible[s.x + (s.y - 1) * MX + (s.z + 1) * MX * MY]);
                    s.edges[6] = Z || (s.x != MX - 1 && visible[s.x + 1 + s.y * MX + (s.z - 1) * MX * MY]);
                    s.edges[7] = Z || (s.y != MY - 1 && visible[s.x + (s.y + 1) * MX + (s.z - 1) * MX * MY]);

                    visibleVoxels[i].Add(s);
                    hash[u][v] = true;
                }
            }
        }

        int FITWIDTH = (MX + MY) * blocksize, FITHEIGHT = ((MX + MY) / 2 + MZ) * blocksize;
        int WIDTH = FITWIDTH + 2 * blocksize, HEIGHT = FITHEIGHT + 2 * blocksize;
        //const int WIDTH = 330, HEIGHT = 330;

        int[] screen = new int[(MARGIN + WIDTH) * HEIGHT];
        for (int i = 0; i < screen.Length; i++) screen[i] = GUI.BACKGROUND;

        void Blit(int[] sprite, int SX, int SY, int x, int y, int r, int g, int b)
        {
            for (int dy = 0; dy < SY; dy++) for (int dx = 0; dx < SX; dx++)
                {
                    int grayscale = sprite[dx + dy * SX];
                    if (grayscale < 0) continue;
                    byte R = (byte)((float)r * (float)grayscale / 256.0f);
                    byte G = (byte)((float)g * (float)grayscale / 256.0f);
                    byte B = (byte)((float)b * (float)grayscale / 256.0f);
                    int X = x + dx;
                    int Y = y + dy;
                    if (MARGIN + X >= 0 && X < WIDTH && Y >= 0 && Y < HEIGHT) screen[MARGIN + X + Y * (MARGIN + WIDTH)] = Int(R, G, B);
                }
        };

        bool success = sprites.TryGetValue(blocksize, out Sprite sprite);
        if (!success)
        {
            sprite = new Sprite(blocksize);
            sprites.Add(blocksize, sprite);
        }       

        for (int i = 0; i < visibleVoxels.Length; i++) foreach (Voxel s in visibleVoxels[i])
            {
                int u = blocksize * (s.x - s.y);
                int v = (blocksize * (s.x + s.y) / 2 - blocksize * s.z);
                int positionx = WIDTH / 2 + u - blocksize;
                //int positionx = WIDTH / 2 + u - 5 * blocksize;
                int positiony = (HEIGHT - FITHEIGHT) / 2 + (MZ - 1) * blocksize + v;
                var (r, g, b) = RGB(s.color);
                Blit(sprite.cube, sprite.width, sprite.height, positionx, positiony, r, g, b);
                for (int j = 0; j < 8; j++) if (s.edges[j]) Blit(sprite.edges[j], sprite.width, sprite.height, positionx, positiony, r, g, b);
            }

        return (screen, MARGIN + WIDTH, HEIGHT);
    }

    /// <summary>Packs the given (r, g, b) values into a 32-bit int.</summary>
    static int Int(byte r, byte g, byte b) => (0xff << 24) + (r << 16) + (g << 8) + b;
    
    /// <summary>Unpacks the (r, g, b) values from a given 32-bit int.</summary>
    static (byte, byte, byte) RGB(int i)
    {
        byte r = (byte)((i & 0xff0000) >> 16);
        byte g = (byte)((i & 0xff00) >> 8);
        byte b = (byte)(i & 0xff);
        return (r, g, b);
    }
}

/// <summary>
/// Creates grayscale sprites of a cube and its eight possibly-visible edges.
/// The sprites are flat arrays of grayscale values in the range 0..256, with
/// transparent pixels represented by -1.
/// </summary>
class Sprite
{
    /// <summary>The sprite for the cube, as a flat array of greyscale values.</summary>
    public int[] cube;
    
    /// <summary>The sprites for the possibly-visible edges of the cube, each as a flat array of greyscale values.</summary>
    public int[][] edges;
    
    /// <summary>The width of each sprite.</summary>
    public int width;
    
    /// <summary>The height of each sprite.</summary>
    public int height;

    /// <summary>The lightness of the top face of the cube, also used for highlighted edges.</summary>
    const int c1 = 215;
    
    /// <summary>The lightness of the left face of the cube.</summary>
    const int c2 = 143;
    
    /// <summary>The lightness of the right face of the cube.</summary>
    const int c3 = 71;
    
    /// <summary>The lightness of a shaded cube edge.</summary>
    const int black = 0;
    
    /// <summary>Sentinel value representing a transparent pixel.</summary>
    const int transparent = -1;

    public Sprite(int size)
    {
        width = 2 * size;
        height = 2 * size - 1;

        int[] texture(Func<int, int, int> f)
        {
            int[] result = new int[width * height];
            for (int j = 0; j < height; j++) for (int i = 0; i < width; i++) result[i + j * width] = f(i - size + 1, size - j - 1);
            return result;
        };

        int f(int x, int y)
        {
            if (2 * y - x >= 2 * size || 2 * y + x > 2 * size || 2 * y - x < -2 * size || 2 * y + x <= -2 * size) return transparent;
            else if (x > 0 && 2 * y < x) return c3;
            else if (x <= 0 && 2 * y <= -x) return c2;
            else return c1;
        };

        cube = texture(f);
        
        // edges are numbered as follows:
        //      35
        //    33  55
        //  33      55
        // 3          5
        // 2          4
        // 2    10    4
        // 2    10    4
        // 2    10    4
        // 77   10   66
        //   77 10 66
        //     7766
        edges = new int[8][];
        edges[0] = texture((x, y) => x == 1 && y <= 0 ? c1 : transparent);
        edges[1] = texture((x, y) => x == 0 && y <= 0 ? c1 : transparent);
        edges[2] = texture((x, y) => x == 1 - size && 2 * y < size && 2 * y >= -size ? black : transparent);
        edges[3] = texture((x, y) => x <= 0 && y == x / 2 + size - 1 ? black : transparent);
        edges[4] = texture((x, y) => x == size && 2 * y < size && 2 * y >= -size ? black : transparent);
        edges[5] = texture((x, y) => x > 0 && y == -(x + 1) / 2 + size ? black : transparent);
        edges[6] = texture((x, y) => x > 0 && y == (x + 1) / 2 - size ? black : transparent);
        edges[7] = texture((x, y) => x <= 0 && y == -x / 2 - size + 1 ? black : transparent);
    }
}

/// <summary>
/// Helper data structure for <see cref="Graphics.IsometricRender(byte[], int, int, int, int[], int, int)">rendering 3D grids</see>.
/// </summary>
struct Voxel
{
    /// <summary>The voxel's color, as a packed 32-bit int.</summary>
    public int color;
    
    /// <summary>The x coordinate of this voxel in the grid.</summary>
    public int x;
    
    /// <summary>The y coordinate of this voxel in the grid.</summary>
    public int y;
    
    /// <summary>The y coordinate of this voxel in the grid.</summary>
    public int z;
    
    /// <summary>Array of flags indicating which edges of this voxel should be drawn.</summary>
    public bool[] edges;

    public Voxel(int color, int x, int y, int z)
    {
        this.color = color;
        this.x = x;
        this.y = y;
        this.z = z;
        edges = new bool[8];
    }
}
