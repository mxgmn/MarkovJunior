// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

class TileNode : WFCNode
{
    List<byte[]> tiledata;       // Stores the actual tile pattern data

    int S, SZ;                   // Tile dimensions (S=width/height, SZ=depth)
    int overlap, overlapz;       // How much tiles overlap when placed

    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        // Load configuration from XML
        periodic = xelem.Get("periodic", false);  // Whether the output should wrap around edges
        /*string*/
        name = xelem.Get<string>("tileset");  // Name of the tileset to use
        string tilesname = xelem.Get("tiles", name);     // Folder containing tile files (default: same as tileset)
        overlap = xelem.Get("overlap", 0);               // Horizontal/vertical overlap between adjacent tiles
        overlapz = xelem.Get("overlapz", 0);             // Depth overlap between adjacent tiles

        // Load the tileset definition XML
        XDocument xdoc;
        string filepath = $"resources/tilesets/{name}.xml";
        try { xdoc = XDocument.Load(filepath, LoadOptions.SetLineInfo); }
        catch (Exception)
        {
            Interpreter.WriteLine($"couldn't open tileset {filepath}");
            return false;
        }
        XElement xroot = xdoc.Root;

        // Check if we should use full 3D symmetry (all 48 cube rotations)
        bool fullSymmetry = xroot.Get("fullSymmetry", false);

        // Load the first tile to determine dimensions
        XElement xfirsttile = xroot.Element("tiles").Element("tile");
        string firstFileName = $"{tilesname}/{xfirsttile.Get<string>("name")}.vox";
        int[] firstData;
        int SY;  // Height of the tile
        (firstData, S, SY, SZ) = VoxHelper.LoadVox($"resources/tilesets/{firstFileName}");
        if (firstData == null)
        {
            Interpreter.WriteLine($"couldn't read {firstFileName}");
            return false;
        }

        // Validate tile dimensions
        if (S != SY)
        {
            Interpreter.WriteLine($"tiles should be square shaped: {S} != {SY}");
            return false;
        }
        if (fullSymmetry && S != SZ)
        {
            Interpreter.WriteLine($"tiles should be cubes for the full symmetry option: {S} != {SZ}");
            return false;
        }

        // Calculate the output grid size based on tile size and overlap
        newgrid = Grid.Load(xelem, (S - overlap) * grid.MX + overlap, (S - overlap) * grid.MY + overlap, (SZ - overlapz) * grid.MZ + overlapz);
        if (newgrid == null) return false;

        tiledata = new List<byte[]>();
        Dictionary<string, bool[]> positions = new();  // Maps tile names to their positions in the tiledata list

        // Helper function to create new tiles with a specified mapping function
        byte[] newtile(Func<int, int, int, byte> f) => AH.FlatArray3D(S, S, SZ, f);

        // Define transformation functions for rotating and reflecting tiles
        byte[] zRotate(byte[] p) => newtile((x, y, z) => p[y + (S - 1 - x) * S + z * S * S]);
        byte[] yRotate(byte[] p) => newtile((x, y, z) => p[z + y * S + (S - 1 - x) * S * S]);
        byte[] xRotate(byte[] p) => newtile((x, y, z) => p[x + z * S + (S - 1 - y) * S * S]);
        byte[] xReflect(byte[] p) => newtile((x, y, z) => p[(S - 1 - x) + y * S + z * S * S]);
        byte[] yReflect(byte[] p) => newtile((x, y, z) => p[x + (S - 1 - y) * S + z * S * S]);
        byte[] zReflect(byte[] p) => newtile((x, y, z) => p[x + y * S + (S - 1 - z) * S * S]);

        // Store all tile variations and their weights
        var namedTileData = new Dictionary<string, List<byte[]>>();
        var tempStationary = new List<double>();

