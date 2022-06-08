// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Xml.Linq;
using System.Collections.Generic;

abstract class WFCNode : Branch
{
    protected Wave wave;
    protected int[][][] propagator;
    protected int P, N = 1;

    (int, int)[] stack;
    int stacksize;

    protected double[] weights;
    double[] weightLogWeights;
    double sumOfWeights, sumOfWeightLogWeights, startingEntropy;

    protected Grid newgrid;
    Wave startwave;

    protected Dictionary<byte, bool[]> map;
    protected bool periodic, shannon;

    double[] distribution;
    int tries;

    public string name;

    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        shannon = xelem.Get("shannon", false);
        tries = xelem.Get("tries", 1000);

        wave = new Wave(grid.state.Length, P, propagator.Length, shannon);
        startwave = new Wave(grid.state.Length, P, propagator.Length, shannon);
        stack = new (int, int)[wave.data.Length * P];

        sumOfWeights = sumOfWeightLogWeights = startingEntropy = 0;

        if (shannon)
        {
            weightLogWeights = new double[P];

            for (int t = 0; t < P; t++)
            {
                weightLogWeights[t] = weights[t] * Math.Log(weights[t]);
                sumOfWeights += weights[t];
                sumOfWeightLogWeights += weightLogWeights[t];
            }

            startingEntropy = Math.Log(sumOfWeights) - sumOfWeightLogWeights / sumOfWeights;
        }

        distribution = new double[P];
        return base.Load(xelem, parentSymmetry, newgrid);
    }

    override public void Reset()
    {
        base.Reset();
        n = -1;
        firstgo = true;
    }

    bool firstgo = true;
    Random random;
    public override bool Go()
    {
        if (n >= 0) return base.Go();

        if (firstgo)
        {
            wave.Init(propagator, sumOfWeights, sumOfWeightLogWeights, startingEntropy, shannon);

            for (int i = 0; i < wave.data.Length; i++)
            {
                byte value = grid.state[i];
                if (map.ContainsKey(value))
                {
                    bool[] startWave = map[value];
                    for (int t = 0; t < P; t++) if (!startWave[t]) Ban(i, t);
                }
            }

            bool firstSuccess = Propagate();
            if (!firstSuccess)
            {
                Console.WriteLine("initial conditions are contradictive");
                return false;
            }
            startwave.CopyFrom(wave, propagator.Length, shannon);
            int? goodseed = GoodSeed();
            if (goodseed == null) return false;

            random = new Random((int)goodseed);
            stacksize = 0;
            wave.CopyFrom(startwave, propagator.Length, shannon);
            firstgo = false;

            newgrid.Clear();
            ip.grid = newgrid;
            return true;
        }
        else
        {
            int node = NextUnobservedNode(random);
            if (node >= 0)
            {
                Observe(node, random);
                Propagate();
            }
            else n++;

            if (n >= 0 || ip.gif) UpdateState();
            return true;
        }
    }

    int? GoodSeed()
    {
        for (int k = 0; k < tries; k++)
        {
            int observationsSoFar = 0;
            int seed = ip.random.Next();
            random = new Random(seed);
            stacksize = 0;
            wave.CopyFrom(startwave, propagator.Length, shannon);

            while (true)
            {
                int node = NextUnobservedNode(random);
                if (node >= 0)
                {
                    Observe(node, random);
                    observationsSoFar++;
                    bool success = Propagate();
                    if (!success)
                    {
                        Console.WriteLine($"CONTRADICTION on try {k} with {observationsSoFar} observations");
                        break;
                    }
                }
                else
                {
                    Console.WriteLine($"wfc found a good seed {seed} on try {k} with {observationsSoFar} observations");
                    return seed;
                }
            }
        }

        Console.WriteLine($"wfc failed to find a good seed in {tries} tries");
        return null;
    }

    int NextUnobservedNode(Random random)
    {
        int MX = grid.MX, MY = grid.MY, MZ = grid.MZ;
        double min = 1E+4;
        int argmin = -1;
        for (int z = 0; z < MZ; z++) for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
                {
                    if (!periodic && (x + N > MX || y + N > MY || z + 1 > MZ)) continue;
                    int i = x + y * MX + z * MX * MY;
                    int remainingValues = wave.sumsOfOnes[i];
                    double entropy = shannon ? wave.entropies[i] : remainingValues;
                    if (remainingValues > 1 && entropy <= min)
                    {
                        double noise = 1E-6 * random.NextDouble();
                        if (entropy + noise < min)
                        {
                            min = entropy + noise;
                            argmin = i;
                        }
                    }
                }
        return argmin;
    }

    void Observe(int node, Random random)
    {
        bool[] w = wave.data[node];
        for (int t = 0; t < P; t++) distribution[t] = w[t] ? weights[t] : 0.0;
        int r = distribution.Random(random.NextDouble());
        for (int t = 0; t < P; t++) if (w[t] != (t == r)) Ban(node, t);
    }

    bool Propagate()
    {
        int MX = grid.MX, MY = grid.MY, MZ = grid.MZ;

        while (stacksize > 0)
        {
            (int i1, int p1) = stack[stacksize - 1];
            stacksize--;

            int x1 = i1 % MX, y1 = (i1 % (MX * MY)) / MX, z1 = i1 / (MX * MY);

            for (int d = 0; d < propagator.Length; d++)
            {
                int dx = DX[d], dy = DY[d], dz = DZ[d];
                int x2 = x1 + dx, y2 = y1 + dy, z2 = z1 + dz;
                if (!periodic && (x2 < 0 || y2 < 0 || z2 < 0 || x2 + N > MX || y2 + N > MY || z2 + 1 > MZ)) continue;

                if (x2 < 0) x2 += MX;
                else if (x2 >= MX) x2 -= MX;
                if (y2 < 0) y2 += MY;
                else if (y2 >= MY) y2 -= MY;
                if (z2 < 0) z2 += MZ;
                else if (z2 >= MZ) z2 -= MZ;

                int i2 = x2 + y2 * MX + z2 * MX * MY;
                int[] p = propagator[d][p1];
                int[][] compat = wave.compatible[i2];

                for (int l = 0; l < p.Length; l++)
                {
                    int t2 = p[l];
                    int[] comp = compat[t2];

                    comp[d]--;
                    if (comp[d] == 0) Ban(i2, t2);
                }
            }
        }

        return wave.sumsOfOnes[0] > 0;
    }

    void Ban(int i, int t)
    {
        wave.data[i][t] = false;

        int[] comp = wave.compatible[i][t];
        for (int d = 0; d < propagator.Length; d++) comp[d] = 0;
        stack[stacksize] = (i, t);
        stacksize++;

        wave.sumsOfOnes[i] -= 1;
        if (shannon)
        {
            double sum = wave.sumsOfWeights[i];
            wave.entropies[i] += wave.sumsOfWeightLogWeights[i] / sum - Math.Log(sum);

            wave.sumsOfWeights[i] -= weights[t];
            wave.sumsOfWeightLogWeights[i] -= weightLogWeights[t];

            sum = wave.sumsOfWeights[i];
            wave.entropies[i] -= wave.sumsOfWeightLogWeights[i] / sum - Math.Log(sum);
        }
    }

    protected abstract void UpdateState();

    protected static int[] DX = { 1, 0, -1, 0, 0, 0 };
    protected static int[] DY = { 0, 1, 0, -1, 0, 0 };
    protected static int[] DZ = { 0, 0, 0, 0, 1, -1 };
}

