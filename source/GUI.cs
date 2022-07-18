// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

static class GUI
{
    static readonly int S, SMALL, MAXWIDTH, ZSHIFT, HINDENT, HGAP, HARROW, HLINE, VSKIP, SMALLVSKIP, FONTSHIFT, AFTERFONT;
    static readonly bool DENSE, D3;
    public static readonly int BACKGROUND, INACTIVE, ACTIVE;

    const string FONT = "Tamzen8x16r", TITLEFONT = "Tamzen8x16b";
    static readonly (bool[], int FX, int FY)[] fonts;

    static readonly char[] legend = "ABCDEFGHIJKLMNOPQRSTUVWXYZ 12345abcdefghijklmnopqrstuvwxyz\u03bb67890{}[]()<>$*-+=/#_%^@\\&|~?'\"`!,.;:".ToCharArray();
    static readonly Dictionary<char, byte> map;
    static GUI()
    {
        map = new Dictionary<char, byte>();
        for (int i = 0; i < legend.Length; i++) map.Add(legend[i], (byte)i);
        fonts = new (bool[], int, int)[2];

        (int[] bitmap, int width, int height, _) = Graphics.LoadBitmap($"resources/fonts/{FONT}.png");
        int b0 = bitmap[0];
        int b1 = bitmap[width - 1];
        fonts[0] = (bitmap.Select(argb => argb != b0 && argb != b1).ToArray(), width / 32, height / 3);
        
        (bitmap, width, height, _) = Graphics.LoadBitmap($"resources/fonts/{TITLEFONT}.png");
        b0 = bitmap[0];
        b1 = bitmap[width - 1];
        fonts[1] = (bitmap.Select(argb => argb != b0 && argb != b1).ToArray(), width / 32, height / 3);

        XElement settings = XDocument.Load("resources/settings.xml").Root;
        S = settings.Get("squareSize", 7);
        SMALL = settings.Get("smallSquareSize", 3);
        MAXWIDTH = settings.Get("maxwidth", 10);
        ZSHIFT = settings.Get("zshift", 2);
        HINDENT = settings.Get("hindent", 30);
        HGAP = settings.Get("hgap", 2);
        HARROW = settings.Get("harrow", 10);
        HLINE = settings.Get("hline", 14);
        VSKIP = settings.Get("vskip", 2);
        SMALLVSKIP = settings.Get("smallvskip", 2);
        FONTSHIFT = settings.Get("fontshift", 2);
        AFTERFONT = settings.Get("afterfont", 4);
        DENSE = settings.Get("dense", true);
        D3 = settings.Get("d3", true);
        BACKGROUND = (255 << 24) + Convert.ToInt32(settings.Get("background", "222222"), 16);
        INACTIVE = (255 << 24) + Convert.ToInt32(settings.Get("inactive", "666666"), 16);
        ACTIVE = (255 << 24) + Convert.ToInt32(settings.Get("active", "ffffff"), 16);
    }

