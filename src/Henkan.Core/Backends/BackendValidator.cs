using Henkan.Core.Expressions;
using Henkan.Core.Options;
using Henkan.Core.Templating;

namespace Henkan.Core.Backends;

/// <summary>How serious a validation message is.</summary>
public enum ValidationSeverity
{
    /// <summary>Worth fixing, but the definition will still load and run.</summary>
    Warning,

    /// <summary>The definition is unusable and is rejected.</summary>
    Error,
}

/// <param name="Severity">Whether the definition is rejected or merely questionable.</param>
/// <param name="Path">Where the problem is, for example <c>targets[2].options[0].visibleWhen</c>.</param>
/// <param name="Message">Text shown to the user.</param>
public readonly record struct ValidationMessage(ValidationSeverity Severity, string Path, string Message)
{
    public override string ToString() => $"{this.Severity}: {this.Path}: {this.Message}";
}

/// <summary>
/// Checks a backend definition before it is used. This is what stands between a
/// hand-edited JSON file and a confusing failure halfway through a batch, and it
/// backs the live feedback in the backend editor.
/// </summary>
public static class BackendValidator
{
    public static IReadOnlyList<ValidationMessage> Validate(BackendDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var messages = new List<ValidationMessage>();

        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error, "id", "A backend needs an id."));
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error, "name", "A backend needs a name."));
        }

        if (definition.Kind == BackendKind.Process && definition.Executable is null)
        {
            messages.Add(new ValidationMessage(
                ValidationSeverity.Error,
                "executable",
                "A process backend must say which executable to run."));
        }

        if (definition.Executable is { } executable && string.IsNullOrWhiteSpace(executable.FileName))
        {
            messages.Add(new ValidationMessage(
                ValidationSeverity.Error,
                "executable.fileName",
                "The executable needs a file name."));
        }

        if (definition.Targets.Count == 0)
        {
            messages.Add(new ValidationMessage(
                ValidationSeverity.Warning,
                "targets",
                "This backend offers no conversions, so nothing will appear in the format list."));
        }

        ValidateOptions(definition.SharedOptions, "sharedOptions", messages);
        ValidateFragments(definition.ArgumentPrefix, "argumentPrefix", messages);
        ValidateFragments(definition.ArgumentSuffix, "argumentSuffix", messages);

        var targetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < definition.Targets.Count; i++)
        {
            ConversionTarget target = definition.Targets[i];
            string path = $"targets[{i}]";

            if (string.IsNullOrWhiteSpace(target.Id))
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Error, $"{path}.id", "A target needs an id."));
            }
            else if (!targetIds.Add(target.Id))
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    $"{path}.id",
                    $"Another target already uses the id \"{target.Id}\"."));
            }

            if (target.OutputIsDirectory)
            {
                // A folder has no extension to check.
            }
            else if (string.IsNullOrWhiteSpace(target.OutputExtension))
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    $"{path}.outputExtension",
                    "A target needs an output extension."));
            }
            else if (target.OutputExtension.StartsWith('.'))
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Warning,
                    $"{path}.outputExtension",
                    "Leave the leading dot off the output extension."));
            }

            ValidateOptions(target.Options, $"{path}.options", messages);
            ValidateFragments(target.Arguments, $"{path}.arguments", messages);

            // Shared and target options land in one namespace, so a collision
            // would silently shadow one of them in every template.
            var sharedIds = definition.SharedOptions.Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (OptionDescriptor option in target.Options.Where(o => sharedIds.Contains(o.Id)))
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    $"{path}.options",
                    $"\"{option.Id}\" is already a shared option of this backend."));
            }

            if (definition.Kind == BackendKind.Process && definition.GetArgumentTemplate(target).Count == 0)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    $"{path}.arguments",
                    "A process target needs at least one argument fragment."));
            }
        }

        return messages;
    }

    /// <summary>True when nothing is wrong enough to stop the definition being used.</summary>
    public static bool IsUsable(IEnumerable<ValidationMessage> messages) =>
        !messages.Any(m => m.Severity == ValidationSeverity.Error);

    private static void ValidateOptions(
        IReadOnlyList<OptionDescriptor> options,
        string path,
        List<ValidationMessage> messages)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < options.Count; i++)
        {
            OptionDescriptor option = options[i];
            string optionPath = $"{path}[{i}]";

            if (string.IsNullOrWhiteSpace(option.Id))
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Error, $"{optionPath}.id", "An option needs an id."));
                continue;
            }

            if (!ids.Add(option.Id))
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    $"{optionPath}.id",
                    $"Another option already uses the id \"{option.Id}\"."));
            }

            if (VariableContext.ReservedNames.Contains(option.Id, StringComparer.OrdinalIgnoreCase))
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    $"{optionPath}.id",
                    $"\"{option.Id}\" is a built-in template variable and cannot be an option id."));
            }

            if (option.Kind == OptionKind.Choice && option.Choices.Count == 0)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    $"{optionPath}.choices",
                    "A choice option needs at least one choice."));
            }

            if (option.Kind == OptionKind.Range && (option.Minimum is null || option.Maximum is null))
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    $"{optionPath}",
                    "A range option needs both a minimum and a maximum."));
            }

            if (option.Minimum is { } min && option.Maximum is { } max && min > max)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    $"{optionPath}",
                    $"The minimum ({min}) is above the maximum ({max})."));
            }

            if (!string.IsNullOrWhiteSpace(option.VisibleWhen)
                && !ConditionExpression.TryParse(option.VisibleWhen, out _, out string? error))
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    $"{optionPath}.visibleWhen",
                    error ?? "The condition could not be parsed."));
            }
        }
    }

    private static void ValidateFragments(
        IReadOnlyList<ArgumentFragment> fragments,
        string path,
        List<ValidationMessage> messages)
    {
        for (int i = 0; i < fragments.Count; i++)
        {
            ArgumentFragment fragment = fragments[i];
            string fragmentPath = $"{path}[{i}]";

            if (!string.IsNullOrWhiteSpace(fragment.When)
                && !ConditionExpression.TryParse(fragment.When, out _, out string? error))
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    $"{fragmentPath}.when",
                    error ?? "The condition could not be parsed."));
            }

            try
            {
                TemplateRenderer.Tokenize(fragment.Value);
            }
            catch (TemplateException ex)
            {
                messages.Add(new ValidationMessage(ValidationSeverity.Error, $"{fragmentPath}.value", ex.Message));
            }
        }
    }
}
