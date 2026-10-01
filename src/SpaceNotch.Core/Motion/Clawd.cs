namespace SpaceNotch.Core.Motion;

/// <summary>Humeur de Clawd, la mascotte de Claude Code, dans la notch.</summary>
public enum ClawdMood
{
    /// <summary>L'agent travaille : il tape du pied, regarde autour, l'étoile tourne.</summary>
    Thinking = 0,

    /// <summary>L'agent demande une autorisation : il sautille bras levés, un « ? » bleu clignote.</summary>
    Asking = 1,

    /// <summary>Terminé : double saut, poussière et étincelles vertes.</summary>
    Done = 2,

    /// <summary>Erreur, ou réponse attendue dans le terminal : accroupi, il secoue la tête, « ! » rouge.</summary>
    Error = 3
}

/// <summary>Rendu des pixels de Clawd, au choix dans les réglages.</summary>
public enum ClawdStyle
{
    /// <summary>A · fidèle : carrés pleins, comme dans le terminal. Le style par défaut.</summary>
    Faithful = 0,

    /// <summary>C · entre les deux : carrés aux coins arrondis, joint presque invisible.</summary>
    Soft = 1,

    /// <summary>B · matière SpaceNotch : pixels ronds et filigrane des pixels éteints.</summary>
    Notch = 2
}

/// <summary>Encre d'un pixel de la scène.</summary>
public enum ClawdInk
{
    /// <summary>Le corps : orange de Claude Code (#D77757).</summary>
    Body = 0,

    /// <summary>La question : bleu des autorisations (#B1B9F9).</summary>
    Ask = 1,

    /// <summary>La réussite : vert (#4EBA65).</summary>
    Ok = 2,

    /// <summary>L'erreur : rouge (#FF6B80).</summary>
    Error = 3,

    /// <summary>Les pensées : blanc.</summary>
    Thought = 4,

    /// <summary>La poussière d'un saut : gris.</summary>
    Dust = 5
}

/// <summary>Un pixel allumé de la scène.</summary>
public readonly record struct ClawdPixel(int X, int Y, ClawdInk Ink, double Alpha);

/// <summary>
/// Clawd, la mascotte de Claude Code, en pixels dans la notch (I4).
///
/// <para>
/// La silhouette est celle de la bannière du terminal (18 × 5 demi-caractères).
/// Dans le terminal, un de ces pixels est deux fois plus haut que large : chaque
/// rangée est donc doublée, pour 18 × 10 pixels carrés. Les yeux sont agrandis à
/// 2 × 2, lisibles en petit. Les poses (de face, regard à gauche, à droite, bras
/// levés) sont celles de Claude Code ; les séquences sont propres à la notch.
/// </para>
///
/// <para>
/// La scène fait <see cref="Width"/> × <see cref="Height"/> : Clawd au milieu,
/// de la place au-dessus pour sauter et pour l'étoile, le « ? » ou le « ! ».
/// Rien ne dépend d'une horloge : une image se calcule à partir du temps écoulé,
/// ce qui se teste.
/// </para>
/// </summary>
public static class Clawd
{
    /// <summary>Largeur de la scène, en pixels.</summary>
    public const int Width = 26;

    /// <summary>Hauteur de la scène, en pixels.</summary>
    public const int Height = 18;

    /// <summary>Coin haut-gauche de Clawd au repos.</summary>
    public const int BodyX = 4;

    public const int BodyY = 6;

    /// <summary>Largeur et hauteur de Clawd lui-même.</summary>
    public const int BodyWidth = 18;

    public const int BodyHeight = 10;

    /// <summary>Partie de la scène montrée dans la notch : Clawd et ce qui l'entoure de près.</summary>
    public static (int X, int Y, int Width, int Height) Crop { get; } = (1, 0, 24, 17);

    /// <summary>Images par seconde suffisantes pour les gestes les plus vifs.</summary>
    public const int FramesPerSecond = 16;

    private static readonly string[] SolidFront =
        ["...xxxxxxxxxxxxx..", "...xxxxxxxxxxxxx..", ".xxxxxxxxxxxxxxxxx", "...xxxxxxxxxxxxx..", "...x.x.......x.x.."];

    private static readonly string[] SolidArmsUp =
        ["...xxxxxxxxxxxxx..", ".xxxxxxxxxxxxxxxxx", "..xxxxxxxxxxxxxxx.", "...xxxxxxxxxxxxx..", "...x.x.......x.x.."];

    private static readonly int[] LegColumns = [3, 5, 13, 15];

    private enum Pose
    {
        Front,
        Left,
        Right,
        Up,
        Blink,
        Happy,
        Down
    }

    private enum Legs
    {
        Down,
        TapLeft,
        TapRight,
        Tucked
    }

    private static readonly Dictionary<Pose, bool[,]> Poses = BuildPoses();

    private static readonly string[][] Stars =
    [
        ["x"],
        [".x.", "xxx", ".x."],
        ["x.x", ".x.", "x.x"],
        ["..x..", ".xxx.", "xxxxx", ".xxx.", "..x.."],
        ["x.x.x", ".xxx.", "xxxxx", ".xxx.", "x.x.x"]
    ];