    public static void Draw(string name, Branch root, Branch current, int[] bitmap, int WIDTH, int HEIGHT, Dictionary<char, int> palette)
    {
        void drawRectangle(int x, int y, int width, int height, int color)
        {
            if (y + height > HEIGHT) return;
            for (int dy = 0; dy < height; dy++) for (int dx = 0; dx < width; dx++) bitmap[x + dx + (y + dy) * WIDTH] = color;
        };
        void drawSquare(int x, int y, int S, char c) => drawRectangle(x, y, S, S, palette[c]);
        void drawShadedSquare(int x, int y, int S, int color)
        {
            drawRectangle(x, y, S, S, color);
            drawRectangle(x + S, y, 1, S + 1, BACKGROUND);
            drawRectangle(x, y + S, S + 1, 1, BACKGROUND);
        };
        void drawHLine(int x, int y, int length, int color, bool dashed = false)
        {
            if (length <= 0 || x < 0 || x + length >= WIDTH) return;
            if (!dashed) drawRectangle(x, y, length, 1, color);
            else
            {
                if (y >= HEIGHT) return;
                int shift = length % 4 == 0 ? 1 : 0;
                for (int dx = 0; dx < length; dx++) if ((dx + shift) / 2 % 2 == 0) bitmap[x + dx + y * WIDTH] = color;
            }
        }
        void drawVLine(int x, int y, int height, int color)
        {
            if (x < 0) return;
            int yend = Math.Min(y + height, HEIGHT);
            drawRectangle(x, y, 1, yend - y, color);
        }
        int write(string s, int x, int y, int color, int font = 0)
        {
            int fontshift = font == 0 ? FONTSHIFT : 0;
            var (f, FX, FY) = fonts[font];
            if (y - FONTSHIFT + FY >= HEIGHT) return -1;
            for (int i = 0; i < s.Length; i++)
            {
                int p = map[s[i]];
                int px = p % 32, py = p / 32;
                for (int dy = 0; dy < FY; dy++) for (int dx = 0; dx < FX; dx++)
                        if (f[px * FX + dx + (py * FY + dy) * FX * 32]) bitmap[x + i * FX + dx + (y + dy - fontshift) * WIDTH] = color;
            }
            return s.Length * FX;
        };

        Dictionary<Node, (int level, int height)> lh = new();
        void drawDash(Node node, bool markov, bool active)
        {
            if (node == root) return;
            (int level, int height) = lh[node];
            int extra = markov ? 3 : 1;
            drawHLine(level * HINDENT - HLINE - HGAP - extra, height + S / 2, (node is MarkovNode || node is SequenceNode ? HINDENT : HLINE) + extra, active ? ACTIVE : INACTIVE);
        };
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
        int drawArray(byte[] a, int x, int y, int MX, int MY, int MZ, char[] characters, int S)
        {
            for (int dz = 0; dz < MZ; dz++) for (int dy = 0; dy < MY; dy++) for (int dx = 0; dx < MX; dx++)
                    {
                        byte i = a[dx + dy * MX + dz * MX * MY];
                        int color = i != 0xff ? palette[characters[i]] : (D3 ? INACTIVE : BACKGROUND);
                        drawShadedSquare(x + dx * S + (MZ - dz - 1) * ZSHIFT, y + dy * S + (MZ - dz - 1) * ZSHIFT, S, color);
                    }
            return MX * S + (MZ - 1) * ZSHIFT;
        };
        void drawSample(int x, int y, bool[] sample, int MX, int MY, byte c0, byte c1, char[] characters, int S)
        {
            for (int dy = 0; dy < MY; dy++) for (int dx = 0; dx < MX; dx++)
                {
                    byte b = sample[dx + dy * MX] ? c1 : c0;
                    drawSquare(x + dx * S, y + dy * S, S, characters[b]);
                }
        };

        int y = fonts[1].FY / 2;
        write(name, 8, y, ACTIVE, 1);
        y += (int)(AFTERFONT * fonts[1].FY / 2);

        void draw(Node node, int level)
        {
            lh.Add(node, (level, y));
            int x = level * HINDENT;
            char[] characters = node.grid.characters;

            if (node is Branch branch)
            {
                int LINECOLOR = branch == current && branch.n < 0 ? ACTIVE : INACTIVE;
                if (branch is WFCNode wfcnode)
                {
                    write($"wfc {wfcnode.name}", x, y, LINECOLOR);
                    y += fonts[0].FY + VSKIP;
                }
                else if (branch is MapNode mapnode)
                {
                    for (int r = 0; r < mapnode.rules.Length; r++)
                    {
                        Rule rule = mapnode.rules[r];
                        if (!rule.original) continue;
                        int s = rule.IMX * rule.IMY > MAXWIDTH ? SMALL : S;

                        x += drawArray(rule.binput, x, y, rule.IMX, rule.IMY, rule.IMZ, characters, s) + HGAP;
                        drawHLine(x, y + S / 2, HARROW, LINECOLOR, true);
                        x += HARROW + HGAP;
                        x += drawArray(rule.output, x, y, rule.OMX, rule.OMY, rule.OMZ, mapnode.newgrid.characters, s) + HGAP;

                        y += Math.Max(rule.IMY, rule.OMY) * s + (Math.Max(rule.IMZ, rule.OMZ) - 1) * ZSHIFT + SMALLVSKIP;
                        x = level * HINDENT;
                    }
                    y += VSKIP;
                }

                bool markov = branch is MarkovNode;
                bool sequence = branch is SequenceNode;
                foreach (var child in branch.nodes)
                {
                    draw(child, markov || sequence ? level + 1 : level);
                    drawDash(child, markov, false);
                }
            }
            else
            {
                bool active = current != null && current.n >= 0 && current.nodes[current.n] == node;
                int NODECOLOR = active ? ACTIVE : INACTIVE;
                if (node is RuleNode rulenode)
                {
                    for (int r = 0; r < rulenode.rules.Length && r < 40; r++)
                    {
                        Rule rule = rulenode.rules[r];
                        if (!rule.original) continue;
                        int s = rule.IMX * rule.IMY > MAXWIDTH ? SMALL : S;

                        int LINECOLOR = (active && IsActive(rulenode, r)) ? ACTIVE : INACTIVE;
                        x += drawArray(rule.binput, x, y, rule.IMX, rule.IMY, rule.IMZ, characters, s) + HGAP;

                        drawHLine(x, y + S / 2, HARROW, LINECOLOR, rulenode is not OneNode);
                        x += HARROW + HGAP;
                        x += drawArray(rule.output, x, y, rule.OMX, rule.OMY, rule.OMZ, characters, s) + HGAP;

                        if (rulenode.steps > 0) write($" {rulenode.counter}/{rulenode.steps}", x, y, LINECOLOR);
                        y += rule.IMY * s + (rule.IMZ - 1) * ZSHIFT + SMALLVSKIP;
                        x = level * HINDENT;
                    }
                    if (rulenode.fields != null)
                    {
                        y += SMALLVSKIP;
                        for (int c = 0; c < rulenode.fields.Length; c++)
                        {
                            Field field = rulenode.fields[c];
                            if (field == null) continue;

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
                    int VSHIFT = (fonts[0].FY - FONTSHIFT - S) / 2;
                    if (!DENSE)
                    {
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
                    string s = "convolution";
                    if (convnode.steps > 0) s += $" {convnode.counter}/{convnode.steps}";
                    write(s, x, y, NODECOLOR);
                    y += fonts[0].FY + VSKIP;
                }
                else if (node is ConvChainNode chainnode)
                {
                    x += write($"convchain ", x, y, NODECOLOR);
                    drawSample(x, y, chainnode.sample, chainnode.SMX, chainnode.SMY, chainnode.c0, chainnode.c1, characters, 7);
                    y += fonts[0].FY + VSKIP;
                }
            }
        };

        void drawLine(Branch branch)
        {
            if (branch.nodes.Length == 0) return;
            int childLevel = lh[branch.nodes[0]].level;
            drawBracket(branch, childLevel - 1, branch.nodes.Length - 1, false);
            foreach (Node child in branch.nodes) if (child is Branch childBranch) drawLine(childBranch);
        };

        draw(root, 0);
        drawLine(root);
        for (Branch b = current; b != null; b = b.parent)
        {
            if (b.n >= 0)
            {
                drawDash(b.nodes[b.n], b is MarkovNode, true);
                drawBracket(b, lh[b.nodes[0]].level - 1, b.n, true);
            }
        }
    }

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
