// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

class TileNode : WFCNode
{
    List<byte[]> tiledata;

    int S, SZ;
    int overlap, overlapz;

    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        periodic = xelem.Get("periodic", false);
        /*string*/ name = xelem.Get<string>("tileset");
        string tilesname = xelem.Get("tiles", name);
        overlap = xelem.Get("overlap", 0);
        overlapz = xelem.Get("overlapz", 0);

        XDocument xdoc;
        string filepath = $"resources/tilesets/{name}.xml";
        try { xdoc = XDocument.Load(filepath, LoadOptions.SetLineInfo); }
        catch (Exception)
        {
            Interpreter.WriteLine($"couldn't open tileset {filepath}");
            return false;
        }
        XElement xroot = xdoc.Root;

        bool fullSymmetry = xroot.Get("fullSymmetry", false);
        XElement xfirsttile = xroot.Element("tiles").Element("tile");
        string firstFileName = $"{tilesname}/{xfirsttile.Get<string>("name")}.vox";
        int[] firstData;
        int SY;
        (firstData, S, SY, SZ) = VoxHelper.LoadVox($"resources/tilesets/{firstFileName}");
        if (firstData == null)
        {
            Interpreter.WriteLine($"couldn't read {firstFileName}");
            return false;
        }
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

        newgrid = Grid.Load(xelem, (S - overlap) * grid.MX + overlap, (S - overlap) * grid.MY + overlap, (SZ - overlapz) * grid.MZ + overlapz);
        if (newgrid == null) return false;

        tiledata = new List<byte[]>();
        Dictionary<string, bool[]> positions = new();
        byte[] newtile(Func<int, int, int, byte> f) => AH.FlatArray3D(S, S, SZ, f);

        byte[] zRotate(byte[] p) => newtile((x, y, z) => p[y + (S - 1 - x) * S + z * S * S]);
        byte[] yRotate(byte[] p) => newtile((x, y, z) => p[z + y * S + (S - 1 - x) * S * S]);
        byte[] xRotate(byte[] p) => newtile((x, y, z) => p[x + z * S + (S - 1 - y) * S * S]);
        byte[] xReflect(byte[] p) => newtile((x, y, z) => p[(S - 1 - x) + y * S + z * S * S]);
        byte[] yReflect(byte[] p) => newtile((x, y, z) => p[x + (S - 1 - y) * S + z * S * S]);
        byte[] zReflect(byte[] p) => newtile((x, y, z) => p[x + y * S + (S - 1 - z) * S * S]);

        var namedTileData = new Dictionary<string, List<byte[]>>();
        var tempStationary = new List<double>();

