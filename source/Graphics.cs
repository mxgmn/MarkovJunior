// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

static class Graphics
{
    // Loads an image file and converts it to an array of integers (BGRA format)
    // Returns a tuple containing: the pixel data array, width, height, and status code
    public static (int[], int, int, int) LoadBitmap(string filename)
    {
        try
        {
            using var image = Image.Load<Bgra32>(filename);
            int width = image.Width, height = image.Height;
            int[] result = new int[width * height];
            // Efficiently copy pixel data using memory marshalling
            image.CopyPixelDataTo(MemoryMarshal.Cast<int, Bgra32>(result));
            return (result, width, height, 1); // Status code 1 indicates success
        }
        catch (Exception) { return (null, -1, -1, -1); } // Error codes (-1) indicate failure
    }

    // Saves an array of pixel data as a PNG image file
    // Uses unsafe code to directly access memory for better performance
    unsafe public static void SaveBitmap(int[] data, int width, int height, string filename)
    {
        if (width <= 0 || height <= 0 || data.Length != width * height)
        {
            Console.WriteLine($"ERROR: wrong image width * height = {width} * {height}");
            return;
        }
        fixed (int* pData = data) // Pin array in memory to prevent garbage collection from moving it
        {
            using var image = Image.WrapMemory<Bgra32>(pData, width, height);
            image.SaveAsPng(filename);
        }
    }

    // Main rendering function that chooses between 2D bitmap or 3D isometric rendering
    // based on the MZ parameter (MZ=1 means 2D, MZ>1 means 3D)
    public static (int[], int, int) Render(byte[] state, int MX, int MY, int MZ, int[] colors, int pixelsize, int MARGIN) => MZ == 1 ? BitmapRender(state, MX, MY, colors, pixelsize, MARGIN) : IsometricRender(state, MX, MY, MZ, colors, pixelsize, MARGIN);

    // Renders a 2D bitmap based on the state array
    // Each element in state is an index into the colors array
    public static (int[], int, int) BitmapRender(byte[] state, int MX, int MY, int[] colors, int pixelsize, int MARGIN)
    {
        int WIDTH = MARGIN + MX * pixelsize, HEIGHT = MY * pixelsize;
        int TOTALWIDTH = WIDTH, TOTALHEIGHT = HEIGHT;
        //int TOTALWIDTH = 189 + MARGIN, TOTALHEIGHT = 189;
        int[] bitmap = new int[TOTALWIDTH * TOTALHEIGHT];
        for (int i = 0; i < bitmap.Length; i++) bitmap[i] = GUI.BACKGROUND;
        //for (int i = 0; i < bitmap.Length; i++) bitmap[i] = 255 << 24;

        // Calculate offsets to center the rendering
        int DX = (TOTALWIDTH - WIDTH) / 2;
        int DY = (TOTALHEIGHT - HEIGHT) / 2;

        // Iterate through each cell in the state array
        for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
            {
                int c = colors[state[x + y * MX]]; // Get color from the colors array
                // Draw a square of pixelsize×pixelsize for each cell
                for (int dy = 0; dy < pixelsize; dy++) for (int dx = 0; dx < pixelsize; dx++)
                    {
                        int SX = DX + x * pixelsize + dx;
                        int SY = DY + y * pixelsize + dy;
                        // Skip pixels outside the valid area
                        if (SX < 0 || SX >= TOTALWIDTH - MARGIN || SY < 0 || SY >= TOTALHEIGHT) continue;
                        bitmap[MARGIN + SX + SY * TOTALWIDTH] = c;
                    }
            }
        return (bitmap, TOTALWIDTH, TOTALHEIGHT);
    }

    // Cache for sprite objects to avoid recreating them
    static readonly Dictionary<int, Sprite> sprites = new();

