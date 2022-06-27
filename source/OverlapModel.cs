// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

class OverlapNode : WFCNode
{
    byte[][] patterns;

    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        if (grid.MZ != 1)
        {
            Interpreter.WriteLine("overlapping model currently works only for 2d");
            return false;
        }
        N = xelem.Get("n", 3);

        string symmetryString = xelem.Get<string>("symmetry", null);
        bool[] symmetry = SymmetryHelper.GetSymmetry(true, symmetryString, parentSymmetry);
        if (symmetry == null)
        {
            Interpreter.WriteLine($"unknown symmetry {symmetryString} at line {xelem.LineNumber()}");
            return false;
        }

        bool periodicInput = xelem.Get("periodicInput", true);

        newgrid = Grid.Load(xelem, grid.MX, grid.MY, grid.MZ);
        if (newgrid == null) return false;
        periodic = true;

        name = xelem.Get<string>("sample");
        (int[] bitmap, int SMX, int SMY, _) = Graphics.LoadBitmap($"resources/samples/{name}.png");
        if (bitmap == null)
        {
            Interpreter.WriteLine($"couldn't read sample {name}");
            return false;
        }
        (byte[] sample, int C) = bitmap.Ords();
        if (C > newgrid.C)
        {
            Interpreter.WriteLine($"there were more than {newgrid.C} colors in the sample");
            return false;
        }
        long W = Helper.Power(C, N * N);

        byte[] patternFromIndex(long ind)
        {
            long residue = ind, power = W;
            byte[] result = new byte[N * N];
            for (int i = 0; i < result.Length; i++)
            {
                power /= C;
                int count = 0;
                while (residue >= power)
                {
                    residue -= power;
                    count++;
                }
                result[i] = (byte)count;
            }
            return result;
        };

        Dictionary<long, int> weights = new();
        List<long> ordering = new();

        int ymax = periodicInput ? grid.MY : grid.MY - N + 1;
        int xmax = periodicInput ? grid.MX : grid.MX - N + 1;
        for (int y = 0; y < ymax; y++) for (int x = 0; x < xmax; x++)
            {
                byte[] pattern = Helper.Pattern((dx, dy) => sample[(x + dx) % SMX + ((y + dy) % SMY) * SMX], N);
                var symmetries = SymmetryHelper.SquareSymmetries(pattern, q => Helper.Rotated(q, N), q => Helper.Reflected(q, N), (q1, q2) => false, symmetry);

                foreach (byte[] p in symmetries)
                {
                    long ind = p.Index(C);
                    if (weights.ContainsKey(ind)) weights[ind]++;
                    else
                    {
                        weights.Add(ind, 1);
                        ordering.Add(ind);
                    }
                }
            }

        P = weights.Count;
        Console.WriteLine($"number of patterns P = {P}");

        patterns = new byte[P][];
        base.weights = new double[P];
        int counter = 0;
        foreach (long w in ordering)
        {
            patterns[counter] = patternFromIndex(w);
            base.weights[counter] = weights[w];
            counter++;
        }

        bool agrees(byte[] p1, byte[] p2, int dx, int dy)
        {
            int xmin = dx < 0 ? 0 : dx, xmax = dx < 0 ? dx + N : N, ymin = dy < 0 ? 0 : dy, ymax = dy < 0 ? dy + N : N;
            for (int y = ymin; y < ymax; y++) for (int x = xmin; x < xmax; x++) if (p1[x + N * y] != p2[x - dx + N * (y - dy)]) return false;
            return true;
        };

        propagator = new int[4][][];
        for (int d = 0; d < 4; d++)
        {
            propagator[d] = new int[P][];
            for (int t = 0; t < P; t++)
            {
                List<int> list = new();
                for (int t2 = 0; t2 < P; t2++) if (agrees(patterns[t], patterns[t2], DX[d], DY[d])) list.Add(t2);
                propagator[d][t] = new int[list.Count];
                for (int c = 0; c < list.Count; c++) propagator[d][t][c] = list[c];
            }
        }

        map = new Dictionary<byte, bool[]>();
        foreach (XElement xrule in xelem.Elements("rule"))
        {
            char input = xrule.Get<char>("in");
            byte[] outputs = xrule.Get<string>("out").Split('|').Select(s => newgrid.values[s[0]]).ToArray();
            bool[] position = Enumerable.Range(0, P).Select(t => outputs.Contains(patterns[t][0])).ToArray();
            map.Add(grid.values[input], position);
        }
        if (!map.ContainsKey(0)) map.Add(0, Enumerable.Repeat(true, P).ToArray());
        
        return base.Load(xelem, parentSymmetry, grid);
    }

    protected override void UpdateState()
    {
        int MX = newgrid.MX, MY = newgrid.MY;
        int[][] votes = AH.Array2D(newgrid.state.Length, newgrid.C, 0);
        for (int i = 0; i < wave.data.Length; i++)
        {
            bool[] w = wave.data[i];
            int x = i % MX, y = i / MX;
            for (int p = 0; p < P; p++) if (w[p])
                {
                    byte[] pattern = patterns[p];
                    for (int dy = 0; dy < N; dy++)
                    {
                        int ydy = y + dy;
                        if (ydy >= MY) ydy -= MY;
                        for (int dx = 0; dx < N; dx++)
                        {
                            int xdx = x + dx;
                            if (xdx >= MX) xdx -= MX;
                            byte value = pattern[dx + dy * N];
                            votes[xdx + ydy * MX][value]++;
                        }
                    }
                }
        }

        Random r = new();
        for (int i = 0; i < votes.Length; i++)
        {
            double max = -1.0;
            byte argmax = 0xff;
            int[] v = votes[i];
            for (byte c = 0; c < v.Length; c++)
            {
                double value = v[c] + 0.1 * r.NextDouble();
                if (value > max)
                {
                    argmax = c;
                    max = value;
                }
            }
            newgrid.state[i] = argmax;
        }
    }
}
