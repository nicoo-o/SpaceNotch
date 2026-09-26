using System.Globalization;

namespace SpaceNotch.Core.Launcher;

/// <summary>
/// La calculatrice du champ de recherche : « 12*7+3 » donne 87 pendant qu'on
/// tape. Quatre opérations, puissance (^), pourcentage (%), parenthèses,
/// virgule ou point décimal, × et ÷. Rien d'autre : une chaîne qui n'est pas
/// clairement un calcul n'en est pas un, pour ne jamais masquer une recherche.
/// </summary>
public static class InlineCalculator
{
    /// <summary>
    /// Évalue <paramref name="text"/>. Faux si ce n'est pas un calcul : pas
    /// d'opérateur, un caractère inconnu, une division par zéro.
    /// </summary>
    public static bool TryEvaluate(string text, out double value)
    {
        value = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string expression = text.Trim()
            .Replace('×', '*')
            .Replace('÷', '/')
            .Replace(',', '.')
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        // Un nombre seul n'est pas un calcul ; il faut au moins un opérateur
        // entre deux termes.
        if (expression.IndexOfAny(['+', '*', '/', '^', '%', '-'], 1) < 0 && !expression.Contains('(', StringComparison.Ordinal))
        {
            return false;
        }

        var parser = new Parser(expression);

        try
        {
            double result = parser.ParseExpression();

            if (!parser.AtEnd || double.IsNaN(result) || double.IsInfinity(result))
            {
                return false;
            }

            value = result;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Résultat lisible : au plus dix décimales, sans zéros inutiles.</summary>
    public static string Format(double value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        double rounded = Math.Round(value, 10);
        return rounded.ToString("#,0.##########", culture);
    }

    /// <summary>Analyse descendante : expression → terme (+ −) → facteur (× ÷ %) → puissance → unaire.</summary>
    private sealed class Parser(string text)
    {
        private int _position;

        public bool AtEnd => _position >= text.Length;

        public double ParseExpression()
        {
            double value = ParseTerm();

            while (!AtEnd && (Peek() == '+' || Peek() == '-'))
            {
                char op = text[_position++];
                double right = ParseTerm();
                value = op == '+' ? value + right : value - right;
            }

            return value;
        }

        private double ParseTerm()
        {
            double value = ParsePower();

            while (!AtEnd && (Peek() == '*' || Peek() == '/' || Peek() == '%'))
            {
                char op = text[_position++];

                // « 20% » seul : un pourcentage, pas un modulo.
                if (op == '%' && (AtEnd || Peek() is ')' or '+' or '-' or '*' or '/'))
                {
                    value /= 100;
                    continue;
                }

                double right = ParsePower();

                value = op switch
                {
                    '*' => value * right,
                    '/' when right == 0 => throw new FormatException("Division par zéro."),
                    '/' => value / right,
                    _ when right == 0 => throw new FormatException("Modulo par zéro."),
                    _ => value % right
                };
            }

            return value;
        }

        private double ParsePower()
        {
            double value = ParseUnary();

            if (!AtEnd && Peek() == '^')
            {
                _position++;
                value = Math.Pow(value, ParsePower());
            }

            return value;
        }

        private double ParseUnary()
        {
            if (!AtEnd && (Peek() == '-' || Peek() == '+'))
            {
                char sign = text[_position++];
                double operand = ParseUnary();
                return sign == '-' ? -operand : operand;
            }

            if (!AtEnd && Peek() == '(')
            {
                _position++;
                double inner = ParseExpression();

                if (AtEnd || Peek() != ')')
                {
                    throw new FormatException("Parenthèse non fermée.");
                }

                _position++;
                return inner;
            }

            int start = _position;

            while (!AtEnd && (char.IsAsciiDigit(Peek()) || Peek() == '.'))
            {
                _position++;
            }

            if (start == _position)
            {
                throw new FormatException("Nombre attendu.");
            }

            return double.Parse(text.AsSpan(start, _position - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }

        private char Peek() => text[_position];
    }
}