class Wave
{
    public bool[][] data;
    public int[][][] compatible;

    public int[] sumsOfOnes;
    public double[] sumsOfWeights, sumsOfWeightLogWeights, entropies;

    public Wave(int length, int P, int D, bool shannon)
    {
        data = AH.Array2D(length, P, true);
        compatible = AH.Array3D(length, P, D, -1);
        sumsOfOnes = new int[length];

        if (shannon)
        {
            sumsOfWeights = new double[length];
            sumsOfWeightLogWeights = new double[length];
            entropies = new double[length];
        }
    }

    public void Init(int[][][] propagator, double sumOfWeights, double sumOfWeightLogWeights, double startingEntropy, bool shannon)
    {
        int P = data[0].Length;
        for (int i = 0; i < data.Length; i++)
        {
            for (int p = 0; p < P; p++)
            {
                data[i][p] = true;
                for (int d = 0; d < propagator.Length; d++) compatible[i][p][d] = propagator[opposite[d]][p].Length;
            }

            sumsOfOnes[i] = P;
            if (shannon)
            {
                sumsOfWeights[i] = sumOfWeights;
                sumsOfWeightLogWeights[i] = sumOfWeightLogWeights;
                entropies[i] = startingEntropy;
            }
        }
    }

    public void CopyFrom(Wave wave, int D, bool shannon)
    {
        for (int i = 0; i < data.Length; i++)
        {
            bool[] datai = data[i], wavedatai = wave.data[i];
            for (int t = 0; t < datai.Length; t++)
            {
                datai[t] = wavedatai[t];
                for (int d = 0; d < D; d++) compatible[i][t][d] = wave.compatible[i][t][d];
            }

            sumsOfOnes[i] = wave.sumsOfOnes[i];

            if (shannon)
            {
                sumsOfWeights[i] = wave.sumsOfWeights[i];
                sumsOfWeightLogWeights[i] = wave.sumsOfWeightLogWeights[i];
                entropies[i] = wave.entropies[i];
            }
        }
    }

    static readonly int[] opposite = { 2, 3, 0, 1, 5, 4 };
}