        // Load all tiles from the tileset
        var uniques = new List<int>();  // For tracking unique colors
        var xtiles = xroot.Element("tiles").Elements("tile");
        int ind = 0;
        foreach (XElement xtile in xtiles)
        {
            string tilename = xtile.Get<string>("name");
            double weight = xtile.Get("weight", 1.0);  // How likely this tile is to be used

            // Load tile voxel data
            string filename = $"resources/tilesets/{tilesname}/{tilename}.vox";
            int[] vox = VoxHelper.LoadVox(filename).Item1;
            if (vox == null)
            {
                Interpreter.WriteLine($"couldn't read tile {filename}");
                return false;
            }

            // Convert color indices to byte values
            (byte[] flatTile, int C) = vox.Ords(uniques);
            if (C > newgrid.C)
            {
                Interpreter.WriteLine($"there were more than {newgrid.C} colors in vox files");
                return false;
            }

            // Generate all symmetrical variants of the tile
            List<byte[]> localdata = fullSymmetry ?
                SymmetryHelper.CubeSymmetries(flatTile, zRotate, yRotate, xReflect, AH.Same).ToList() :
                SymmetryHelper.SquareSymmetries(flatTile, zRotate, xReflect, AH.Same).ToList();

            // Track the positions of this tile's variants in the tiledata list
            bool[] position = new bool[128];
            namedTileData.Add(tilename, localdata);
            foreach (byte[] p in localdata)
            {
                tiledata.Add(p);
                tempStationary.Add(weight);
                position[ind] = true;
                ind++;
            }
            positions.Add(tilename, position);
        }

        // Store the total number of tile variants
        P = tiledata.Count;
        Console.WriteLine($"P = {P}");
        weights = tempStationary.ToArray();

        // Set up mapping from input values to allowed tiles
        map = new Dictionary<byte, bool[]>();
        foreach (XElement xrule in xelem.Elements("rule"))
        {
            char input = xrule.Get<char>("in");
            string[] outputs = xrule.Get<string>("out").Split('|');
            bool[] position = new bool[P];
            foreach (string s in outputs)
            {
                bool success = positions.TryGetValue(s, out bool[] array);
                if (!success)
                {
                    Interpreter.WriteLine($"unknown tilename {s} at line {xrule.LineNumber()}");
                    return false;
                }
                for (int p = 0; p < P; p++) if (array[p]) position[p] = true;
            }
            map.Add(grid.values[input], position);
        }
        if (!map.ContainsKey(0)) map.Add(0, AH.Array1D(P, true));  // Default for empty cells

        // Initialize propagator with adjacency rules (which tiles can be placed next to each other)
        bool[][][] tempPropagator = AH.Array3D(6, P, P, false);

        // Helper function to find a tile's index in the tiledata list
        int index(byte[] p)
        {
            for (int i = 0; i < tiledata.Count; i++) if (AH.Same(p, tiledata[i])) return i;
            return -1;
        };

        // Helper functions for processing tile references in neighbor definitions
        static string last(string attribute) => attribute?.Split(' ').Last();
        byte[] tile(string attribute)
        {
            string[] code = attribute.Split(' ');
            string action = code.Length == 2 ? code[0] : "";
            byte[] starttile = namedTileData[last(attribute)][0];
            for (int i = action.Length - 1; i >= 0; i--)
            {
                char sym = action[i];
                if (sym == 'x') starttile = xRotate(starttile);
                else if (sym == 'y') starttile = yRotate(starttile);
                else if (sym == 'z') starttile = zRotate(starttile);
                else
                {
                    Interpreter.WriteLine($"unknown symmetry {sym}");
                    return null;
                }
            }
            return starttile;
        };

        // Get all tilenames for validation
        List<string> tilenames = xtiles.Select(x => x.Get<string>("name")).ToList();
        tilenames.Add(null);