    // Renders a 3D isometric view of the state array
    // Much more complex than 2D rendering due to visibility calculation and 3D projection
    public static (int[], int, int) IsometricRender(byte[] state, int MX, int MY, int MZ, int[] colors, int blocksize, int MARGIN)
    {
        // Create lists to hold voxels sorted by their position in the isometric view
        // The sum of x+y+z determines the rendering order (painter's algorithm)
        var voxels = new List<Voxel>[MX + MY + MZ - 2];
        var visibleVoxels = new List<Voxel>[MX + MY + MZ - 2];
        for (int i = 0; i < voxels.Length; i++)
        {
            voxels[i] = new List<Voxel>();
            visibleVoxels[i] = new List<Voxel>();
        }
        bool[] visible = new bool[MX * MY * MZ]; // Tracks which voxels are visible (non-zero)

        // First pass: identify all non-empty voxels and add them to the voxels list
        for (int z = 0; z < MZ; z++) for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
                {
                    int i = x + y * MX + z * MX * MY;
                    byte value = state[i];
                    visible[i] = value != 0;
                    if (value != 0) voxels[x + y + z].Add(new Voxel(colors[value], x, y, z));
                }

        // Used to track which voxels are visible from the isometric view perspective
        // This implements occlusion culling to avoid drawing hidden voxels
        bool[][] hash = AH.Array2D(MX + MY - 1, MX + MY + 2 * MZ - 3, false);

        // Second pass: determine which voxels are actually visible and their edge visibility
        for (int i = voxels.Length - 1; i >= 0; i--) // Process back-to-front
        {
            List<Voxel> voxelsi = voxels[i];
            for (int j = 0; j < voxelsi.Count; j++)
            {
                Voxel s = voxelsi[j];
                // Calculate the 2D position in the isometric projection
                int u = s.x - s.y + MY - 1, v = s.x + s.y - 2 * s.z + 2 * MZ - 2;
                if (!hash[u][v]) // If nothing is already rendered at this position
                {
                    // Check if the three main faces are visible (based on neighbors)
                    bool X = s.x == 0 || !visible[(s.x - 1) + s.y * MX + s.z * MX * MY];
                    bool Y = s.y == 0 || !visible[s.x + (s.y - 1) * MX + s.z * MX * MY];
                    bool Z = s.z == 0 || !visible[s.x + s.y * MX + (s.z - 1) * MX * MY];

                    // Determine which edges need to be drawn based on neighbor visibility
                    s.edges[0] = s.y == MY - 1 || !visible[s.x + (s.y + 1) * MX + s.z * MX * MY];
                    s.edges[1] = s.x == MX - 1 || !visible[s.x + 1 + s.y * MX + s.z * MX * MY];
                    s.edges[2] = X || (s.y != MY - 1 && visible[s.x - 1 + (s.y + 1) * MX + s.z * MX * MY]);
                    s.edges[3] = X || (s.z != MZ - 1 && visible[s.x - 1 + s.y * MX + (s.z + 1) * MX * MY]);
                    s.edges[4] = Y || (s.x != MX - 1 && visible[s.x + 1 + (s.y - 1) * MX + s.z * MX * MY]);
                    s.edges[5] = Y || (s.z != MZ - 1 && visible[s.x + (s.y - 1) * MX + (s.z + 1) * MX * MY]);
                    s.edges[6] = Z || (s.x != MX - 1 && visible[s.x + 1 + s.y * MX + (s.z - 1) * MX * MY]);
                    s.edges[7] = Z || (s.y != MY - 1 && visible[s.x + (s.y + 1) * MX + (s.z - 1) * MX * MY]);

                    visibleVoxels[i].Add(s);
                    hash[u][v] = true; // Mark this position as occupied
                }
            }
        }

        // Calculate dimensions for the final image
        int FITWIDTH = (MX + MY) * blocksize, FITHEIGHT = ((MX + MY) / 2 + MZ) * blocksize;
        int WIDTH = FITWIDTH + 2 * blocksize, HEIGHT = FITHEIGHT + 2 * blocksize;
        //const int WIDTH = 330, HEIGHT = 330;

        // Create the output image array and fill with background color
        int[] screen = new int[(MARGIN + WIDTH) * HEIGHT];
        for (int i = 0; i < screen.Length; i++) screen[i] = GUI.BACKGROUND;

        // Helper function to blit (copy) a sprite to the output image with color tinting
        void Blit(int[] sprite, int SX, int SY, int x, int y, int r, int g, int b)
        {
            for (int dy = 0; dy < SY; dy++) for (int dx = 0; dx < SX; dx++)
                {
                    int grayscale = sprite[dx + dy * SX];
                    if (grayscale < 0) continue; // Skip transparent pixels
                    // Apply color tinting to the grayscale sprite
                    byte R = (byte)((float)r * (float)grayscale / 256.0f);
                    byte G = (byte)((float)g * (float)grayscale / 256.0f);
                    byte B = (byte)((float)b * (float)grayscale / 256.0f);
                    int X = x + dx;
                    int Y = y + dy;
                    // Ensure we're drawing within bounds
                    if (MARGIN + X >= 0 && X < WIDTH && Y >= 0 && Y < HEIGHT) screen[MARGIN + X + Y * (MARGIN + WIDTH)] = Int(R, G, B);
                }
        };

        // Get or create a sprite of the appropriate size
        bool success = sprites.TryGetValue(blocksize, out Sprite sprite);
        if (!success)
        {
            sprite = new Sprite(blocksize);
            sprites.Add(blocksize, sprite);
        }

        // Draw all visible voxels
        for (int i = 0; i < visibleVoxels.Length; i++) foreach (Voxel s in visibleVoxels[i])
            {
                // Calculate isometric position
                int u = blocksize * (s.x - s.y);
                int v = (blocksize * (s.x + s.y) / 2 - blocksize * s.z);
                int positionx = WIDTH / 2 + u - blocksize;
                //int positionx = WIDTH / 2 + u - 5 * blocksize;
                int positiony = (HEIGHT - FITHEIGHT) / 2 + (MZ - 1) * blocksize + v;
                var (r, g, b) = RGB(s.color); // Extract RGB components
                // Draw the cube body
                Blit(sprite.cube, sprite.width, sprite.height, positionx, positiony, r, g, b);
                // Draw any visible edges
                for (int j = 0; j < 8; j++) if (s.edges[j]) Blit(sprite.edges[j], sprite.width, sprite.height, positionx, positiony, r, g, b);
            }

        return (screen, MARGIN + WIDTH, HEIGHT);
    }

