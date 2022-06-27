// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Xml.Linq;

class ConvChainNode : Node
{
    int N;
    double temperature;
    double[] weights;

    public byte c0, c1;
    bool[] substrate;
    byte substrateColor;
    int counter, steps;

    public bool[] sample;
    public int SMX, SMY;

    override protected bool Load(XElement xelem, bool[] symmetry, Grid grid)
    {
        if (grid.MZ != 1)
        {
            Interpreter.WriteLine("convchain currently works only for 2d");
            return false;
        }

        string name = xelem.Get<string>("sample");
        string filename = $"resources/samples/{name}.png";
        int[] bitmap;
        (bitmap, SMX, SMY, _) = Graphics.LoadBitmap(filename);
        if (bitmap == null)
        {
            Interpreter.WriteLine($"couldn't load ConvChain sample {filename}");
            return false;
        }
        sample = new bool[bitmap.Length];
        for (int i = 0; i < sample.Length; i++) sample[i] = bitmap[i] == -1;

        N = xelem.Get("n", 3);
        steps = xelem.Get("steps", -1);
        temperature = xelem.Get("temperature", 1.0);
        c0 = grid.values[xelem.Get<char>("black")];
        c1 = grid.values[xelem.Get<char>("white")];
        substrateColor = grid.values[xelem.Get<char>("on")];

        substrate = new bool[grid.state.Length];

        weights = new double[1 << (N * N)];
        for (int y = 0; y < SMY; y++) for (int x = 0; x < SMX; x++)
            {
                bool[] pattern = Helper.Pattern((dx, dy) => sample[(x + dx) % SMX + (y + dy) % SMY * SMX], N);
                var symmetries = SymmetryHelper.SquareSymmetries(pattern, q => Helper.Rotated(q, N), q => Helper.Reflected(q, N), (q1, q2) => false, symmetry);
                foreach (bool[] q in symmetries) weights[q.Index()] += 1;
            }

        for (int k = 0; k < weights.Length; k++) if (weights[k] <= 0) weights[k] = 0.1;
        return true;
    }

    void Toggle(byte[] state, int i) => state[i] = state[i] == c0 ? c1 : c0;
    override public bool Go()
    {
        if (steps > 0 && counter >= steps) return false;

        int MX = grid.MX, MY = grid.MY;
        byte[] state = grid.state;

        if (counter == 0)
        {
            bool anySubstrate = false;
            for (int i = 0; i < substrate.Length; i++) if (state[i] == substrateColor)
                {
                    state[i] = ip.random.Next(2) == 0 ? c0 : c1;
                    substrate[i] = true;
                    anySubstrate = true;
                }
            counter++;
            return anySubstrate;
        }

        for (int k = 0; k < state.Length; k++)
        {
            int r = ip.random.Next(state.Length);
            if (!substrate[r]) continue;

            int x = r % MX, y = r / MX;
            double q = 1;

            for (int sy = y - N + 1; sy <= y + N - 1; sy++) for (int sx = x - N + 1; sx <= x + N - 1; sx++)
                {
                    int ind = 0, difference = 0;
                    for (int dy = 0; dy < N; dy++) for (int dx = 0; dx < N; dx++)
                        {
                            int X = sx + dx;
                            if (X < 0) X += MX;
                            else if (X >= MX) X -= MX;

                            int Y = sy + dy;
                            if (Y < 0) Y += MY;
                            else if (Y >= MY) Y -= MY;

                            bool value = state[X + Y * MX] == c1;
                            int power = 1 << (dy * N + dx);
                            ind += value ? power : 0;
                            if (X == x && Y == y) difference = value ? power : -power;
                        }

                    q *= weights[ind - difference] / weights[ind];
                }

            if (q >= 1) { Toggle(state, r); continue; }
            if (temperature != 1) q = Math.Pow(q, 1.0 / temperature);
            if (q > ip.random.NextDouble()) Toggle(state, r);
        }

        counter++;
        return true;
    }

    override public void Reset()
    {
        for (int i = 0; i < substrate.Length; i++) substrate[i] = false;
        counter = 0;
    }
}
