// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Xml.Linq;
using System.Collections.Generic;

/// <summary>
/// Base class for 'wfc' nodes, which run the Wave Function Collapse algorithm.
/// </summary>
/// <seealso href="https://github.com/mxgmn/WaveFunctionCollapse">WaveFunctionCollapse project on GitHub</seealso>
abstract class WFCNode : Branch
{
    /// <summary>The current state of the WFC algorithm.</summary>
    protected Wave wave;
    
    /// <summary>
    /// A precomputed table of which patterns can occur in which directions
    /// from which other patterns. <c>propagator[d][p]</c> contains <c>q</c> if
    /// and only if pattern <c>q</c> can occur adjacent to pattern <c>p</c> in
    /// the direction <c>d</c>.
    /// </summary>
    protected int[][][] propagator;
    
    /// <summary>The number of distinct patterns in the tileset or sample image.</summary>
    protected int P;
    
    /// <summary>The size of the patterns. For the tile model, this is always 1.</summary>
    protected int N = 1;

    /// <summary>
    /// The stack of changes which have not yet been propagated to the rest of
    /// the state. Each entry is a (node, pattern) tuple, where that pattern
    /// cannot occur at that node.
    /// </summary>
    (int, int)[] stack;
    
    /// <summary>The index of the first unused entry in the <see cref="WFCNode.stack">stack</see>.</summary>
    int stacksize;

    /// <summary>Maps each pattern to its weight.</summary>
    protected double[] weights;
    
    /// <summary>If <c>shannon</c> is <c>true</c>, maps each pattern to its (weight * log weight).</summary>
    double[] weightLogWeights;
    
    /// <summary>If <c>shannon</c> is <c>true</c>, it is the sum of weights of all patterns.</summary>
    double sumOfWeights;
    
    /// <summary>If <c>shannon</c> is <c>true</c>, it is the sum of (weight * log weight) of all patterns.</summary>
    double sumOfWeightLogWeights;
    
    /// <summary>if <c>shannon</c> is <c>true</c>, it is the entropy of a distribution where all patterns are possible.</summary>
    double startingEntropy;

    /// <inheritdoc cref="MapNode.newgrid"/>
    protected Grid newgrid;
    
    /// <summary>
    /// The initial state of the WFC algorithm, once the initial constraints
    /// from the input grid have been propagated.
    /// </summary>
    Wave startwave;

    /// <summary>
    /// Maps each color to a mask of possible patterns, for the initial grid
    /// constraints. If a color is not present in the dictionary, then cells
    /// with that color have no initial constraints.
    /// </summary>
    protected Dictionary<byte, bool[]> map;
    
    /// <summary>If <c>true</c>, patterns will wrap around at the grid's edges.</summary>
    protected bool periodic;
    
    /// <summary>
    /// If <c>true</c>, the algorithm calculates Shannon entropies to find
    /// positions which are most constrained; otherwise, the number of possible
    /// patterns is used as a less reliable estimate. Calculating entropies is
    /// slower, but more likely to avoid contradictions so that the algorithm
    /// completes successfully.
    /// </summary>
    /// <seealso href="https://en.wikipedia.org/wiki/Entropy_(information_theory)">Entropy (information theory) on Wikipedia</seealso>
    protected bool shannon;

    /// <summary>Temporary buffer used for taking weighted samples from waves.</summary>
    double[] distribution;
    
    /// <summary>
    /// The maximum number of attempts which will be made to apply the WFC
    /// algorithm. This node will fail to execute if the algorithm reaches a
    /// contradiction this many times. Must be at least 1.
    /// </summary>
    int tries;

    /// <summary>
    /// The name of the resource file containing the tileset or sample image
    /// which this WFC model is trained on.
    /// </summary>
    public string name;

    /// <summary>
    /// If <c>true</c>, then the next execution step will initialise <see cref="WFCNode.startwave">startwave</see>
    /// and search for a good PRNG seed. Otherwise, a good seed has been found,
    /// so each execution step will 'observe' a node and propagate constraints,
    /// knowing that it will not lead to a contradiction.
    /// </summary>
    bool firstgo = true;
    
