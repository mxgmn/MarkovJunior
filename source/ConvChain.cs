// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Xml.Linq;

// ConvChainNode implements a Markov Chain Monte Carlo approach for pattern synthesis
class ConvChainNode : Node
{
    int N;                  // Size of neighborhood/pattern (typically 3x3)
    double temperature;     // Controls randomness in the generation process
    double[] weights;       // Weights for each possible pattern configuration

    public byte c0, c1;     // Values for "black" and "white" pixels
    bool[] substrate;       // Tracks which cells can be modified
    byte substrateColor;    // Color indicating where generation should occur
    int counter, steps;     // Current step and max steps counters

    public bool[] sample;   // Sample pattern used as reference
    public int SMX, SMY;    // Sample dimensions

    // Load configuration from XML
    override protected bool Load(XElement xelem, bool[] symmetry, Grid grid)
    {
        // Currently only works with 2D grids
        if (grid.MZ != 1)
        {
            Interpreter.WriteLine("convchain currently works only for 2d");
            return false;
        }

        // Load the sample image to use as a reference
        string name = xelem.Get<string>("sample");
        string filename = $"resources/samples/{name}.png";
        int[] bitmap;
        (bitmap, SMX, SMY, _) = Graphics.LoadBitmap(filename);
        if (bitmap == null)
        {
            Interpreter.WriteLine($"couldn't load ConvChain sample {filename}");
            return false;
        }
        // Convert to boolean array (true = white, false = black)
        sample = new bool[bitmap.Length];
        for (int i = 0; i < sample.Length; i++) sample[i] = bitmap[i] == -1;

        // Load parameters from XML
        N = xelem.Get("n", 3);                             // Size of pattern neighborhood
        steps = xelem.Get("steps", -1);                    // Number of steps (-1 = unlimited)
        temperature = xelem.Get("temperature", 1.0);       // Temperature for MCMC
        c0 = grid.values[xelem.Get<char>("black")];        // Value for black pixels
        c1 = grid.values[xelem.Get<char>("white")];        // Value for white pixels
        substrateColor = grid.values[xelem.Get<char>("on")]; // Which cells can be modified

        substrate = new bool[grid.state.Length];

        // Count pattern frequencies in the sample image
        weights = new double[1 << (N * N)];                // Allocate weights for all possible patterns
        for (int y = 0; y < SMY; y++) for (int x = 0; x < SMX; x++)
            {
                // Extract pattern at this position
                bool[] pattern = Helper.Pattern((dx, dy) => sample[(x + dx) % SMX + (y + dy) % SMY * SMX], N);
                // Add all symmetric variants of this pattern
                var symmetries = SymmetryHelper.SquareSymmetries(pattern, q => Helper.Rotated(q, N), q => Helper.Reflected(q, N), (q1, q2) => false, symmetry);
                foreach (bool[] q in symmetries) weights[q.Index()] += 1;
            }

        // Ensure all weights are positive
        for (int k = 0; k < weights.Length; k++) if (weights[k] <= 0) weights[k] = 0.1;
        return true;
    }

    // Flip a cell between black and white
    void Toggle(byte[] state, int i) => state[i] = state[i] == c0 ? c1 : c0;

    // Main generation method
    override public bool Go()
    {
        // Stop if we've reached the maximum steps
        if (steps > 0 && counter >= steps) return false;

        int MX = grid.MX, MY = grid.MY;
        byte[] state = grid.state;

        // First step: initialize the substrate with random values
        if (counter == 0)
        {
            bool anySubstrate = false;
            for (int i = 0; i < substrate.Length; i++) if (state[i] == substrateColor)
                {
                    // Randomly set each substrate cell to black or white
                    state[i] = ip.random.Next(2) == 0 ? c0 : c1;
                    substrate[i] = true;
                    anySubstrate = true;
                }
            counter++;
            return anySubstrate;
        }

        // Main MCMC loop: try to flip random cells
        for (int k = 0; k < state.Length; k++)
        {
            // Pick a random cell
            int r = ip.random.Next(state.Length);
            if (!substrate[r]) continue;  // Skip if not part of substrate

            int x = r % MX, y = r / MX;
            double q = 1;  // Acceptance probability

            // Check all possible patterns that include this cell
            for (int sy = y - N + 1; sy <= y + N - 1; sy++) for (int sx = x - N + 1; sx <= x + N - 1; sx++)
                {
                    int ind = 0, difference = 0;
                    // Construct pattern index
                    for (int dy = 0; dy < N; dy++) for (int dx = 0; dx < N; dx++)
                        {
                            // Handle wrapping for periodic boundary conditions
                            int X = sx + dx;
                            if (X < 0) X += MX;
                            else if (X >= MX) X -= MX;

                            int Y = sy + dy;
                            if (Y < 0) Y += MY;
                            else if (Y >= MY) Y -= MY;

                            // Compute pattern index and how it would change if we flip the cell
                            bool value = state[X + Y * MX] == c1;
                            int power = 1 << (dy * N + dx);
                            ind += value ? power : 0;
                            if (X == x && Y == y) difference = value ? power : -power;
                        }

                    // Multiply acceptance probability by ratio of weights
                    q *= weights[ind - difference] / weights[ind];
                }

            // Metropolis-Hastings acceptance criterion
            if (q >= 1) { Toggle(state, r); continue; }         // Accept if probability increases
            if (temperature != 1) q = Math.Pow(q, 1.0 / temperature);  // Apply temperature
            if (q > ip.random.NextDouble()) Toggle(state, r);    // Probabilistic acceptance
        }

        counter++;
        return true;
    }

    // Reset the node for reuse
    override public void Reset()
    {
        // Clear substrate markers
        for (int i = 0; i < substrate.Length; i++) substrate[i] = false;
        counter = 0;
    }
}

/*
SUMMARY:
This code is a pattern generator that creates textures similar to an example image you provide.

Imagine you have a small sample of a pattern (like brick wall, grass, or clouds) and want to create 
a larger version that looks similar. This ConvChainNode does exactly that!

It works by:
1. Loading a small sample image
2. Counting how often different small patterns (typically 3x3 pixels) appear in the sample
3. Starting with random black/white pixels in your target area
4. Repeatedly picking a random pixel and deciding whether to flip it from black to white (or vice versa)
5. Making this decision based on whether the flip would create patterns that look more like the sample

The process is like filling in a coloring book while constantly checking a reference picture to make 
sure your overall design matches the style. The "temperature" controls how strictly it follows the 
sample patterns - higher temperature allows more creativity/randomness.

This technique is used in games, art, and design to create natural-looking textures that match a 
specific style without exact repetition.
*/