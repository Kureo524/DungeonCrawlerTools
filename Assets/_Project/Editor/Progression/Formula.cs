using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheDungeon.Editor.Progression
{
    /// <summary>Formule invalide : message pour un designer et position de l'erreur dans le texte.</summary>
    public sealed class FormulaException : Exception
    {
        public int Position { get; }

        public FormulaException(string message, int position) : base(message)
        {
            Position = position;
        }
    }

    /// <summary>
    /// Formule mathématique saisie par un designer, ex : « start * pow(end / start, t) ».
    /// Opérateurs + - * / % ^ et parenthèses ; variables et fonctions listées dans <see cref="Help"/>.
    /// </summary>
    public sealed class Formula
    {
        public const string Help =
            "Variables : n (niveau), N (dernier niveau), t (de 0 au niveau 1 à 1 au dernier niveau), start (valeur au niveau 1), end (valeur au dernier niveau), pi, e.\n" +
            "Fonctions : sqrt, abs, exp, ln, log, pow(a, b), min, max, floor, ceil, round, clamp(x, a, b), lerp(a, b, t), sin, cos.";

        static readonly HashSet<string> Variables = new() { "n", "N", "t", "start", "end" };

        static readonly Dictionary<string, (int arity, Func<double[], double> apply)> Functions = new()
        {
            ["sqrt"] = (1, a => Math.Sqrt(a[0])),
            ["abs"] = (1, a => Math.Abs(a[0])),
            ["exp"] = (1, a => Math.Exp(a[0])),
            ["ln"] = (1, a => Math.Log(a[0])),
            ["log"] = (1, a => Math.Log10(a[0])),
            ["floor"] = (1, a => Math.Floor(a[0])),
            ["ceil"] = (1, a => Math.Ceiling(a[0])),
            ["round"] = (1, a => Math.Round(a[0], MidpointRounding.AwayFromZero)),
            ["sin"] = (1, a => Math.Sin(a[0])),
            ["cos"] = (1, a => Math.Cos(a[0])),
            ["pow"] = (2, a => Math.Pow(a[0], a[1])),
            ["min"] = (2, a => Math.Min(a[0], a[1])),
            ["max"] = (2, a => Math.Max(a[0], a[1])),
            ["clamp"] = (3, a => Math.Clamp(a[0], Math.Min(a[1], a[2]), Math.Max(a[1], a[2]))),
            ["lerp"] = (3, a => a[0] + (a[1] - a[0]) * a[2])
        };

        readonly Func<IReadOnlyDictionary<string, double>, double> evaluate;

        public string Text { get; }

        Formula(string text, Func<IReadOnlyDictionary<string, double>, double> evaluate)
        {
            Text = text;
            this.evaluate = evaluate;
        }

        /// <summary>Analyse le texte. Lève <see cref="FormulaException"/> si la formule est invalide.</summary>
        public static Formula Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new FormulaException("La formule est vide.", 0);
            var parser = new Parser(text);
            var node = parser.Expression();
            parser.ExpectEnd();
            return new Formula(text, node);
        }

        public static bool TryParse(string text, out Formula formula, out string error)
        {
            try
            {
                formula = Parse(text);
                error = null;
                return true;
            }
            catch (FormulaException exception)
            {
                formula = null;
                error = exception.Message;
                return false;
            }
        }

        /// <summary>Valeur de la formule pour chaque niveau, de 1 à <paramref name="levelCount"/>.</summary>
        public double[] EvaluateLevels(int levelCount, double start, double end)
        {
            var values = new double[levelCount];
            var variables = new Dictionary<string, double> { ["N"] = levelCount, ["start"] = start, ["end"] = end };
            for (var i = 0; i < levelCount; i++)
            {
                variables["n"] = i + 1;
                variables["t"] = levelCount > 1 ? i / (double)(levelCount - 1) : 0d;
                values[i] = evaluate(variables);
            }
            return values;
        }

        /// <summary>Analyse descendante récursive ; chaque règle renvoie directement la fonction d'évaluation.</summary>
        sealed class Parser
        {
            readonly string text;
            int position;

            public Parser(string text)
            {
                this.text = text;
            }

            public void ExpectEnd()
            {
                SkipSpaces();
                if (position < text.Length)
                    throw new FormulaException($"« {text[position]} » inattendu.", position);
            }

            // expression := terme (('+' | '-') terme)*
            public Func<IReadOnlyDictionary<string, double>, double> Expression()
            {
                var left = Term();
                while (true)
                {
                    if (Accept('+'))
                    {
                        var (a, b) = (left, Term());
                        left = v => a(v) + b(v);
                    }
                    else if (Accept('-'))
                    {
                        var (a, b) = (left, Term());
                        left = v => a(v) - b(v);
                    }
                    else
                        return left;
                }
            }

            // terme := unaire (('*' | '/' | '%') unaire)*
            Func<IReadOnlyDictionary<string, double>, double> Term()
            {
                var left = Unary();
                while (true)
                {
                    if (Accept('*'))
                    {
                        var (a, b) = (left, Unary());
                        left = v => a(v) * b(v);
                    }
                    else if (Accept('/'))
                    {
                        var (a, b) = (left, Unary());
                        left = v => a(v) / b(v);
                    }
                    else if (Accept('%'))
                    {
                        var (a, b) = (left, Unary());
                        left = v => a(v) % b(v);
                    }
                    else
                        return left;
                }
            }

            // unaire := '-' unaire | '+' unaire | puissance   (-2^2 vaut -4, comme en mathématiques)
            Func<IReadOnlyDictionary<string, double>, double> Unary()
            {
                if (Accept('-'))
                {
                    var operand = Unary();
                    return v => -operand(v);
                }
                return Accept('+') ? Unary() : Power();
            }

            // puissance := primaire ('^' unaire)?   (associative à droite : 2^3^2 = 2^9)
            Func<IReadOnlyDictionary<string, double>, double> Power()
            {
                var basis = Primary();
                if (!Accept('^'))
                    return basis;
                var exponent = Unary();
                return v => Math.Pow(basis(v), exponent(v));
            }

            Func<IReadOnlyDictionary<string, double>, double> Primary()
            {
                SkipSpaces();
                if (position >= text.Length)
                    throw new FormulaException("La formule est incomplète.", position);

                var c = text[position];
                if (Accept('('))
                {
                    var inner = Expression();
                    Expect(')');
                    return inner;
                }
                if (char.IsDigit(c) || c == '.')
                    return Number();
                if (char.IsLetter(c) || c == '_')
                    return Identifier();
                throw new FormulaException($"« {c} » inattendu.", position);
            }

            Func<IReadOnlyDictionary<string, double>, double> Number()
            {
                var start = position;
                while (position < text.Length && (char.IsDigit(text[position]) || text[position] == '.'))
                    position++;
                var token = text.Substring(start, position - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    throw new FormulaException($"« {token} » n'est pas un nombre valide (utilisez le point comme séparateur décimal).", start);
                return _ => value;
            }

            Func<IReadOnlyDictionary<string, double>, double> Identifier()
            {
                var start = position;
                while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_'))
                    position++;
                var name = text.Substring(start, position - start);

                if (Accept('('))
                {
                    if (!Functions.TryGetValue(name.ToLowerInvariant(), out var function))
                        throw new FormulaException($"Fonction « {name} » inconnue.", start);
                    var arguments = new List<Func<IReadOnlyDictionary<string, double>, double>> { Expression() };
                    while (Accept(','))
                        arguments.Add(Expression());
                    Expect(')');
                    if (arguments.Count != function.arity)
                        throw new FormulaException($"« {name} » attend {function.arity} valeur(s), {arguments.Count} donnée(s).", start);
                    return v =>
                    {
                        var values = new double[arguments.Count];
                        for (var i = 0; i < values.Length; i++)
                            values[i] = arguments[i](v);
                        return function.apply(values);
                    };
                }

                switch (name)
                {
                    case "pi":
                        return _ => Math.PI;
                    case "e":
                        return _ => Math.E;
                }
                if (!Variables.Contains(name))
                    throw new FormulaException($"Variable « {name} » inconnue. Variables possibles : n, N, t, start, end.", start);
                return v => v[name];
            }

            void SkipSpaces()
            {
                while (position < text.Length && char.IsWhiteSpace(text[position]))
                    position++;
            }

            bool Accept(char c)
            {
                SkipSpaces();
                if (position >= text.Length || text[position] != c)
                    return false;
                position++;
                return true;
            }

            void Expect(char c)
            {
                if (!Accept(c))
                    throw new FormulaException($"« {c} » manquant.", position);
            }
        }
    }
}