    /// <summary>The PRNG instance; it is separate from <see cref="Interpreter.random">the interpreter's</see>.</summary>
    Random random;
    
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

    public override bool Go()
    {
        // if the WFC algorithm has completed, then behave like a sequence node
        if (n >= 0) return base.Go();

        if (firstgo)
        {
            // initialise the WFC algorithm state
            wave.Init(propagator, sumOfWeights, sumOfWeightLogWeights, startingEntropy, shannon);

            // apply the initial constraints from the grid
            for (int i = 0; i < wave.data.Length; i++)
            {
                byte value = grid.state[i];
                if (map.ContainsKey(value))
                {
                    bool[] startWave = map[value];
                    for (int t = 0; t < P; t++) if (!startWave[t]) Ban(i, t);
                }
            }
            
            // propagate constraints and check for consistency
            bool firstSuccess = Propagate();
            if (!firstSuccess)
            {
                Console.WriteLine("initial conditions are contradictive");
                return false;
            }
            
            // save a copy of this initial state so we can try multiple times
            startwave.CopyFrom(wave, propagator.Length, shannon);
            
            // search for a good seed; if it's not found, this node is inapplicable
            int? goodseed = GoodSeed();
            if (goodseed == null) return false;
            
            // found a good seed; reset the state to 'replay' the algorithm from the start, using that seed
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
            // we have a known good seed, so no need to check for contradictions
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

    /// <summary>
    /// Attempts to find a PRNG seed for which the WFC algorithm completes
    /// without reaching a contradiction, i.e. a 'good' seed.
    /// </summary>
    /// <returns>A good seed, or <c>null</c> if no good seed is found.</returns>
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

    /// <summary>
    /// Finds a position with multiple possible patterns, preferring positions
    /// which are the most constrained. If no such position exists, then -1 is
    /// returned, implying that the WFC algorithm has completed.
    /// </summary>
    int NextUnobservedNode(Random random)
    {
        int MX = grid.MX, MY = grid.MY, MZ = grid.MZ;
        double min = 1E+4;
        int argmin = -1;
        for (int z = 0; z < MZ; z++) for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
                {
                    // skip positions where a pattern would be out of bounds
                    if (!periodic && (x + N > MX || y + N > MY || z + 1 > MZ)) continue;
                    
                    int i = x + y * MX + z * MX * MY;
                    int remainingValues = wave.sumsOfOnes[i];
                    double entropy = shannon ? wave.entropies[i] : remainingValues;
                    if (remainingValues > 1 && entropy <= min)
                    {
                        // add a small noise term to randomly choose between equal minima
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

    /// <summary>
    /// 'Collapses the wave function' at the given position, by taking a
    /// weighted sample of its possibilities and 'observing' that result.
    /// </summary>
    /// <param name="node">The index of the position, equal to <c>x + y * MX + z * MX * MY</c>.</param>
    /// <param name="random"><inheritdoc cref="WFCNode.random" path="/summary"/></param>
    void Observe(int node, Random random)
    {
        bool[] w = wave.data[node];
        for (int t = 0; t < P; t++) distribution[t] = w[t] ? weights[t] : 0.0;
        int r = distribution.Random(random.NextDouble());
        
        // mark every other possible pattern as no longer possible at this node
        for (int t = 0; t < P; t++) if (w[t] != (t == r)) Ban(node, t);
    }

    /// <summary>
    /// Propagates the constraints, using the <see cref="WFCNode.stack">stack</see>
    /// of unprocessed changes.
    /// </summary>
    /// <returns><c>true</c> if no contradiction has been found, otherwise <c>false</c>.</returns>
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

    /// <summary>
    /// Updates the algorithm state when it is discovered that a grid position
    /// cannot have a particular pattern.
    /// </summary>
    /// <param name="i">The index of the position, equal to <c>x + y * MX + z * MX * MY</c>.</param>
    /// <param name="t">The pattern which cannot occur at that grid cell.</param>
    void Ban(int i, int t)
    {
        // t is not possible at i
        wave.data[i][t] = false;
        
        // no patterns are possible at i's neighbours if t occurs at i, since that is a contradiction
        int[] comp = wave.compatible[i][t];
        for (int d = 0; d < propagator.Length; d++) comp[d] = 0;
        
        // push to the stack, so constraints will be propagated
        stack[stacksize] = (i, t);
        stacksize++;
        
        // keep statistics up to date
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

    /// <summary>
    /// Writes a representation of the current WFC algorithm state to the grid.
    /// </summary>
    protected abstract void UpdateState();

    /// <summary>Maps each direction index to that direction's delta x.</summary>
    protected static int[] DX = { 1, 0, -1, 0, 0, 0 };
    
    /// <summary>Maps each direction index to that direction's delta y.</summary>
    protected static int[] DY = { 0, 1, 0, -1, 0, 0 };
    
    /// <summary>Maps each direction index to that direction's delta z.</summary>
    protected static int[] DZ = { 0, 0, 0, 0, 1, -1 };
}

/// <summary>
/// Represents a state of the Wave Function Collapse algorithm.
/// </summary>
class Wave
{
    /// <summary>
    /// Table of flags for which patterns are possible at which grid cells.
    /// <c>data[x + y * MX + z * MX * MY][p]</c> is true if and only if pattern
    /// <c>p</c> is possible at position (x, y, z).
    /// </summary>
    public bool[][] data;
    
    /// <summary>
    /// Counts how many patterns would be possible at each grid cell's
    /// neighbours, depending on the cell's pattern. <c>compatible[x + y * MX + z * MX * MY][p][d]</c>
    /// is the number of patterns which would be possible at the neighbour in
    /// direction <c>d</c>, if pattern <c>p</c> occurred at position (x, y, z).
    /// </summary>
    public int[][][] compatible;

    /// <summary>Maps each position to the number of possible patterns there.</summary>
    public int[] sumsOfOnes;
    
    /// <summary>If <c>shannon</c> is <c>true</c>, maps each position to the sum of weights of the possible patterns there.</summary>
    public double[] sumsOfWeights;
    
    /// <summary>If <c>shannon</c> is <c>true</c>, maps each position to the sum of (weight * log weight) of the possible patterns there.</summary>
    public double[] sumsOfWeightLogWeights;
    
    /// <summary>If <c>shannon</c> is <c>true</c>, maps each position to the Shannon entropy of the distribution of possible patterns there.</summary>
    public double[] entropies;
    
    /// <param name="length">The length of the grid state, i.e. the number of cells in the grid.</param>
    /// <param name="P"><inheritdoc cref="WFCNode.P" path="/summary"/></param>
    /// <param name="D">The number of directions a neighbour can be in. This is 4 for a 2D model or 6 for a 3D model.</param>
    /// <param name="shannon"><inheritdoc cref="WFCNode.shannon" path="/summary"/></param>
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

    /// <summary>
    /// Initialises the state of the WFC algorithm to one where all patterns
    /// are possible at all positions.
    /// </summary>
    /// <param name="propagator"><inheritdoc cref="WFCNode.propagator" path="/summary"/></param>
    /// <param name="sumOfWeights"><inheritdoc cref="WFCNode.sumOfWeights" path="/summary"/></param>
    /// <param name="sumOfWeightLogWeights"><inheritdoc cref="WFCNode.sumOfWeightLogWeights" path="/summary"/></param>
    /// <param name="startingEntropy"><inheritdoc cref="WFCNode.startingEntropy" path="/summary"/></param>
    /// <param name="shannon"><inheritdoc cref="WFCNode.shannon" path="/summary"/></param>
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

    /// <summary>
    /// Copies the state from another wave to this one.
    /// </summary>
    /// <param name="wave">The wave to copy the state from.</param>
    /// <param name="D"><inheritdoc cref="Wave.Wave(int, int, int, bool)" path="/param[@name='D']"/></param>
    /// <param name="shannon"><inheritdoc cref="WFCNode.shannon" path="/summary"/></param>
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
    
    /// <summary>Maps each direction index to the index of its opposite direction.</summary>
    static readonly int[] opposite = { 2, 3, 0, 1, 5, 4 };
}