        // Process neighbor definitions from the tileset
        foreach (XElement xneighbor in xroot.Element("neighbors").Elements("neighbor"))
        {
            // Handle full 3D symmetry case
            if (fullSymmetry)
            {
                string left = xneighbor.Get<string>("left"), right = xneighbor.Get<string>("right");
                if (!tilenames.Contains(last(left)) || !tilenames.Contains(last(right)))
                {
                    Interpreter.WriteLine($"unknown tile {last(left)} or {last(right)} at line {xneighbor.LineNumber()}");
                    return false;
                }

                byte[] ltile = tile(left), rtile = tile(right);
                if (ltile == null || rtile == null) return false;

                // Generate all symmetrical variants for x-direction neighbors
                var lsym = SymmetryHelper.SquareSymmetries(ltile, xRotate, yReflect, (p1, p2) => false).ToArray();
                var rsym = SymmetryHelper.SquareSymmetries(rtile, xRotate, yReflect, (p1, p2) => false).ToArray();

                for (int i = 0; i < lsym.Length; i++)
                {
                    tempPropagator[0][index(lsym[i])][index(rsym[i])] = true;
                    tempPropagator[0][index(xReflect(rsym[i]))][index(xReflect(lsym[i]))] = true;
                }

                // Transform neighbors for y-direction
                byte[] dtile = zRotate(ltile);
                byte[] utile = zRotate(rtile);

                var dsym = SymmetryHelper.SquareSymmetries(dtile, yRotate, zReflect, (p1, p2) => false).ToArray();
                var usym = SymmetryHelper.SquareSymmetries(utile, yRotate, zReflect, (p1, p2) => false).ToArray();

                for (int i = 0; i < dsym.Length; i++)
                {
                    tempPropagator[1][index(dsym[i])][index(usym[i])] = true;
                    tempPropagator[1][index(yReflect(usym[i]))][index(yReflect(dsym[i]))] = true;
                }

                // Transform neighbors for z-direction
                byte[] btile = yRotate(ltile);
                byte[] ttile = yRotate(rtile);

                var bsym = SymmetryHelper.SquareSymmetries(btile, zRotate, xReflect, (p1, p2) => false).ToArray();
                var tsym = SymmetryHelper.SquareSymmetries(ttile, zRotate, xReflect, (p1, p2) => false).ToArray();

                for (int i = 0; i < bsym.Length; i++)
                {
                    tempPropagator[4][index(bsym[i])][index(tsym[i])] = true;
                    tempPropagator[4][index(zReflect(tsym[i]))][index(zReflect(bsym[i]))] = true;
                }
            }
            // Handle horizontal (left-right) neighbors for 2D case
            else if (xneighbor.Get<string>("left", null) != null)
            {
                string left = xneighbor.Get<string>("left"), right = xneighbor.Get<string>("right");
                if (!tilenames.Contains(last(left)) || !tilenames.Contains(last(right)))
                {
                    Interpreter.WriteLine($"unknown tile {last(left)} or {last(right)} at line {xneighbor.LineNumber()}");
                    return false;
                }

                byte[] ltile = tile(left), rtile = tile(right);
                if (ltile == null || rtile == null) return false;

                // Add all symmetrical variants for x-direction
                tempPropagator[0][index(ltile)][index(rtile)] = true;
                tempPropagator[0][index(yReflect(ltile))][index(yReflect(rtile))] = true;
                tempPropagator[0][index(xReflect(rtile))][index(xReflect(ltile))] = true;
                tempPropagator[0][index(yReflect(xReflect(rtile)))][index(yReflect(xReflect(ltile)))] = true;

                // Transform to y-direction
                byte[] dtile = zRotate(ltile);
                byte[] utile = zRotate(rtile);

                tempPropagator[1][index(dtile)][index(utile)] = true;
                tempPropagator[1][index(xReflect(dtile))][index(xReflect(utile))] = true;
                tempPropagator[1][index(yReflect(utile))][index(yReflect(dtile))] = true;
                tempPropagator[1][index(xReflect(yReflect(utile)))][index(xReflect(yReflect(dtile)))] = true;
            }
            // Handle vertical (top-bottom) neighbors
            else
            {
                string top = xneighbor.Get<string>("top", null), bottom = xneighbor.Get<string>("bottom", null);
                if (!tilenames.Contains(last(top)) || !tilenames.Contains(last(bottom)))
                {
                    Interpreter.WriteLine($"unknown tile {last(top)} or {last(bottom)} at line {xneighbor.LineNumber()}");
                    return false;
                }

                byte[] ttile = tile(top), btile = tile(bottom);
                if (ttile == null || btile == null) return false;

                var tsym = SymmetryHelper.SquareSymmetries(ttile, zRotate, xReflect, (p1, p2) => false).ToArray();
                var bsym = SymmetryHelper.SquareSymmetries(btile, zRotate, xReflect, (p1, p2) => false).ToArray();

                for (int i = 0; i < tsym.Length; i++) tempPropagator[4][index(bsym[i])][index(tsym[i])] = true;
            }
        }

        // Fill in the opposite directions in the propagator
        for (int p2 = 0; p2 < P; p2++) for (int p1 = 0; p1 < P; p1++)
            {
                tempPropagator[2][p2][p1] = tempPropagator[0][p1][p2];  // -x is opposite of +x
                tempPropagator[3][p2][p1] = tempPropagator[1][p1][p2];  // -y is opposite of +y
                tempPropagator[5][p2][p1] = tempPropagator[4][p1][p2];  // -z is opposite of +z
            }

