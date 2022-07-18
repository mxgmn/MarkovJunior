// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

static class Graphics
{
    public static (int[], int, int, int) LoadBitmap(string filename)
    {
        try
        {
            using var image = Image.Load<Bgra32>(filename);
            int width = image.Width, height = image.Height;
            int[] result = new int[width * height];
            image.CopyPixelDataTo(MemoryMarshal.Cast<int, Bgra32>(result));
            return (result, width, height, 1);
        }
        catch (Exception) { return (null, -1, -1, -1); }
    }

    unsafe public static void SaveBitmap(int[] data, int width, int height, string filename)
    {
        if (width <= 0 || height <= 0 || data.Length != width * height)
        {
            Console.WriteLine($"ERROR: wrong image width * height = {width} * {height}");
            return;
        }
        fixed (int* pData = data)
        {
            using var image = Image.WrapMemory<Bgra32>(pData, width, height);
            image.SaveAsPng(filename);
        }
    }

    public static (int[], int, int) Render(byte[] state, int MX, int MY, int MZ, int[] colors, int pixelsize, int MARGIN) => MZ == 1 ? BitmapRender(state, MX, MY, colors, pixelsize, MARGIN) : IsometricRender(state, MX, MY, MZ, colors, pixelsize, MARGIN);

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

    static readonly Dictionary<int, Sprite> sprites = new();
    public static (int[], int, int) IsometricRender(byte[] state, int MX, int MY, int MZ, int[] colors, int blocksize, int MARGIN)
    {
        var voxels = new List<Voxel>[MX + MY + MZ - 2];
        var visibleVoxels = new List<Voxel>[MX + MY + MZ - 2];
        for (int i = 0; i < voxels.Length; i++)
        {
            voxels[i] = new List<Voxel>();
            visibleVoxels[i] = new List<Voxel>();
        }
        bool[] visible = new bool[MX * MY * MZ]; //нужен для быстрой работы с transparent

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

    static int Int(byte r, byte g, byte b) => (0xff << 24) + (r << 16) + (g << 8) + b;
    static (byte, byte, byte) RGB(int i)
    {
        byte r = (byte)((i & 0xff0000) >> 16);
        byte g = (byte)((i & 0xff00) >> 8);
        byte b = (byte)(i & 0xff);
        return (r, g, b);
    }
}

class Sprite
{
    public int[] cube;
    public int[][] edges;
    public int width, height;

    const int c1 = 215, c2 = 143, c3 = 71, black = 0, transparent = -1;

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

struct Voxel
{
    public int color;
    public int x, y, z;
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
