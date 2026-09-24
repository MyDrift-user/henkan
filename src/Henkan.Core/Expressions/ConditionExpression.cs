using System.Globalization;
using System.Text;

namespace Henkan.Core.Expressions;

/// <summary>
/// A small boolean expression over option values, used for <c>visibleWhen</c> on
/// options and <c>when</c> on argument fragments.
/// </summary>
/// <remarks>
/// <para>Grammar, loosest to tightest binding:</para>
/// <code>
/// expression  := orExpr
/// orExpr      := andExpr ( '||' andExpr )*
/// andExpr     := unary  ( '&amp;&amp;' unary )*
/// unary       := '!' unary | comparison
/// comparison  := primary ( ( '==' | '!=' | '&lt;' | '&lt;=' | '&gt;' | '&gt;=' ) primary )?
/// primary     := '(' expression ')' | number | string | identifier
/// </code>
/// <para>
/// Every value is a string. Comparisons use numeric ordering when both sides
/// parse as numbers and case-insensitive ordinal ordering otherwise. A bare
/// value is truthy unless it is empty or one of <c>false</c>, <c>0</c>,
/// <c>no</c>, <c>off</c>.
/// </para>
/// </remarks>
public sealed class ConditionExpression
{
    private readonly Node root;
    private readonly HashSet<string> referenced;

    private ConditionExpression(Node root, HashSet<string> referenced)
    {
        this.root = root;
        this.referenced = referenced;
    }

    /// <summary>Option identifiers this expression reads, for change tracking in the UI.</summary>
    public IReadOnlyCollection<string> ReferencedNames => this.referenced;

