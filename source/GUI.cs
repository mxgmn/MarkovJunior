// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

static class GUI
{
    // Configuration values loaded from settings.xml
    static readonly int S, SMALL, MAXWIDTH, ZSHIFT, HINDENT, HGAP, HARROW, HLINE, VSKIP, SMALLVSKIP, FONTSHIFT, AFTERFONT;
    static readonly bool DENSE, D3;
    public static readonly int BACKGROUND, INACTIVE, ACTIVE;

    // Font configuration
    const string FONT = "Tamzen8x16r", TITLEFONT = "Tamzen8x16b";
    static readonly (bool[], int FX, int FY)[] fonts;

    // Character map for text rendering
    static readonly char[] legend = "ABCDEFGHIJKLMNOPQRSTUVWXYZ 12345abcdefghijklmnopqrstuvwxyz\u03bb67890{}[]()<>$*-+=/#_%^@\\&|~?'\"`!,.;:".ToCharArray();
    static readonly Dictionary<char, byte> map;

    // Static constructor - loads fonts and settings
    static GUI()
    {
        // Initialize character map for font rendering
        map = new Dictionary<char, byte>();
        for (int i = 0; i < legend.Length; i++) map.Add(legend[i], (byte)i);
        fonts = new (bool[], int, int)[2];

        // Load regular font
        (int[] bitmap, int width, int height, _) = Graphics.LoadBitmap($"resources/fonts/{FONT}.png");
        int b0 = bitmap[0];
        int b1 = bitmap[width - 1];
        fonts[0] = (bitmap.Select(argb => argb != b0 && argb != b1).ToArray(), width / 32, height / 3);

        // Load title/bold font
        (bitmap, width, height, _) = Graphics.LoadBitmap($"resources/fonts/{TITLEFONT}.png");
        b0 = bitmap[0];
        b1 = bitmap[width - 1];
        fonts[1] = (bitmap.Select(argb => argb != b0 && argb != b1).ToArray(), width / 32, height / 3);

        // Load GUI settings from XML file
        XElement settings = XDocument.Load("resources/settings.xml").Root;
        S = settings.Get("squareSize", 7);                // Normal square size for grid rendering
        SMALL = settings.Get("smallSquareSize", 3);       // Small square size for compact rendering
        MAXWIDTH = settings.Get("maxwidth", 10);          // Max width before switching to small squares
        ZSHIFT = settings.Get("zshift", 2);               // Shift for 3D visualization (isometric effect)
        HINDENT = settings.Get("hindent", 30);            // Horizontal indentation
        HGAP = settings.Get("hgap", 2);                   // Horizontal gap between elements
        HARROW = settings.Get("harrow", 10);              // Arrow length
        HLINE = settings.Get("hline", 14);                // Horizontal line length
        VSKIP = settings.Get("vskip", 2);                 // Vertical spacing
        SMALLVSKIP = settings.Get("smallvskip", 2);       // Small vertical spacing
        FONTSHIFT = settings.Get("fontshift", 2);         // Vertical font shift
        AFTERFONT = settings.Get("afterfont", 4);         // Spacing after text
        DENSE = settings.Get("dense", true);              // Compact layout mode
        D3 = settings.Get("d3", true);                    // 3D visualization mode

        // Color settings (ARGB format)
        BACKGROUND = (255 << 24) + Convert.ToInt32(settings.Get("background", "222222"), 16);
        INACTIVE = (255 << 24) + Convert.ToInt32(settings.Get("inactive", "666666"), 16);
        ACTIVE = (255 << 24) + Convert.ToInt32(settings.Get("active", "ffffff"), 16);
    }

