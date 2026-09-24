using Henkan.Core.Backends;
using Henkan.Core.Backends.Editing;
using Henkan.Core.Options;

namespace Henkan.Core.Tests;

/// <summary>
/// The editable mirror of a definition has to give back what it was handed. An
/// editor that quietly drops a field is the kind of fault nobody notices until
/// their backend stops working, so every shipped definition is round-tripped.
/// </summary>
public class BackendDraftTests
{
    public static IEnumerable<object[]> Definitions() =>
        BackendRegistry.LoadBuiltInDefinitions().Select(d => new object[] { d.Definition.Id, d.Definition });

    [Theory]
    [MemberData(nameof(Definitions))]
    public void ABuiltInSurvivesBeingEdited(string id, BackendDefinition definition)
    {
        BackendDefinition again = BackendDraft.From(definition).ToDefinition();

        Assert.Equal(definition.Id, again.Id);
        Assert.Equal(definition.Name, again.Name);
        Assert.Equal(definition.Description, again.Description);
        Assert.Equal(definition.Kind, again.Kind);
        Assert.Equal(definition.TimeoutSeconds, again.TimeoutSeconds);
        Assert.Equal(definition.SuccessExitCodes, again.SuccessExitCodes);
        Assert.Equal(definition.Targets.Count, again.Targets.Count);
        Assert.Equal(definition.SharedOptions.Count, again.SharedOptions.Count);
        Assert.Equal(definition.ArgumentPrefix.Count, again.ArgumentPrefix.Count);
        Assert.Equal(definition.ArgumentSuffix.Count, again.ArgumentSuffix.Count);

        Assert.Equal(definition.Executable?.FileName, again.Executable?.FileName);
        Assert.Equal(definition.Executable?.BundledPath, again.Executable?.BundledPath);
        Assert.Equal(definition.Executable?.SearchPaths ?? [], again.Executable?.SearchPaths ?? []);
        Assert.Equal(definition.Executable?.ProbeArguments ?? [], again.Executable?.ProbeArguments ?? []);
        Assert.Equal(definition.Executable?.VersionPattern, again.Executable?.VersionPattern);

        Assert.True(
            BackendValidator.IsUsable(BackendValidator.Validate(again)),
            $"{id} no longer validates after a round trip.");
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public void EveryTargetSurvivesIntact(string id, BackendDefinition definition)
    {
        BackendDefinition again = BackendDraft.From(definition).ToDefinition();

        for (int i = 0; i < definition.Targets.Count; i++)
        {
            ConversionTarget before = definition.Targets[i];
            ConversionTarget after = again.Targets[i];

            Assert.Equal(before.Id, after.Id);
            Assert.Equal(before.Label, after.Label);
            Assert.Equal(before.OutputExtension, after.OutputExtension);
            Assert.Equal(before.Category, after.Category);
            Assert.Equal(before.Description, after.Description);
            Assert.Equal(before.InputExtensions, after.InputExtensions);
            Assert.Equal(before.Operation, after.Operation);
            Assert.Equal(before.AcceptsFolders, after.AcceptsFolders);
            Assert.Equal(before.CombinesInputs, after.CombinesInputs);
            Assert.Equal(before.OutputIsDirectory, after.OutputIsDirectory);
            Assert.Equal(before.Options.Count, after.Options.Count);
            Assert.Equal(before.Arguments.Count, after.Arguments.Count);

            // Missing from the first version of the editor, which would have
            // saved every multi-step route with its steps quietly removed.
            Assert.Equal(before.Steps.Count, after.Steps.Count);

            for (int j = 0; j < before.Steps.Count; j++)
            {
                Assert.Equal(before.Steps[j].Targets, after.Steps[j].Targets);
                Assert.Equal(
                    (before.Steps[j].Options ?? new Dictionary<string, string>()).OrderBy(o => o.Key),
                    (after.Steps[j].Options ?? new Dictionary<string, string>()).OrderBy(o => o.Key));
            }

            foreach ((string key, string value) in before.Settings)
            {
                Assert.Equal(value, after.Setting(key));
            }

            for (int j = 0; j < before.Arguments.Count; j++)
            {
                Assert.Equal(before.Arguments[j].Value, after.Arguments[j].Value);
                Assert.Equal(before.Arguments[j].When, after.Arguments[j].When);
                Assert.Equal(before.Arguments[j].SplitAfterExpansion, after.Arguments[j].SplitAfterExpansion);
            }

            for (int j = 0; j < before.Options.Count; j++)
            {
                OptionDescriptor left = before.Options[j];
                OptionDescriptor right = after.Options[j];

                Assert.Equal(left.Id, right.Id);
                Assert.Equal(left.Kind, right.Kind);
                Assert.Equal(left.Default, right.Default);
                Assert.Equal(left.Minimum, right.Minimum);
                Assert.Equal(left.Maximum, right.Maximum);
                Assert.Equal(left.Step, right.Step);
                Assert.Equal(left.VisibleWhen, right.VisibleWhen);
                Assert.Equal(left.Advanced, right.Advanced);
                Assert.Equal(left.Choices.Count, right.Choices.Count);
                Assert.Equal(
                    left.Choices.Select(c => (c.Value, c.Label)),
                    right.Choices.Select(c => (c.Value, c.Label)));
            }
        }

        Assert.Equal(definition.Targets.Count, again.Targets.Count);
    }

    /// <summary>
    /// The whole definition, serialised, must come back byte for byte. The field
    /// by field checks above only cover what someone thought to list; this one
    /// fails for any field the editor forgets, which is how the progress patterns,
    /// the output handling and the pipeline steps were found missing.
    /// </summary>
    [Theory]
    [MemberData(nameof(Definitions))]
    public void NothingIsLostAnywhere(string id, BackendDefinition definition)
    {
        string before = BackendSerializer.Serialize(definition);
        string after = BackendSerializer.Serialize(BackendDraft.From(definition).ToDefinition());

        Assert.True(before == after, $"{id} changes when it passes through the editor:{Environment.NewLine}{FirstDifference(before, after)}");
    }

    private static string FirstDifference(string before, string after)
    {
        string[] left = before.Split('\n');
        string[] right = after.Split('\n');

        for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            string l = i < left.Length ? left[i] : "(end)";
            string r = i < right.Length ? right[i] : "(end)";

            if (l != r)
            {
                return $"line {i + 1}{Environment.NewLine}  was: {l.Trim()}{Environment.NewLine}  now: {r.Trim()}";
            }
        }

        return "(no difference)";
    }

