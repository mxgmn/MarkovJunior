// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System.Linq;
using System.Collections.Generic;

class Observation
{
    readonly byte from;
    readonly int to;

    public Observation(char from, string to, Grid grid)
    {
        this.from = grid.values[from];
        this.to = grid.Wave(to);
    }

    public static bool ComputeFutureSetPresent(int[] future, byte[] state, Observation[] observations)
    {
        bool[] mask = new bool[observations.Length];
        for (int k = 0; k < observations.Length; k++) if (observations[k] == null) mask[k] = true;
        
        for (int i = 0; i < state.Length; i++)
        {
            byte value = state[i];
            Observation obs = observations[value];
            mask[value] = true;
            if (obs != null)
            {
                future[i] = obs.to;
                state[i] = obs.from;
            }
            else future[i] = 1 << value;
        }

        for (int k = 0; k < mask.Length; k++) if (!mask[k])
            {
                //Console.WriteLine($"observed value {k} not present on the grid, observe-node returning false");
                return false;
            }
        return true;
    }

    public static void ComputeForwardPotentials(int[][] potentials, byte[] state, int MX, int MY, int MZ, Rule[] rules)
    {
        potentials.Set2D(-1);
        for (int i = 0; i < state.Length; i++) potentials[state[i]][i] = 0;
        ComputePotentials(potentials, MX, MY, MZ, rules, false);
    }
    public static void ComputeBackwardPotentials(int[][] potentials, int[] future, int MX, int MY, int MZ, Rule[] rules)
    {
        for (int c = 0; c < potentials.Length; c++)
        {
            int[] potential = potentials[c];
            for (int i = 0; i < future.Length; i++) potential[i] = (future[i] & (1 << c)) != 0 ? 0 : -1;
        }
        ComputePotentials(potentials, MX, MY, MZ, rules, true);
    }

    static void ComputePotentials(int[][] potentials, int MX, int MY, int MZ, Rule[] rules, bool backwards)
    {
        Queue<(byte c, int x, int y, int z)> queue = new();
        for (byte c = 0; c < potentials.Length; c++)
        {
            int[] potential = potentials[c];
            for (int i = 0; i < potential.Length; i++) if (potential[i] == 0) queue.Enqueue((c, i % MX, (i % (MX * MY)) / MX, i / (MX * MY)));
        }
        bool[][] matchMask = AH.Array2D(rules.Length, potentials[0].Length, false);

        while (queue.Any())
        {
            (byte value, int x, int y, int z) = queue.Dequeue();
            int i = x + y * MX + z * MX * MY;
            int t = potentials[value][i];
            for (int r = 0; r < rules.Length; r++)
            {
                bool[] maskr = matchMask[r];
                Rule rule = rules[r];
                var shifts = backwards ? rule.oshifts[value] : rule.ishifts[value];
                for (int l = 0; l < shifts.Length; l++)
                {
                    var (shiftx, shifty, shiftz) = shifts[l];
                    int sx = x - shiftx;
                    int sy = y - shifty;
                    int sz = z - shiftz;

                    if (sx < 0 || sy < 0 || sz < 0 || sx + rule.IMX > MX || sy + rule.IMY > MY || sz + rule.IMZ > MZ) continue;
                    int si = sx + sy * MX + sz * MX * MY;
                    if (!maskr[si] && ForwardMatches(rule, sx, sy, sz, potentials, t, MX, MY, backwards))
                    {
                        maskr[si] = true;
                        ApplyForward(rule, sx, sy, sz, potentials, t, MX, MY, queue, backwards);
                    }
                }
            }
        }
    }

    static bool ForwardMatches(Rule rule, int x, int y, int z, int[][] potentials, int t, int MX, int MY, bool backwards)
    {
        int dz = 0, dy = 0, dx = 0;
        byte[] a = backwards ? rule.output : rule.binput;
        for (int di = 0; di < a.Length; di++)
        {
            byte value = a[di];
            if (value != 0xff)
            {
                int current = potentials[value][x + dx + (y + dy) * MX + (z + dz) * MX * MY];
                if (current > t || current == -1) return false;
            }
            dx++;
            if (dx == rule.IMX)
            {
                dx = 0; dy++;
                if (dy == rule.IMY) { dy = 0; dz++; }
            }
        }
        return true;
    }

    static void ApplyForward(Rule rule, int x, int y, int z, int[][] potentials, int t, int MX, int MY, Queue<(byte, int, int, int)> q, bool backwards)
    {
        byte[] a = backwards ? rule.binput : rule.output;
        for (int dz = 0; dz < rule.IMZ; dz++)
        {
            int zdz = z + dz;
            for (int dy = 0; dy < rule.IMY; dy++)
            {
                int ydy = y + dy;
                for (int dx = 0; dx < rule.IMX; dx++)
                {
                    int xdx = x + dx;
                    int idi = xdx + ydy * MX + zdz * MX * MY;
                    int di = dx + dy * rule.IMX + dz * rule.IMX * rule.IMY;
                    byte o = a[di];
                    if (o != 0xff && potentials[o][idi] == -1)
                    {
                        potentials[o][idi] = t + 1;
                        q.Enqueue((o, xdx, ydy, zdz));
                    }
                }
            }
        }
    }

    public static bool IsGoalReached(byte[] present, int[] future)
    {
        for (int i = 0; i < present.Length; i++) if (((1 << present[i]) & future[i]) == 0) return false;
        return true;
    }

    public static int ForwardPointwise(int[][] potentials, int[] future)
    {
        int sum = 0;
        for (int i = 0; i < future.Length; i++)
        {
            int f = future[i];
            int min = 1000, argmin = -1;
            for (int c = 0; c < potentials.Length; c++, f >>= 1)
            {
                int potential = potentials[c][i];
                if ((f & 1) == 1 && potential >= 0 && potential < min)
                {
                    min = potential;
                    argmin = c;
                }
            }
            if (argmin < 0) return -1;
            sum += min;
        }
        return sum;
    }

    public static int BackwardPointwise(int[][] potentials, byte[] present)
    {
        int sum = 0;
        for (int i = 0; i < present.Length; i++)
        {
            int potential = potentials[present[i]][i];
            if (potential < 0) return -1;
            sum += potential;
        }
        return sum;
    }
}