    // Main rendering method for the entire WFC node tree
    public static void Draw(string name, Branch root, Branch current, int[] bitmap, int WIDTH, int HEIGHT, Dictionary<char, int> palette)
    {
        // Draw a filled rectangle
        void drawRectangle(int x, int y, int width, int height, int color)
        {
            if (y + height > HEIGHT) return;  // Clipping check
            for (int dy = 0; dy < height; dy++) for (int dx = 0; dx < width; dx++) bitmap[x + dx + (y + dy) * WIDTH] = color;
        };

        // Draw a colored square for a specific character
        void drawSquare(int x, int y, int S, char c) => drawRectangle(x, y, S, S, palette[c]);

        // Draw a square with a shadow effect (3D look)
        void drawShadedSquare(int x, int y, int S, int color)
        {
            drawRectangle(x, y, S, S, color);
            drawRectangle(x + S, y, 1, S + 1, BACKGROUND);  // Right shadow
            drawRectangle(x, y + S, S + 1, 1, BACKGROUND);  // Bottom shadow
        };

        // Draw a horizontal line
        void drawHLine(int x, int y, int length, int color, bool dashed = false)
        {
            if (length <= 0 || x < 0 || x + length >= WIDTH) return;  // Bounds check
            if (!dashed) drawRectangle(x, y, length, 1, color);
            else
            {
                if (y >= HEIGHT) return;
                int shift = length % 4 == 0 ? 1 : 0;
                for (int dx = 0; dx < length; dx++) if ((dx + shift) / 2 % 2 == 0) bitmap[x + dx + y * WIDTH] = color;
            }
        }

        // Draw a vertical line
        void drawVLine(int x, int y, int height, int color)
        {
            if (x < 0) return;  // Bounds check
            int yend = Math.Min(y + height, HEIGHT);
            drawRectangle(x, y, 1, yend - y, color);
        }

        // Render text using bitmap font
        int write(string s, int x, int y, int color, int font = 0)
        {
            int fontshift = font == 0 ? FONTSHIFT : 0;
            var (f, FX, FY) = fonts[font];
            if (y - FONTSHIFT + FY >= HEIGHT) return -1;  // Bounds check

            // Draw each character
            for (int i = 0; i < s.Length; i++)
            {
                int p = map[s[i]];
                int px = p % 32, py = p / 32;  // Character position in the font bitmap

                // Draw each pixel of the character
                for (int dy = 0; dy < FY; dy++) for (int dx = 0; dx < FX; dx++)
                        if (f[px * FX + dx + (py * FY + dy) * FX * 32]) bitmap[x + i * FX + dx + (y + dy - fontshift) * WIDTH] = color;
            }
            return s.Length * FX;  // Return text width
        };

        // Store level and height for each node
        Dictionary<Node, (int level, int height)> lh = new();

        // Draw connecting line from parent to node
        void drawDash(Node node, bool markov, bool active)
        {
            if (node == root) return;
            (int level, int height) = lh[node];
            int extra = markov ? 3 : 1;
            drawHLine(level * HINDENT - HLINE - HGAP - extra, height + S / 2, (node is MarkovNode || node is SequenceNode ? HINDENT : HLINE) + extra, active ? ACTIVE : INACTIVE);
        };

        // Draw bracket connecting a branch's children
        void drawBracket(Branch branch, int level, int n, bool active)
        {
            int first = lh[branch.nodes[0]].height;
            int last = lh[branch.nodes[n]].height;
            int x = (level + 1) * HINDENT - HGAP - HLINE;
            int color = active ? ACTIVE : INACTIVE;
            bool markov = branch is MarkovNode;
            drawVLine(x, first + S / 2, last - first + 1, color);
            drawVLine(x - (markov ? 3 : 1), first + S / 2, last - first + 1, color);
        };

        // Draw a grid of cells
        int drawArray(byte[] a, int x, int y, int MX, int MY, int MZ, char[] characters, int S)
        {
            for (int dz = 0; dz < MZ; dz++) for (int dy = 0; dy < MY; dy++) for (int dx = 0; dx < MX; dx++)
                    {
                        byte i = a[dx + dy * MX + dz * MX * MY];
                        int color = i != 0xff ? palette[characters[i]] : (D3 ? INACTIVE : BACKGROUND);
                        // Use offset for 3D effect in Z dimension
                        drawShadedSquare(x + dx * S + (MZ - dz - 1) * ZSHIFT, y + dy * S + (MZ - dz - 1) * ZSHIFT, S, color);
                    }
            return MX * S + (MZ - 1) * ZSHIFT;  // Return width of the rendered array
        };

        // Draw a binary (two-color) sample grid
        void drawSample(int x, int y, bool[] sample, int MX, int MY, byte c0, byte c1, char[] characters, int S)
        {
            for (int dy = 0; dy < MY; dy++) for (int dx = 0; dx < MX; dx++)
                {
                    byte b = sample[dx + dy * MX] ? c1 : c0;
                    drawSquare(x + dx * S, y + dy * S, S, characters[b]);
                }
        };

        // Start rendering
        int y = fonts[1].FY / 2;
        write(name, 8, y, ACTIVE, 1);  // Write title
        y += (int)(AFTERFONT * fonts[1].FY / 2);

        // Recursive function to draw the entire node tree
        void draw(Node node, int level)
        {
            lh.Add(node, (level, y));  // Record position for connections
            int x = level * HINDENT;
            char[] characters = node.grid.characters;

            // Handle branch nodes (contain child nodes)
            if (node is Branch branch)
            {
                int LINECOLOR = branch == current && branch.n < 0 ? ACTIVE : INACTIVE;
                if (branch is WFCNode wfcnode)
                {
                    // Draw WFC node
                    write($"wfc {wfcnode.name}", x, y, LINECOLOR);
                    y += fonts[0].FY + VSKIP;
                }
                else if (branch is MapNode mapnode)
                {
                    // Draw MapNode rules
                    for (int r = 0; r < mapnode.rules.Length; r++)
                    {
                        Rule rule = mapnode.rules[r];
                        if (!rule.original) continue;  // Skip symmetry-generated rules
                        int s = rule.IMX * rule.IMY > MAXWIDTH ? SMALL : S;  // Use small squares for large rules

                        // Draw input pattern
                        x += drawArray(rule.binput, x, y, rule.IMX, rule.IMY, rule.IMZ, characters, s) + HGAP;
                        // Draw arrow connecting input to output
                        drawHLine(x, y + S / 2, HARROW, LINECOLOR, true);
                        x += HARROW + HGAP;
                        // Draw output pattern
                        x += drawArray(rule.output, x, y, rule.OMX, rule.OMY, rule.OMZ, mapnode.newgrid.characters, s) + HGAP;

                        // Move down for next rule
                        y += Math.Max(rule.IMY, rule.OMY) * s + (Math.Max(rule.IMZ, rule.OMZ) - 1) * ZSHIFT + SMALLVSKIP;
                        x = level * HINDENT;
                    }
                    y += VSKIP;
                }

                // Process child nodes
                bool markov = branch is MarkovNode;
                bool sequence = branch is SequenceNode;
                foreach (var child in branch.nodes)
                {
                    draw(child, markov || sequence ? level + 1 : level);
                    drawDash(child, markov, false);
                }
            }
            else  // Leaf nodes (no children)
            {
                bool active = current != null && current.n >= 0 && current.nodes[current.n] == node;
                int NODECOLOR = active ? ACTIVE : INACTIVE;
                if (node is RuleNode rulenode)
                {
                    // Draw RuleNode rules
                    for (int r = 0; r < rulenode.rules.Length && r < 40; r++)  // Limit to 40 rules max
                    {
                        Rule rule = rulenode.rules[r];
                        if (!rule.original) continue;  // Skip symmetry-generated rules
                        int s = rule.IMX * rule.IMY > MAXWIDTH ? SMALL : S;

                        int LINECOLOR = (active && IsActive(rulenode, r)) ? ACTIVE : INACTIVE;
                        x += drawArray(rule.binput, x, y, rule.IMX, rule.IMY, rule.IMZ, characters, s) + HGAP;

                        drawHLine(x, y + S / 2, HARROW, LINECOLOR, rulenode is not OneNode);
                        x += HARROW + HGAP;
                        x += drawArray(rule.output, x, y, rule.OMX, rule.OMY, rule.OMZ, characters, s) + HGAP;

                        // Draw counter for nodes with steps limit
                        if (rulenode.steps > 0) write($" {rulenode.counter}/{rulenode.steps}", x, y, LINECOLOR);
                        y += rule.IMY * s + (rule.IMZ - 1) * ZSHIFT + SMALLVSKIP;
                        x = level * HINDENT;
                    }

                    // Draw fields for potentials-based rules
                    if (rulenode.fields != null)
                    {
                        y += SMALLVSKIP;
                        for (int c = 0; c < rulenode.fields.Length; c++)
                        {
                            Field field = rulenode.fields[c];
                            if (field == null) continue;

                            // Two layout options - dense or verbose
                            if (!DENSE)
                            {
                                x += write("field for ", x, y, NODECOLOR);
                                drawSquare(x, y, S, characters[c]);
                                x += S;

                                x += write(field.inversed ? " from " : " to ", x, y, NODECOLOR);
                                byte[] zero = Helper.NonZeroPositions(field.zero);
                                for (int k = 0; k < zero.Length; k++, x += S) drawSquare(x, y, S, characters[zero[k]]);

                                x += write(" on ", x, y, NODECOLOR);
                                byte[] substrate = Helper.NonZeroPositions(field.substrate);
                                for (int k = 0; k < substrate.Length; k++, x += S) drawSquare(x, y, S, characters[substrate[k]]);
                            }
                            else
                            {
                                x += write("field ", x, y, NODECOLOR);
                                drawSquare(x, y, S, characters[c]);
                                x += S + HGAP;
                                byte[] zero = Helper.NonZeroPositions(field.zero);
                                for (int k = 0; k < zero.Length; k++, x += S) drawSquare(x, y, S, characters[zero[k]]);
                                x += HGAP;
                                byte[] substrate = Helper.NonZeroPositions(field.substrate);
                                for (int k = 0; k < substrate.Length; k++, x += S) drawSquare(x, y, S, characters[substrate[k]]);
                            }

                            x = level * HINDENT;
                            y += fonts[0].FY;
                        }
                    }
                    y += VSKIP;
                }
                else if (node is PathNode path)
                {
                    // Draw PathNode
                    int VSHIFT = (fonts[0].FY - FONTSHIFT - S) / 2;
                    if (!DENSE)
                    {
                        // Verbose layout
                        x += write("path from ", x, y, NODECOLOR);
                        byte[] start = Helper.NonZeroPositions(path.start);
                        for (int k = 0; k < start.Length; k++, x += S) drawSquare(x, y + VSHIFT, S, characters[start[k]]);

                        x += write(" to ", x, y, NODECOLOR);
                        byte[] finish = Helper.NonZeroPositions(path.finish);
                        for (int k = 0; k < finish.Length; k++, x += S) drawSquare(x, y + VSHIFT, S, characters[finish[k]]);

                        x += write(" on ", x, y, NODECOLOR);
                        byte[] on = Helper.NonZeroPositions(path.substrate);
                        for (int k = 0; k < on.Length; k++, x += S) drawSquare(x, y + VSHIFT, S, characters[on[k]]);

                        x += write(" colored ", x, y, NODECOLOR);
                        drawSquare(x, y + VSHIFT, S, characters[path.value]);
                        y += fonts[0].FY + VSKIP;
                    }
                    else
                    {
                        // Compact layout
                        x += write("path ", x, y, NODECOLOR);
                        byte[] start = Helper.NonZeroPositions(path.start);
                        for (int k = 0; k < start.Length; k++, x += S) drawSquare(x, y + VSHIFT, S, characters[start[k]]);
                        x += HGAP;
                        byte[] finish = Helper.NonZeroPositions(path.finish);
                        for (int k = 0; k < finish.Length; k++, x += S) drawSquare(x, y + VSHIFT, S, characters[finish[k]]);
                        x += HGAP;
                        byte[] on = Helper.NonZeroPositions(path.substrate);
                        for (int k = 0; k < on.Length; k++, x += S) drawSquare(x, y + VSHIFT, S, characters[on[k]]);
                        x += HGAP;
                        drawSquare(x, y + VSHIFT, S, characters[path.value]);
                        y += fonts[0].FY + VSKIP;
                    }
                }
                else if (node is ConvolutionNode convnode)
                {
                    // Draw ConvolutionNode
                    string s = "convolution";
                    if (convnode.steps > 0) s += $" {convnode.counter}/{convnode.steps}";
                    write(s, x, y, NODECOLOR);
                    y += fonts[0].FY + VSKIP;
                }
                else if (node is ConvChainNode chainnode)
                {
                    // Draw ConvChainNode
                    x += write($"convchain ", x, y, NODECOLOR);
                    drawSample(x, y, chainnode.sample, chainnode.SMX, chainnode.SMY, chainnode.c0, chainnode.c1, characters, 7);
                    y += fonts[0].FY + VSKIP;
                }
            }
        };

        // Draw connecting lines for branches
        void drawLine(Branch branch)
        {
            if (branch.nodes.Length == 0) return;
            int childLevel = lh[branch.nodes[0]].level;
            drawBracket(branch, childLevel - 1, branch.nodes.Length - 1, false);
            foreach (Node child in branch.nodes) if (child is Branch childBranch) drawLine(childBranch);
        };

        // Main drawing sequence
        draw(root, 0);
        drawLine(root);

        // Highlight active branch path
        for (Branch b = current; b != null; b = b.parent)
        {
            if (b.n >= 0)
            {
                drawDash(b.nodes[b.n], b is MarkovNode, true);
                drawBracket(b, lh[b.nodes[0]].level - 1, b.n, true);
            }
        }
    }

