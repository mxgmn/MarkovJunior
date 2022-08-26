// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System.Linq;
using System.Collections.Generic;

/// <summary>
/// Represents a constraint on the future state of a grid. Each observation is
/// associated with a <see cref="RuleNode">RuleNode</see> and a color which the
/// observation applies to.
/// </summary>
class Observation
{
    /// <summary>
    /// The color this observation is 'from'. If this is different to the color
    /// the observation applies to, then that color will be replaced with this
    /// one at the start of inference.
    /// </summary>
    readonly byte from;
    
    /// <summary>
    /// The observation's goal, as a bitmask of colors.
    /// </summary>
    readonly int to;

    public Observation(char from, string to, Grid grid)
    {
        this.from = grid.values[from];
        this.to = grid.Wave(to);
    }

    /// <summary>
    /// Computes the future state (as a flat array of color bitmasks) from the
    /// present state and the observations which apply to each color. The
    /// present state is also updated by replacing each observed color with the
    /// observation's '<see cref="Observation.from">from</see>' color, if that
    /// is different to the color the observation applies to.
    /// </summary>
    /// <returns><c>true</c> if all observed colors are present in the grid, otherwise <c>false</c>.</returns>
    public static bool ComputeFutureSetPresent(int[] future, byte[] state, Observation[] observations)
    {
        // mask for which colors are either unobserved or present in the grid
        bool[] mask = new bool[observations.Length];
        for (int k = 0; k < observations.Length; k++) if (observations[k] == null) mask[k] = true;
        
        for (int i = 0; i < state.Length; i++)
        {
            byte value = state[i];
            Observation obs = observations[value];
            mask[value] = true;
            if (obs != null)
            {
                // if this color is observed, set this cell's future to the observation's goal
                future[i] = obs.to;
                // `obs.from` may be different to the color the observation applies to
                state[i] = obs.from;
            }
            else
            {
                // otherwise the color is not observed, so set this cells' future to be the same as its present
                future[i] = 1 << value;
            }
        }

        // check that all observed colors were present in the grid
        for (int k = 0; k < mask.Length; k++) if (!mask[k])
            {
                //Console.WriteLine($"observed value {k} not present on the grid, observe-node returning false");
                return false;
            }
        return true;
    }

    /// <summary>
    /// Computes the forward potentials for the given grid state. The forward
    /// potential <c>potentials[c][x + y * MX + z * MX * MY]</c> is a lower
    /// bound for the number of rewrites it would take to reach any state where
    /// the color <c>c</c> occurs at position (x, y, z), by applying the given
    /// rules from the initial grid. A potential of -1 indicates that such a
    /// state cannot be reached.
    /// </summary>
    public static void ComputeForwardPotentials(int[][] potentials, byte[] state, int MX, int MY, int MZ, Rule[] rules)
    {
        potentials.Set2D(-1);
        for (int i = 0; i < state.Length; i++) potentials[state[i]][i] = 0;
        ComputePotentials(potentials, MX, MY, MZ, rules, false);
    }
    
    /// <summary>
    /// Computes the backward potentials for the given future. The backward
    /// potential <c>potentials[c][x * y * MX + z * MX * MY]</c> is a lower
    /// bound for the number of rewrites it would take to reach any state
    /// matching the given future, by applying the given rules from any grid
    /// where the color <c>c</c> occurs at position (x, y, z). A potential of
    /// -1 indicates that the future cannot be reached from such a state.
    /// </summary>
    public static void ComputeBackwardPotentials(int[][] potentials, int[] future, int MX, int MY, int MZ, Rule[] rules)
    {
        for (int c = 0; c < potentials.Length; c++)
        {
            int[] potential = potentials[c];
            for (int i = 0; i < future.Length; i++) potential[i] = (future[i] & (1 << c)) != 0 ? 0 : -1;
        }
        ComputePotentials(potentials, MX, MY, MZ, rules, true);
    }

    /// <summary>
    /// Helper function for computing forward and backward potentials.
    /// </summary>
    static void ComputePotentials(int[][] potentials, int MX, int MY, int MZ, Rule[] rules, bool backwards)
    {
        // compute the potentials by dynamic programming
        Queue<(byte c, int x, int y, int z)> queue = new();
        // enqueue cells with a potential of zero
        for (byte c = 0; c < potentials.Length; c++)
        {
            int[] potential = potentials[c];
            for (int i = 0; i < potential.Length; i++) if (potential[i] == 0) queue.Enqueue((c, i % MX, (i % (MX * MY)) / MX, i / (MX * MY)));
        }
        
        // buffer used to avoid applying the same rule in the same location more than once
        // matchMask[r][x + y * MX + z * MX * MY] is true when rule r has already been applied at (x, y, z)
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

    /// <summary>
    /// Estimates whether the given rule can possibly be matched at (x, y, z)
    /// after <c>t</c> steps. A return value of <c>true</c> indicates that it
    /// may be possible, whereas <c>false</c> indicates that it is definitely
    /// not possible.
    /// </summary>
    static bool ForwardMatches(Rule rule, int x, int y, int z, int[][] potentials, int t, int MX, int MY, bool backwards)
    {
        int dz = 0, dy = 0, dx = 0;
        // unions in input patterns are not implemented yet
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

    /// <summary>
    /// Updates the <c>potentials</c> array to account for the rule being
    /// possibly-applicable at the given position after <c>t</c> steps,
    /// and enqueues any changed cells.
    /// </summary>
    static void ApplyForward(Rule rule, int x, int y, int z, int[][] potentials, int t, int MX, int MY, Queue<(byte, int, int, int)> q, bool backwards)
    {
        // unions in input patterns are not implemented yet
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
                    // this is not the correct behaviour for a wildcard in the input pattern; not implemented yet
                    if (o != 0xff && potentials[o][idi] == -1)
                    {
                        potentials[o][idi] = t + 1;
                        q.Enqueue((o, xdx, ydy, zdz));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Determines whether the goal is reached, i.e. every cell in the present
    /// state matches the corresponding bitmask in the future state.
    /// </summary>
    public static bool IsGoalReached(byte[] present, int[] future)
    {
        for (int i = 0; i < present.Length; i++) if (((1 << present[i]) & future[i]) == 0) return false;
        return true;
    }

    /// <summary>
    /// Computes the minimum 'score' for a grid which matches the future state,
    /// using the given forward potentials. A 'score' of -1 indicates that the
    /// goal cannot be reached.
    /// </summary>
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
    
    /// <summary>
    /// Computes the 'score' of the grid, using the given backwards potentials.
    /// A 'score' of -1 indicates that the goal cannot be reached.
    /// </summary>
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