    [Fact]
    public void ANewBackendIsUsableRatherThanBroken()
    {
        BackendDefinition definition = BackendDraft.CreateNew().ToDefinition();
        IReadOnlyList<ValidationMessage> messages = BackendValidator.Validate(definition);

        Assert.True(BackendValidator.IsUsable(messages), string.Join("; ", messages));
        Assert.NotEmpty(definition.Targets);
    }

    [Fact]
    public void EmptyRowsAreDroppedRatherThanSaved()
    {
        BackendDraft draft = BackendDraft.CreateNew();
        draft.Targets[0].Options.Add(new OptionDraft());
        draft.Targets[0].Arguments.Add(new FragmentDraft());
        draft.Targets.Add(new TargetDraft());

        BackendDefinition definition = draft.ToDefinition();

        Assert.Single(definition.Targets);
        Assert.Empty(definition.Targets[0].Options);
        Assert.Empty(definition.Targets[0].Arguments);
    }

    [Fact]
    public void WritingTheSameKindBackAnnouncesNothing()
    {
        // A combo box bound both ways writes the value it was just told about
        // straight back. If that announces a change again, the two feed each
        // other until the stack runs out, which is how the backend page died.
        var option = new OptionDraft { Kind = OptionKind.Choice };
        var backend = new BackendDraft { Kind = BackendKind.Archive };
        int announcements = 0;
        option.PropertyChanged += (_, _) => announcements++;
        backend.PropertyChanged += (_, _) => announcements++;

        option.KindName = "Choice";
        option.Kind = OptionKind.Choice;
        backend.KindName = "Archive";
        backend.Kind = BackendKind.Archive;

        Assert.Equal(0, announcements);
    }

    [Fact]
    public void SettingsAreKeptAsNameEqualsValueLines()
    {
        var target = new TargetDraft
        {
            Id = "x",
            Label = "X",
            OutputExtension = "pdf",
            Settings = "mode=saveas\nformat=17",
        };

        ConversionTarget converted = target.ToTarget();

        Assert.Equal("saveas", converted.Setting("mode"));
        Assert.Equal("17", converted.Setting("format"));
    }
}
