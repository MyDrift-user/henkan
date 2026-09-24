using System.Collections.Concurrent;
using System.Text;
using Henkan.Core.Expressions;

namespace Henkan.Core.Templating;

/// <summary>
/// Turns argument templates into an argument list.
/// </summary>
/// <remarks>
/// <para>
/// Quoting is deliberately resolved <em>before</em> substitution. A template is
/// first split into tokens, honouring double quotes as grouping syntax, and each
/// token is then expanded on its own. That means <c>-i "{input}"</c> always
/// yields exactly two arguments no matter what the path contains, and there is
/// no way for a file name to inject an extra argument. The tokens are handed to
/// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>, which does its
/// own escaping, so no quotes survive into the child process command line.
/// </para>
/// <para>
/// Write <c>{{</c> and <c>}}</c> for a literal brace.
/// </para>
/// </remarks>
public static class TemplateRenderer
{
    private static readonly ConcurrentDictionary<string, ConditionExpression> ConditionCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Expands a template into a single string. Used for output path templates
    /// and anywhere a whole value rather than an argument list is wanted.
    /// </summary>
    public static string RenderText(string template, Func<string, string?> lookup)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(lookup);

        var builder = new StringBuilder(template.Length);
        int i = 0;

        while (i < template.Length)
        {
            char c = template[i];

            if (c == '{')
            {
                if (i + 1 < template.Length && template[i + 1] == '{')
                {
                    builder.Append('{');
                    i += 2;
                    continue;
                }

                int close = template.IndexOf('}', i + 1);
                if (close < 0)
                {
                    throw new TemplateException($"Unclosed '{{' at position {i} in \"{template}\".");
                }

                string name = template[(i + 1)..close].Trim();
                if (name.Length == 0)
                {
                    throw new TemplateException($"Empty placeholder at position {i} in \"{template}\".");
                }

                builder.Append(lookup(name) ?? string.Empty);
                i = close + 1;
                continue;
            }

            if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                builder.Append('}');
                i += 2;
                continue;
            }

            builder.Append(c);
            i++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Renders the fragments whose conditions hold into a flat argument list.
    /// </summary>
    public static IReadOnlyList<string> RenderArguments(
        IEnumerable<ArgumentFragment> fragments,
        VariableContext context)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        ArgumentNullException.ThrowIfNull(context);

        var arguments = new List<string>();

        foreach (ArgumentFragment fragment in fragments)
        {
            if (!IsIncluded(fragment, context.Lookup))
            {
                continue;
            }

            if (fragment.SplitAfterExpansion)
            {
                string expanded = RenderText(fragment.Value, context.Lookup);
                arguments.AddRange(Tokenize(expanded));
                continue;
            }

            foreach (string token in Tokenize(fragment.Value))
            {
                // The whole selection, one argument per file, so a path with a
                // space in it is never split and nothing needs quoting.
                if (token.Equals("{inputs}", StringComparison.OrdinalIgnoreCase))
                {
                    arguments.AddRange(context.Inputs);
                    continue;
                }

                string value = RenderText(token, context.Lookup);

                // A token that expanded to nothing would otherwise be passed as
                // an empty argument, which most tools reject.
                if (value.Length > 0)
                {
                    arguments.Add(value);
                }
            }
        }

        return arguments;
    }

    /// <summary>Evaluates a fragment's condition, treating an absent one as true.</summary>
    public static bool IsIncluded(ArgumentFragment fragment, Func<string, string?> lookup)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        return string.IsNullOrWhiteSpace(fragment.When)
            || GetCondition(fragment.When).Evaluate(lookup);
    }

    /// <summary>Parses and caches a condition, since templates are re-evaluated on every keystroke.</summary>
    public static ConditionExpression GetCondition(string condition) =>
        ConditionCache.GetOrAdd(condition, ConditionExpression.Parse);

    /// <summary>
    /// Splits a template into argument tokens. Double quotes group, a doubled
    /// double quote inside a quoted section is a literal quote, and whitespace
    /// outside quotes separates.
    /// </summary>
    public static IReadOnlyList<string> Tokenize(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        bool hasToken = false;

        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < template.Length && template[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                hasToken = true;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }

                continue;
            }

            current.Append(c);
            hasToken = true;
        }

        if (inQuotes)
        {
            throw new TemplateException($"Unbalanced quote in \"{template}\".");
        }

        if (hasToken)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    /// <summary>
    /// Renders the argument list and joins it the way a shell would display it,
    /// for the command preview in the backend editor and for the job log.
    /// </summary>
    public static string RenderCommandLine(
        string executable,
        IEnumerable<ArgumentFragment> fragments,
        VariableContext context)
    {
        IReadOnlyList<string> arguments = RenderArguments(fragments, context);
        var builder = new StringBuilder();
        builder.Append(Quote(executable));

        foreach (string argument in arguments)
        {
            builder.Append(' ').Append(Quote(argument));
        }

        return builder.ToString();
    }

    private static string Quote(string value) =>
        value.Length == 0 || value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
}