        // Convert the dense propagator to a sparse representation for efficiency
        List<int>[][] sparsePropagator = new List<int>[6][];
        for (int d = 0; d < 6; d++)
        {
            sparsePropagator[d] = new List<int>[P];
            for (int t = 0; t < P; t++) sparsePropagator[d][t] = new List<int>();
        }

        propagator = new int[6][][];
        for (int d = 0; d < 6; d++)
        {
            propagator[d] = new int[P][];
            for (int p1 = 0; p1 < P; p1++)
            {
                List<int> sp = sparsePropagator[d][p1];
                bool[] tp = tempPropagator[d][p1];

                // Add only the tiles that can be neighbors in this direction
                for (int p2 = 0; p2 < P; p2++) if (tp[p2]) sp.Add(p2);

                // Convert list to array for faster access
                int ST = sp.Count;
                propagator[d][p1] = new int[ST];
                for (int st = 0; st < ST; st++) propagator[d][p1][st] = sp[st];
            }
        }

        // Finish loading using the parent class implementation
        return base.Load(xelem, parentSymmetry, grid);
    }

    // Converts the wave function collapse result to the final output grid
    protected override void UpdateState()
    {
        Random r = new();
        for (int z = 0; z < grid.MZ; z++) for (int y = 0; y < grid.MY; y++) for (int x = 0; x < grid.MX; x++)
                {
                    // Get the wave function at this cell
                    bool[] w = wave.data[x + y * grid.MX + z * grid.MX * grid.MY];

                    // Count votes for each color at each position in the tile
                    int[][] votes = AH.Array2D(S * S * SZ, newgrid.C, 0);

                    // Accumulate votes from all possible tiles that could be placed here
                    for (int t = 0; t < P; t++) if (w[t])
                        {
                            byte[] tile = tiledata[t];
                            for (int dz = 0; dz < SZ; dz++) for (int dy = 0; dy < S; dy++) for (int dx = 0; dx < S; dx++)
                                    {
                                        int di = dx + dy * S + dz * S * S;
                                        votes[di][tile[di]]++;
                                    }
                        }

                    // For each position in the tile, select the color with the most votes
                    for (int dz = 0; dz < SZ; dz++) for (int dy = 0; dy < S; dy++) for (int dx = 0; dx < S; dx++)
                            {
                                int[] v = votes[dx + dy * S + dz * S * S];
                                double max = -1.0;
                                byte argmax = 0xff;

                                // Find color with most votes (with small random tie-breaking)
                                for (byte c = 0; c < v.Length; c++)
                                {
                                    double vote = v[c] + 0.1 * r.NextDouble();
                                    if (vote > max)
                                    {
                                        argmax = c;
                                        max = vote;
                                    }
                                }

                                // Calculate position in output grid, accounting for overlap
                                int sx = x * (S - overlap) + dx;
                                int sy = y * (S - overlap) + dy;
                                int sz = z * (SZ - overlapz) + dz;

                                // Set the final output color
                                newgrid.state[sx + sy * newgrid.MX + sz * newgrid.MX * newgrid.MY] = argmax;
                            }
                }
    }
}

/*
========== SUMMARY ==========

This code implements a tile-based pattern generation system using the Wave Function Collapse (WFC) algorithm. Think of it like creating a jigsaw puzzle where the pieces must fit together according to specific rules.

In simple terms:

1. Tile Management:
   - The code loads 3D "tiles" (small building blocks) from voxel files
   - It can generate rotated and reflected versions of these tiles automatically
   - Each tile has a "weight" that affects how likely it is to appear in the output

2. Adjacency Rules:
   - The system defines which tiles can be placed next to each other
   - These rules ensure that patterns connect seamlessly across tile boundaries
   - Rules can specify left-right, up-down, and front-back relationships

3. Wave Function Collapse:
   - The algorithm starts with all tiles possible at each position
   - It gradually eliminates incompatible tiles based on adjacency rules
   - When multiple tiles remain possible, it makes weighted random choices

4. Output Generation:
   - Once the algorithm completes, each cell has several possible tiles
   - The final output is created by a "voting" system where each possible tile contributes
   - Tiles can overlap to create smoother transitions between patterns

This approach allows creating complex, non-repeating patterns that still follow consistent rules - useful for procedural generation of landscapes, textures, structures, and more. The system is flexible enough to work with both 2D and full 3D patterns.
*/