    private static readonly string[] Question = [".xx.", "x..x", "...x", "..x.", ".x..", "....", ".x.."];
    private static readonly string[] Bang = ["xx", "xx", "xx", "xx", "..", "xx"];
    private static readonly string[] Spark = [".x.", "xxx", ".x."];

    /// <summary>Vrai si le corps de Clawd au repos, de face, allume ce pixel (coordonnées de Clawd).</summary>
    public static bool FrontPixel(int column, int row) => Poses[Pose.Front][row, column];

    /// <summary>L'image de <paramref name="mood"/> au temps <paramref name="seconds"/>.</summary>
    public static IReadOnlyList<ClawdPixel> Frame(ClawdMood mood, double seconds)
    {
        double t = Math.Max(0, seconds);
        var scene = new Scene();

        switch (mood)
        {
            case ClawdMood.Asking:
                Asking(scene, t);
                break;
            case ClawdMood.Done:
                Done(scene, t);
                break;
            case ClawdMood.Error:
                Error(scene, t);
                break;
            default:
                Thinking(scene, t);
                break;
        }

        return scene.Pixels;
    }

    private static void Thinking(Scene scene, double t)
    {
        double c = t % 2.6;
        Pose pose = Pose.Front;
        Legs legs = (int)(t / 0.16) % 2 == 1 ? Legs.TapLeft : Legs.TapRight;

        if (c > 1.3 && c <= 1.85)
        {
            pose = Pose.Right;
            legs = Legs.Down;
        }
        else if (c > 1.85 && c <= 2.4)
        {
            pose = Pose.Left;
            legs = Legs.Down;
        }
        else if (c > 2.4 && c <= 2.52)
        {
            pose = Pose.Blink;
            legs = Legs.Down;
        }

        scene.Body(pose, legs, 0, 0);

        int[] order = [0, 1, 2, 3, 4, 3, 2, 1];
        string[] star = Stars[order[(int)(t / 0.11) % order.Length]];
        scene.Mask(star, 22 - (star.Length / 2), 3 - (star.Length / 2), ClawdInk.Body, 1);

        for (int i = 0; i < 3; i++)
        {
            double p = ((t / 1.5) + (i / 3.0)) % 1;
            scene.Put(13 + (i % 2), (int)Math.Round(5 - (p * 5)), ClawdInk.Thought, 1 - p);
        }
    }

    private static void Asking(Scene scene, double t)
    {
        double c = t % 1.3;
        Pose pose = Pose.Up;
        Legs legs = Legs.Down;
        int dy = 0;

        if (c < 0.14)
        {
            pose = Pose.Front;
            dy = 1;
            legs = Legs.Tucked;
            scene.Poof(1, wide: false);
        }
        else if (c < 0.3)
        {
            dy = -3;
            legs = Legs.Tucked;
        }
        else if (c < 0.44)
        {
            dy = -4;
            legs = Legs.Tucked;
        }
        else if (c < 0.56)
        {
            dy = -2;
        }
        else if (c >= 0.7)
        {
            // Il agite les bras : « par ici ».
            pose = (int)(c / 0.15) % 2 == 1 ? Pose.Up : Pose.Front;
        }

        scene.Body(pose, legs, 0, dy);

        if (t % 0.8 < 0.56)
        {
            scene.Mask(Question, 21, 1, ClawdInk.Ask, 1);
        }
    }

    private static void Done(Scene scene, double t)
    {
        double c = t % 1.8;
        Pose pose = Pose.Happy;
        Legs legs = Legs.Down;
        int dy = 0;

        void Jump(double s)
        {
            if (s < 0.1)
            {
                pose = Pose.Front;
                dy = 1;
                legs = Legs.Tucked;
                scene.Poof(1, wide: false);
            }
            else if (s < 0.2)
            {
                pose = Pose.Front;
                dy = 1;
                legs = Legs.Tucked;
                scene.Poof(1, wide: true);
            }
            else if (s < 0.34)
            {
                dy = -3;
                legs = Legs.Tucked;
            }
            else if (s < 0.46)
            {
                dy = -5;
                legs = Legs.Tucked;
            }
            else if (s < 0.56)
            {
                dy = -2;
            }
            else
            {
                pose = Pose.Front;
            }
        }

        if (c < 0.65)
        {
            Jump(c);
        }
        else if (c < 1.3)
        {
            Jump(c - 0.65);
        }
        else if (c < 1.5)
        {
            dy = 1;
            legs = Legs.Tucked;
        }
        else
        {
            pose = (int)(c / 0.1) % 2 == 1 ? Pose.Left : Pose.Right;
        }

        scene.Body(pose, legs, 0, dy);

        if (dy < 0 || c > 1.3)
        {
            (int X, int Y)[] spots = [(1, 3), (22, 2), (0, 9), (23, 8), (3, 0), (20, 12), (1, 14)];
            int k = (int)(t / 0.12);

            for (int i = 0; i < 3; i++)
            {
                (int x, int y) = spots[(k + (i * 2)) % spots.Length];
                scene.Mask(Spark, x, y, ClawdInk.Ok, (k + i) % 3 == 0 ? 0.45 : 1);
            }
        }
    }