        var uniques = new List<int>();
        var xtiles = xroot.Element("tiles").Elements("tile");
        int ind = 0;
        foreach (XElement xtile in xtiles)
        {
            string tilename = xtile.Get<string>("name");
            double weight = xtile.Get("weight", 1.0);

            string filename = $"resources/tilesets/{tilesname}/{tilename}.vox";
            int[] vox = VoxHelper.LoadVox(filename).Item1;
            if (vox == null)
            {
                Interpreter.WriteLine($"couldn't read tile {filename}");
                return false;
            }
            (byte[] flatTile, int C) = vox.Ords(uniques);
            if (C > newgrid.C)
            {
                Interpreter.WriteLine($"there were more than {newgrid.C} colors in vox files");
                return false;
            }

            List<byte[]> localdata = fullSymmetry ? SymmetryHelper.CubeSymmetries(flatTile, zRotate, yRotate, xReflect, AH.Same).ToList()
                : SymmetryHelper.SquareSymmetries(flatTile, zRotate, xReflect, AH.Same).ToList();

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

        P = tiledata.Count;
        Console.WriteLine($"P = {P}");
        weights = tempStationary.ToArray();

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
        if (!map.ContainsKey(0)) map.Add(0, AH.Array1D(P, true));

        bool[][][] tempPropagator = AH.Array3D(6, P, P, false);

        int index(byte[] p)
        {
            for (int i = 0; i < tiledata.Count; i++) if (AH.Same(p, tiledata[i])) return i;
            return -1;
        };

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

        List<string> tilenames = xtiles.Select(x => x.Get<string>("name")).ToList();
        tilenames.Add(null);

        foreach (XElement xneighbor in xroot.Element("neighbors").Elements("neighbor"))
        {
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

                var lsym = SymmetryHelper.SquareSymmetries(ltile, xRotate, yReflect, (p1, p2) => false).ToArray();
                var rsym = SymmetryHelper.SquareSymmetries(rtile, xRotate, yReflect, (p1, p2) => false).ToArray();

                for (int i = 0; i < lsym.Length; i++)
                {
                    tempPropagator[0][index(lsym[i])][index(rsym[i])] = true;
                    tempPropagator[0][index(xReflect(rsym[i]))][index(xReflect(lsym[i]))] = true;
                }

                byte[] dtile = zRotate(ltile);
                byte[] utile = zRotate(rtile);

                var dsym = SymmetryHelper.SquareSymmetries(dtile, yRotate, zReflect, (p1, p2) => false).ToArray();
                var usym = SymmetryHelper.SquareSymmetries(utile, yRotate, zReflect, (p1, p2) => false).ToArray();

                for (int i = 0; i < dsym.Length; i++)
                {
                    tempPropagator[1][index(dsym[i])][index(usym[i])] = true;
                    tempPropagator[1][index(yReflect(usym[i]))][index(yReflect(dsym[i]))] = true;
                }

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

                tempPropagator[0][index(ltile)][index(rtile)] = true;
                tempPropagator[0][index(yReflect(ltile))][index(yReflect(rtile))] = true;
                tempPropagator[0][index(xReflect(rtile))][index(xReflect(ltile))] = true;
                tempPropagator[0][index(yReflect(xReflect(rtile)))][index(yReflect(xReflect(ltile)))] = true;

                byte[] dtile = zRotate(ltile);
                byte[] utile = zRotate(rtile);

                tempPropagator[1][index(dtile)][index(utile)] = true;
                tempPropagator[1][index(xReflect(dtile))][index(xReflect(utile))] = true;
                tempPropagator[1][index(yReflect(utile))][index(yReflect(dtile))] = true;
                tempPropagator[1][index(xReflect(yReflect(utile)))][index(xReflect(yReflect(dtile)))] = true;
            }
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

        for (int p2 = 0; p2 < P; p2++) for (int p1 = 0; p1 < P; p1++)
            {
                tempPropagator[2][p2][p1] = tempPropagator[0][p1][p2];
                tempPropagator[3][p2][p1] = tempPropagator[1][p1][p2];
                tempPropagator[5][p2][p1] = tempPropagator[4][p1][p2];
            }

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

                for (int p2 = 0; p2 < P; p2++) if (tp[p2]) sp.Add(p2);

                int ST = sp.Count;
                propagator[d][p1] = new int[ST];
                for (int st = 0; st < ST; st++) propagator[d][p1][st] = sp[st];
            }
        }

        return base.Load(xelem, parentSymmetry, grid);
    }

    protected override void UpdateState()
    {
        Random r = new();
        for (int z = 0; z < grid.MZ; z++) for (int y = 0; y < grid.MY; y++) for (int x = 0; x < grid.MX; x++)
                {
                    bool[] w = wave.data[x + y * grid.MX + z * grid.MX * grid.MY];
                    int[][] votes = AH.Array2D(S * S * SZ, newgrid.C, 0);

                    for (int t = 0; t < P; t++) if (w[t])
                        {
                            byte[] tile = tiledata[t];
                            for (int dz = 0; dz < SZ; dz++) for (int dy = 0; dy < S; dy++) for (int dx = 0; dx < S; dx++)
                                    {
                                        int di = dx + dy * S + dz * S * S;
                                        votes[di][tile[di]]++;
                                    }
                        }

                    for (int dz = 0; dz < SZ; dz++) for (int dy = 0; dy < S; dy++) for (int dx = 0; dx < S; dx++)
                            {
                                int[] v = votes[dx + dy * S + dz * S * S];
                                double max = -1.0;
                                //int max = -1;
                                byte argmax = 0xff;
                                for (byte c = 0; c < v.Length; c++)
                                {
                                    //int vote = v[c];
                                    double vote = v[c] + 0.1 * r.NextDouble();
                                    if (vote > max)
                                    {
                                        argmax = c;
                                        max = vote;
                                    }
                                }
                                int sx = x * (S - overlap) + dx;
                                int sy = y * (S - overlap) + dy;
                                int sz = z * (SZ - overlapz) + dz;
                                newgrid.state[sx + sy * newgrid.MX + sz * newgrid.MX * newgrid.MY] = argmax;
                            }
                }
    }
}