    /// <summary>Parses <paramref name="text"/> or throws <see cref="ExpressionException"/>.</summary>
    public static ConditionExpression Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parser = new Parser(Lexer.Tokenize(text), referenced);
        Node parsed = parser.ParseExpression();
        parser.ExpectEnd();
        return new ConditionExpression(parsed, referenced);
    }

    /// <summary>Non-throwing counterpart of <see cref="Parse"/>, for validating user input.</summary>
    public static bool TryParse(string? text, out ConditionExpression? expression, out string? error)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            expression = null;
            error = "The condition is empty.";
            return false;
        }

        try
        {
            expression = Parse(text);
            error = null;
            return true;
        }
        catch (ExpressionException ex)
        {
            expression = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Evaluates the expression. <paramref name="lookup"/> returns the value of an
    /// option, or null when it is not set. Unknown names evaluate to the empty
    /// string, which is falsy, so a condition never throws because of a typo.
    /// </summary>
    public bool Evaluate(Func<string, string?> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        return this.root.Evaluate(lookup).AsBool();
    }

    private readonly record struct Val(string Text)
    {
        public static Val FromBool(bool value) => new(value ? "true" : "false");

        public bool AsBool()
        {
            if (this.Text.Length == 0)
            {
                return false;
            }

            return !(this.Text.Equals("false", StringComparison.OrdinalIgnoreCase)
                  || this.Text.Equals("0", StringComparison.Ordinal)
                  || this.Text.Equals("no", StringComparison.OrdinalIgnoreCase)
                  || this.Text.Equals("off", StringComparison.OrdinalIgnoreCase));
        }

        public bool TryAsNumber(out double value) =>
            double.TryParse(this.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private abstract class Node
    {
        public abstract Val Evaluate(Func<string, string?> lookup);
    }

    private sealed class LiteralNode(string text) : Node
    {
        public override Val Evaluate(Func<string, string?> lookup) => new(text);
    }

    private sealed class IdentifierNode(string name) : Node
    {
        public override Val Evaluate(Func<string, string?> lookup) => new(lookup(name) ?? string.Empty);
    }

    private sealed class NotNode(Node inner) : Node
    {
        public override Val Evaluate(Func<string, string?> lookup) =>
            Val.FromBool(!inner.Evaluate(lookup).AsBool());
    }

    private sealed class AndNode(Node left, Node right) : Node
    {
        public override Val Evaluate(Func<string, string?> lookup) =>
            Val.FromBool(left.Evaluate(lookup).AsBool() && right.Evaluate(lookup).AsBool());
    }

    private sealed class OrNode(Node left, Node right) : Node
    {
        public override Val Evaluate(Func<string, string?> lookup) =>
            Val.FromBool(left.Evaluate(lookup).AsBool() || right.Evaluate(lookup).AsBool());
    }

    private sealed class CompareNode(Node left, TokenKind op, Node right) : Node
    {
        public override Val Evaluate(Func<string, string?> lookup)
        {
            Val a = left.Evaluate(lookup);
            Val b = right.Evaluate(lookup);

            int order;
            if (a.TryAsNumber(out double x) && b.TryAsNumber(out double y))
            {
                order = x.CompareTo(y);
            }
            else
            {
                order = string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase);
            }

            bool result = op switch
            {
                TokenKind.Equal => order == 0,
                TokenKind.NotEqual => order != 0,
                TokenKind.Less => order < 0,
                TokenKind.LessOrEqual => order <= 0,
                TokenKind.Greater => order > 0,
                TokenKind.GreaterOrEqual => order >= 0,
                _ => throw new ExpressionException($"'{op}' is not a comparison operator."),
            };

            return Val.FromBool(result);
        }
    }

    private enum TokenKind
    {
        Identifier,
        String,
        Number,
        LeftParen,
        RightParen,
        Not,
        And,
        Or,
        Equal,
        NotEqual,
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual,
        End,
    }

    private readonly record struct Token(TokenKind Kind, string Text, int Position);

    private static class Lexer
    {
        private const char SingleQuote = '\'';
        private const char DoubleQuote = '"';

        public static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            int i = 0;

            while (i < text.Length)
            {
                char c = text[i];

                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                int start = i;

                if (c == '(')
                {
                    tokens.Add(new Token(TokenKind.LeftParen, "(", start));
                    i++;
                    continue;
                }

                if (c == ')')
                {
                    tokens.Add(new Token(TokenKind.RightParen, ")", start));
                    i++;
                    continue;
                }

                if (c == '&')
                {
                    Expect(text, i + 1, '&', "&&");
                    tokens.Add(new Token(TokenKind.And, "&&", start));
                    i += 2;
                    continue;
                }

                if (c == '|')
                {
                    Expect(text, i + 1, '|', "||");
                    tokens.Add(new Token(TokenKind.Or, "||", start));
                    i += 2;
                    continue;
                }

                if (c == '!')
                {
                    if (i + 1 < text.Length && text[i + 1] == '=')
                    {
                        tokens.Add(new Token(TokenKind.NotEqual, "!=", start));
                        i += 2;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenKind.Not, "!", start));
                        i++;
                    }

                    continue;
                }

                if (c == '=')
                {
                    Expect(text, i + 1, '=', "==");
                    tokens.Add(new Token(TokenKind.Equal, "==", start));
                    i += 2;
                    continue;
                }

                if (c == '<' || c == '>')
                {
                    bool orEqual = i + 1 < text.Length && text[i + 1] == '=';
                    TokenKind relational = (c, orEqual) switch
                    {
                        ('<', false) => TokenKind.Less,
                        ('<', true) => TokenKind.LessOrEqual,
                        ('>', false) => TokenKind.Greater,
                        _ => TokenKind.GreaterOrEqual,
                    };

                    tokens.Add(new Token(relational, orEqual ? $"{c}=" : c.ToString(), start));
                    i += orEqual ? 2 : 1;
                    continue;
                }

                if (c == SingleQuote || c == DoubleQuote)
                {
                    i = ReadString(text, i, c, out string literal);
                    tokens.Add(new Token(TokenKind.String, literal, start));
                    continue;
                }

                if (char.IsDigit(c) || (c == '-' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
                {
                    i++;
                    while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.'))
                    {
                        i++;
                    }

                    tokens.Add(new Token(TokenKind.Number, text[start..i], start));
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    i++;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '.'))
                    {
                        i++;
                    }

                    tokens.Add(new Token(TokenKind.Identifier, text[start..i], start));
                    continue;
                }

                throw new ExpressionException($"Unexpected character '{c}' at position {i}.");
            }

            tokens.Add(new Token(TokenKind.End, string.Empty, text.Length));
            return tokens;
        }

        private static void Expect(string text, int index, char expected, string operatorText)
        {
            if (index >= text.Length || text[index] != expected)
            {
                throw new ExpressionException($"Expected '{operatorText}' at position {index - 1}.");
            }
        }

        private static int ReadString(string text, int start, char quote, out string literal)
        {
            var builder = new StringBuilder();
            int i = start + 1;

            while (i < text.Length && text[i] != quote)
            {
                if (text[i] == '\\' && i + 1 < text.Length)
                {
                    builder.Append(text[i + 1]);
                    i += 2;
                    continue;
                }

                builder.Append(text[i]);
                i++;
            }

            if (i >= text.Length)
            {
                throw new ExpressionException($"Unterminated string starting at position {start}.");
            }

            literal = builder.ToString();
            return i + 1;
        }
    }

    private sealed class Parser(List<Token> tokens, HashSet<string> referenced)
    {
        private int position;

        private Token Current => tokens[this.position];

        public Node ParseExpression() => this.ParseOr();

        public void ExpectEnd()
        {
            if (this.Current.Kind != TokenKind.End)
            {
                throw new ExpressionException(
                    $"Unexpected '{this.Current.Text}' at position {this.Current.Position}.");
            }
        }

        private Node ParseOr()
        {
            Node node = this.ParseAnd();
            while (this.Current.Kind == TokenKind.Or)
            {
                this.position++;
                node = new OrNode(node, this.ParseAnd());
            }

            return node;
        }

        private Node ParseAnd()
        {
            Node node = this.ParseUnary();
            while (this.Current.Kind == TokenKind.And)
            {
                this.position++;
                node = new AndNode(node, this.ParseUnary());
            }

            return node;
        }

        private Node ParseUnary()
        {
            if (this.Current.Kind == TokenKind.Not)
            {
                this.position++;
                return new NotNode(this.ParseUnary());
            }

            return this.ParseComparison();
        }

        private Node ParseComparison()
        {
            Node left = this.ParsePrimary();

            if (this.Current.Kind is TokenKind.Equal or TokenKind.NotEqual or TokenKind.Less
                or TokenKind.LessOrEqual or TokenKind.Greater or TokenKind.GreaterOrEqual)
            {
                TokenKind op = this.Current.Kind;
                this.position++;
                return new CompareNode(left, op, this.ParsePrimary());
            }

            return left;
        }

        private Node ParsePrimary()
        {
            Token token = this.Current;

            switch (token.Kind)
            {
                case TokenKind.LeftParen:
                    this.position++;
                    Node inner = this.ParseOr();
                    if (this.Current.Kind != TokenKind.RightParen)
                    {
                        throw new ExpressionException($"Expected ')' at position {this.Current.Position}.");
                    }

                    this.position++;
                    return inner;

                case TokenKind.String:
                case TokenKind.Number:
                    this.position++;
                    return new LiteralNode(token.Text);

                case TokenKind.Identifier:
                    this.position++;
                    if (token.Text.Equals("true", StringComparison.OrdinalIgnoreCase)
                        || token.Text.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        return new LiteralNode(token.Text.ToLowerInvariant());
                    }

                    referenced.Add(token.Text);
                    return new IdentifierNode(token.Text);

                default:
                    throw new ExpressionException(
                        token.Kind == TokenKind.End
                            ? "The condition ended unexpectedly."
                            : $"Unexpected '{token.Text}' at position {token.Position}.");
            }
        }
    }
}