    // Helper function to convert RGB bytes to a single integer
    static int Int(byte r, byte g, byte b) => (0xff << 24) + (r << 16) + (g << 8) + b;

    // Helper function to extract RGB components from a color integer
    static (byte, byte, byte) RGB(int i)
    {
        byte r = (byte)((i & 0xff0000) >> 16);
        byte g = (byte)((i & 0xff00) >> 8);
        byte b = (byte)(i & 0xff);
        return (r, g, b);
    }
}

// Represents a pre-rendered sprite for a cube and its edges in isometric view
class Sprite
{
    public int[] cube;       // Grayscale pixels for the cube body
    public int[][] edges;    // Grayscale pixels for the 8 different edge types
    public int width, height;

    // Color constants used in the sprite
    const int c1 = 215, c2 = 143, c3 = 71, black = 0, transparent = -1;

    public Sprite(int size)
    {
        width = 2 * size;
        height = 2 * size - 1;

        // Helper function to create a texture using the provided function f
        int[] texture(Func<int, int, int> f)
        {
            int[] result = new int[width * height];
            for (int j = 0; j < height; j++) for (int i = 0; i < width; i++) result[i + j * width] = f(i - size + 1, size - j - 1);
            return result;
        };

        // Function to determine the color of each pixel in the cube
        // Different shades are used for different faces to create a 3D effect
        int f(int x, int y)
        {
            if (2 * y - x >= 2 * size || 2 * y + x > 2 * size || 2 * y - x < -2 * size || 2 * y + x <= -2 * size) return transparent;
            else if (x > 0 && 2 * y < x) return c3; // Right face (darkest)
            else if (x <= 0 && 2 * y <= -x) return c2; // Left face (medium)
            else return c1; // Top face (lightest)
        };

        // Create the main cube sprite
        cube = texture(f);

        // Create sprites for the 8 different edge types
        // Each one only shows a specific edge of the cube
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

// Represents a single voxel (3D pixel) in the scene
struct Voxel
{
    public int color;       // The color to use for this voxel
    public int x, y, z;     // Position in 3D space
    public bool[] edges;    // Which edges of this voxel should be drawn

    public Voxel(int color, int x, int y, int z)
    {
        this.color = color;
        this.x = x;
        this.y = y;
        this.z = z;
        edges = new bool[8];
    }
}

/*
========== SUMMARY ==========

This code creates a rendering system for both 2D and 3D graphics. Think of it like a digital art studio with two different drawing modes:

1. 2D Mode (BitmapRender): This is like drawing a simple grid of colored squares on graph paper. Each colored square represents a cell in your data.

2. 3D Mode (IsometricRender): This creates a 3D-looking view, similar to building with colored blocks. The code carefully figures out which blocks you can see from your viewpoint and draws only those.

The main components are:

- Loading/Saving Images: Functions to read and write PNG files
- Render: The main function that decides whether to use 2D or 3D rendering
- BitmapRender: Creates a 2D grid of colored squares
- IsometricRender: Creates a 3D-looking view of blocks (voxels)
- Sprite: Defines what the 3D blocks look like with different shading on each face
- Voxel: Represents a single 3D block with position and color

The 3D rendering is especially clever - it uses what artists call the "painter's algorithm" (drawing back-to-front) and only draws blocks that would be visible to the viewer. It even adds edges and shading to make blocks look more 3D.

Think of it like this: if you were building with LEGO blocks, you wouldn't be able to see blocks that are completely hidden inside your model. This code figures out which blocks would be visible in your LEGO creation and only draws those, saving a lot of work.
*/