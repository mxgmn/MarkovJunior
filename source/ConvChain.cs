// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Xml.Linq;

/// <summary>
/// <para>
/// A 'convchain' node runs the ConvChain algorithm on a part of the grid. The
/// algorithm attempts to replace cells of the 'substrate' color with two
/// colors (nominally 'black' and 'white'), such that the 'black' and 'white'
/// cells locally form patterns which occur in a sample image. A 'convchain'
/// node is inapplicable if the grid contains no substrate cells.
/// </para>
/// <para>
/// Non-substrate cells are left unchanged by the algorithm, but may contribute
/// to the 'local similarity' of patterns. 'Black' and 'white' cells originally
/// in the grid will be treated as parts of patterns; other non-substrate
/// colors will be treated as if they are 'black'. Patterns may wrap around the
/// grid boundary.
/// </para>
/// </summary>
/// <seealso href="https://github.com/mxgmn/ConvChain">ConvChain project on GitHub</seealso>
/// <seealso href="https://en.wikipedia.org/wiki/Metropolis%E2%80%93Hastings_algorithm">Metropolis–Hastings algorithm on Wikipedia</seealso>
class ConvChainNode : Node
{
    /// <summary>The size of each ConvChain pattern.</summary>
    int N;
    
    /// <summary>
    /// Controls the probability of generating patterns which are dissimilar
    /// to the sample image. A higher temperature makes dissimilarity more
    /// likely, whereas a lower temperature makes the algorithm converge more
    /// slowly. A temperature of 1.0 gives the Metropolis-Hastings algorithm.
    /// </summary>
    double temperature;
    
    /// <summary>
    /// Maps each <c>N * N</c> bitmask to the number of times the corresponding
    /// pattern appears in the sample image, or a small positive number if that
    /// pattern does not appear.
    /// </summary>
    double[] weights;

    /// <summary>The 'black' color.</summary>
    public byte c0;
    
    /// <summary>The 'white' color.</summary>
    public byte c1;
    
    /// <summary>Mask indicating which grid cells are the 'substrate', and may be changed.</summary>
    bool[] substrate;
    
    /// <summary>The color of the 'substrate', i.e. cells which may be changed by the ConvChain algorithm.</summary>
    byte substrateColor;
    
    /// <inheritdoc cref="RuleNode.counter"/>
    int counter;
    
    /// <inheritdoc cref="RuleNode.steps"/>
    int steps;

    /// <summary>The sample image, as a flat array.</summary>
    public bool[] sample;
    
    /// <summary>The width of the sample image.</summary>
    public int SMX;
    
    /// <summary>The height of the sample image.</summary>
    public int SMY;

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
        for (int i = 0; i < sample.Length; i++)
        {
            // -1 is 0xffffffff, i.e. white with alpha = 1
            sample[i] = bitmap[i] == -1;
        }

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
                // `(q1, q2) => false` means we don't deduplicate symmetries, so that weights aren't biased towards asymmetric patterns
                var symmetries = SymmetryHelper.SquareSymmetries(pattern, q => Helper.Rotated(q, N), q => Helper.Reflected(q, N), (q1, q2) => false, symmetry);
                foreach (bool[] q in symmetries) weights[q.Index()] += 1;
            }
        
        // ensure all weights are positive
        for (int k = 0; k < weights.Length; k++) if (weights[k] <= 0) weights[k] = 0.1;
        return true;
    }
    
    /// <summary>
    /// Toggles the grid state at index <c>i</c> between the two colors
    /// <c>c0</c> and <c>c1</c>.
    /// </summary>
    void Toggle(byte[] state, int i) => state[i] = state[i] == c0 ? c1 : c0;
    
    override public bool Go()
    {
        if (steps > 0 && counter >= steps) return false;

        int MX = grid.MX, MY = grid.MY;
        byte[] state = grid.state;
        
        // if this is the first step, replace substrate cells with pure random noise
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
            // this node is only applicable if there are any substrate cells
            return anySubstrate;
        }
        
        // otherwise, proceed by toggling random substrate cells
        for (int k = 0; k < state.Length; k++)
        {
            // choose a random substrate cell, by rejection sampling
            // the expected number of samples equals the total number of substrate cells, but samples are independent
            int r = ip.random.Next(state.Length);
            if (!substrate[r]) continue;

            int x = r % MX, y = r / MX;
            
            // calculate the ratio of the 'scores' after and before toggling the cell at (x, y)
            double q = 1;
            
            for (int sy = y - N + 1; sy <= y + N - 1; sy++) for (int sx = x - N + 1; sx <= x + N - 1; sx++)
                {
                    int ind = 0, difference = 0;
                    for (int dy = 0; dy < N; dy++) for (int dx = 0; dx < N; dx++)
                        {
                            // apply offsets, wrapping around the grid boundary
                            int X = sx + dx;
                            if (X < 0) X += MX;
                            else if (X >= MX) X -= MX;

                            int Y = sy + dy;
                            if (Y < 0) Y += MY;
                            else if (Y >= MY) Y -= MY;

                            // true if this cell is 'white', false if it is 'black'; other colors count as black
                            bool value = state[X + Y * MX] == c1;
                            int power = 1 << (dy * N + dx);
                            ind += value ? power : 0;
                            if (X == x && Y == y) difference = value ? power : -power;
                        }

                    q *= weights[ind - difference] / weights[ind];
                }
            
            // if the 'score' doesn't go down, always toggle
            if (q >= 1) { Toggle(state, r); continue; }
            
            // otherwise, toggle with some probability depending on how much worse the score gets
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