    private static void Error(Scene scene, double t)
    {
        double c = t % 2.2;
        Pose pose = Pose.Down;
        int dx = 0;

        if (c > 0.9 && c < 1.5)
        {
            // Il secoue la tête.
            bool right = (int)(c / 0.07) % 2 == 1;
            dx = right ? 1 : -1;
            pose = right ? Pose.Right : Pose.Left;
        }
        else if (c > 1.9 && c < 2.02)
        {
            pose = Pose.Blink;
        }

        scene.Body(pose, Legs.Tucked, dx, 1);

        if (t % 0.9 < 0.6)
        {
            scene.Mask(Bang, 22, 2, ClawdInk.Error, 1);
        }
    }

    private static Dictionary<Pose, bool[,]> BuildPoses()
    {
        // Yeux : coin haut-gauche (rangée, colonne) et hauteur (2 ouverts, 1 plissés).
        (Pose Pose, string[] Base, (int R, int C)[] Eyes, int H)[] specs =
        [
            (Pose.Front, SolidFront, [(2, 5), (2, 12)], 2),
            (Pose.Left, SolidFront, [(0, 4), (0, 11)], 2),
            (Pose.Right, SolidFront, [(0, 6), (0, 13)], 2),
            (Pose.Up, SolidArmsUp, [(2, 5), (2, 12)], 2),
            (Pose.Blink, SolidFront, [(3, 5), (3, 12)], 1),
            (Pose.Happy, SolidArmsUp, [(2, 5), (2, 12)], 1),
            (Pose.Down, SolidFront, [(4, 5), (4, 12)], 2)
        ];

        var poses = new Dictionary<Pose, bool[,]>();

        foreach ((Pose pose, string[] solid, (int R, int C)[] eyes, int h) in specs)
        {
            var cells = new bool[BodyHeight, BodyWidth];

            for (int row = 0; row < BodyHeight; row++)
            {
                for (int column = 0; column < BodyWidth; column++)
                {
                    cells[row, column] = solid[row / 2][column] == 'x';
                }
            }

            foreach ((int r, int c) in eyes)
            {
                for (int dy = 0; dy < h; dy++)
                {
                    cells[r + dy, c] = false;
                    cells[r + dy, c + 1] = false;
                }
            }

            poses[pose] = cells;
        }

        return poses;
    }

    /// <summary>La grille d'une image en cours de composition.</summary>
    private sealed class Scene
    {
        private readonly Dictionary<(int X, int Y), ClawdPixel> _cells = [];

        public IReadOnlyList<ClawdPixel> Pixels => [.. _cells.Values];

        public void Put(int x, int y, ClawdInk ink, double alpha)
        {
            if (x >= 0 && x < Width && y >= 0 && y < Height && alpha > 0)
            {
                _cells[(x, y)] = new ClawdPixel(x, y, ink, Math.Clamp(alpha, 0, 1));
            }
        }

        public void Mask(string[] mask, int x, int y, ClawdInk ink, double alpha)
        {
            for (int row = 0; row < mask.Length; row++)
            {
                for (int column = 0; column < mask[row].Length; column++)
                {
                    if (mask[row][column] == 'x')
                    {
                        Put(x + column, y + row, ink, alpha);
                    }
                }
            }
        }

        public void Body(Pose pose, Legs legs, int dx, int dy)
        {
            bool[,] cells = Poses[pose];

            for (int row = 0; row < BodyHeight - 2; row++)
            {
                for (int column = 0; column < BodyWidth; column++)
                {
                    if (cells[row, column])
                    {
                        Put(BodyX + column + dx, BodyY + row + dy, ClawdInk.Body, 1);
                    }
                }
            }

            // Pattes : deux rangées. Une patte levée perd celle du bas.
            for (int i = 0; i < LegColumns.Length; i++)
            {
                int x = BodyX + LegColumns[i] + dx;
                Put(x, BodyY + BodyHeight - 2 + dy, ClawdInk.Body, 1);

                bool lifted = legs == Legs.Tucked || (legs == Legs.TapLeft && i < 2) || (legs == Legs.TapRight && i >= 2);

                if (!lifted)
                {
                    Put(x, BodyY + BodyHeight - 1 + dy, ClawdInk.Body, 1);
                }
            }
        }

        /// <summary>Poussière d'un saut, au niveau des pattes, de chaque côté.</summary>
        public void Poof(int dy, bool wide)
        {
            int y = BodyY + BodyHeight - 1 + dy;
            string[] mask = wide ? ["x.", ".x"] : ["x"];
            int top = y - (mask.Length - 1);
            Mask(mask, BodyX - 2, top, ClawdInk.Dust, 0.8);
            Mask(mask, BodyX + BodyWidth + 1, top, ClawdInk.Dust, 0.8);
        }
    }
}
