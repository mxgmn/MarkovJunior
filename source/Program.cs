// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)
using System;
using System.Linq;
using System.Xml.Linq;
using System.Diagnostics;
using System.Collections.Generic;
static class Program
{
    static void Main()
    {
        // Start a timer to measure execution time
        Stopwatch sw = Stopwatch.StartNew();

        // Create or clear the output directory
        var folder = System.IO.Directory.CreateDirectory("output");
        foreach (var file in folder.GetFiles()) file.Delete();

        // Load the color palette from XML file - maps symbols to color values
        Dictionary<char, int> palette = XDocument.Load("resources/palette.xml").Root.Elements("color").ToDictionary(x => x.Get<char>("symbol"), x => (255 << 24) + Convert.ToInt32(x.Get<string>("value"), 16));

        // Create a random number generator for seeds
        Random meta = new();

        // Load the main models configuration file
        XDocument xdoc = XDocument.Load("models.xml", LoadOptions.SetLineInfo);

        // Process each model defined in the configuration
        foreach (XElement xmodel in xdoc.Root.Elements("model"))
        {
            // Extract model parameters from XML
            string name = xmodel.Get<string>("name");
            int linearSize = xmodel.Get("size", -1);  // Default size if not specified
            int dimension = xmodel.Get("d", 2);       // Default to 2D if not specified
            int MX = xmodel.Get("length", linearSize);
            int MY = xmodel.Get("width", linearSize);
            int MZ = xmodel.Get("height", dimension == 2 ? 1 : linearSize);  // Height is 1 for 2D models

            Console.Write($"{name} > ");

            // Try to load the specific model file
            string filename = $"models/{name}.xml";
            XDocument modeldoc;
            try { modeldoc = XDocument.Load(filename, LoadOptions.SetLineInfo); }
            catch (Exception)
            {
                Console.WriteLine($"ERROR: couldn't open xml file {filename}");
                continue;  // Skip this model if file can't be loaded
            }

            // Create an interpreter for this model
            Interpreter interpreter = Interpreter.Load(modeldoc.Root, MX, MY, MZ);
            if (interpreter == null)
            {
                Console.WriteLine("ERROR");
                continue;  // Skip if interpreter couldn't be created
            }

            // Extract additional generation parameters
            int amount = xmodel.Get("amount", 2);           // How many outputs to generate
            int pixelsize = xmodel.Get("pixelsize", 4);     // Size of pixels in output image
            string seedString = xmodel.Get<string>("seeds", null);  // Optional specific seeds
            int[] seeds = seedString?.Split(' ').Select(s => int.Parse(s)).ToArray();
            bool gif = xmodel.Get("gif", false);            // Whether to generate animated GIF
            bool iso = xmodel.Get("iso", false);            // Isometric view for 3D models
            int steps = xmodel.Get("steps", gif ? 1000 : 50000);  // Number of generation steps
            int gui = xmodel.Get("gui", 0);                 // GUI visualization level

            // Override amount for GIF mode (only generate one)
            if (gif) amount = 1;

            // Create a custom palette for this model (inherits from main palette)
            Dictionary<char, int> customPalette = new(palette);
            foreach (var x in xmodel.Elements("color")) customPalette[x.Get<char>("symbol")] = (255 << 24) + Convert.ToInt32(x.Get<string>("value"), 16);

            // Generate the specified number of outputs for this model
            for (int k = 0; k < amount; k++)
            {
                // Use provided seed if available, otherwise generate a random one
                int seed = seeds != null && k < seeds.Length ? seeds[k] : meta.Next();

                // Run the interpreter to generate output(s)
                foreach ((byte[] result, char[] legend, int FX, int FY, int FZ) in interpreter.Run(seed, steps, gif))
                {
                    // Map symbols to colors
                    int[] colors = legend.Select(ch => customPalette[ch]).ToArray();

                    // Determine output filename
                    string outputname = gif ? $"output/{interpreter.counter}" : $"output/{name}_{seed}";

                    // Save output as image or voxel file depending on dimensions
                    if (FZ == 1 || iso)
                    {
                        // Render 2D output or isometric view of 3D
                        var (bitmap, WIDTH, HEIGHT) = Graphics.Render(result, FX, FY, FZ, colors, pixelsize, gui);

                        // Display in GUI if enabled
                        if (gui > 0) GUI.Draw(name, interpreter.root, interpreter.current, bitmap, WIDTH, HEIGHT, customPalette);

                        // Save as PNG
                        Graphics.SaveBitmap(bitmap, WIDTH, HEIGHT, outputname + ".png");
                    }
                    else
                        // Save 3D output as VOX file format
                        VoxHelper.SaveVox(result, (byte)FX, (byte)FY, (byte)FZ, colors, outputname + ".vox");
                }
                Console.WriteLine("DONE");
            }
        }

        // Display total execution time
        Console.WriteLine($"time = {sw.ElapsedMilliseconds}");
    }
}

/*
=== SUMMARY ===

This code is a procedural content generator using the Wave Function Collapse (WFC) algorithm. 

Think of WFC like solving a jigsaw puzzle where each piece must connect properly with its neighbors according to rules. The program:

1. Loads configuration from XML files:
   - A main config file listing all models to generate
   - Individual model files with specific rules
   - A color palette to visualize the results

2. For each model, it:
   - Sets up parameters like size, dimensions (2D or 3D), and number of outputs
   - Creates a specialized "interpreter" that runs the WFC algorithm
   - Generates one or more outputs using either specified or random seeds
   - Renders and saves the results as PNG images or VOX 3D files

It's similar to how procedural games like Minecraft generate terrain, except it follows more complex pattern-matching rules defined in the XML files. The program can create images, animations (GIFs), or 3D models following these rules.

The code is useful for procedural generation in games, generative art, texture creation, and automatically creating complex structures that follow specific design patterns.
*/