    // Check if a rule is active or if any of its symmetry variants are active
    static bool IsActive(RuleNode node, int index)
    {
        if (node.last[index]) return true;
        for (int r = index + 1; r < node.rules.Length; r++)
        {
            Rule rule = node.rules[r];
            if (rule.original) break;
            if (node.last[r]) return true;
        }
        return false;
    }
}

/*
=== SUMMARY ===

The GUI class provides visualization for the Wave Function Collapse (WFC) algorithm's execution. It's essentially a custom rendering engine that displays the algorithm's state in a human-readable form.

Think of it like a visual debugger that shows:

1. The hierarchical structure of execution nodes:
   - Different node types (WFC, Map, Path, etc.) with their specific configurations
   - Parent-child relationships between nodes drawn with brackets and connecting lines
   - The currently active execution path highlighted

2. Pattern representations and rules:
   - Input patterns and their corresponding output patterns
   - Rule transformations shown with arrows connecting patterns
   - Special visualization for different node types (paths, fields, etc.)

3. Execution state information:
   - Progress counters showing current step vs. total steps
   - Active/inactive status using color highlighting
   - Different levels of detail based on configuration (dense/verbose modes)

The class uses primitive drawing operations (rectangles, lines) and bitmap fonts to create a comprehensive visualization that shows both the algorithm's structure and its current state. This visualization is particularly valuable for understanding the complex behavior of the WFC algorithm and debugging issues during development.

The rendering is highly configurable through settings.xml, allowing customization of sizes, spacing, colors, and layout options to suit different needs and preferences.
*/