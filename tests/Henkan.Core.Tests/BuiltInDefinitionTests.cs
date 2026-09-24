using Henkan.Core.Backends;
using Henkan.Core.Options;
using Henkan.Core.Templating;

namespace Henkan.Core.Tests;

public class BuiltInDefinitionTests
{
    public static IEnumerable<object[]> Definitions() =>
        BackendRegistry.LoadBuiltInDefinitions().Select(d => new object[] { d.Definition.Id, d.Definition });

    [Fact]
    public void AllBuiltInsLoad()
    {
        var problems = new List<BackendLoadProblem>();
        var loaded = BackendRegistry.LoadBuiltInDefinitions(problems).ToList();

        Assert.Empty(problems);
        Assert.Equal(["ffmpeg", "ghostscript", "imagemagick", "libreoffice", "office", "pipeline", "sevenzip"], loaded.Select(d => d.Definition.Id).Order());
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public void BuiltInValidatesWithoutErrors(string id, BackendDefinition definition)
    {
        var messages = BackendValidator.Validate(definition);
        Assert.True(BackendValidator.IsUsable(messages), $"{id}: {string.Join("; ", messages)}");
        Assert.DoesNotContain(messages, m => m.Severity == ValidationSeverity.Warning);
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public void EveryTargetRendersWithDefaults(string id, BackendDefinition definition)
    {
        Assert.Equal(id, definition.Id);

        foreach (ConversionTarget target in definition.Targets)
        {
            IReadOnlyList<OptionDescriptor> schema = definition.GetOptionSchema(target);
            OptionValueSet values = OptionValueSet.FromSchema(schema);

            // Every option must be readable with its default in place, and every
            // condition in the definition must evaluate without throwing.
            foreach (OptionDescriptor option in schema)
            {
                _ = values.IsVisible(option);
            }

            if (definition.Kind != BackendKind.Process)
            {
                continue;
            }

            VariableContext context = VariableContext.CreateSample(target.OutputExtension, values);
            IReadOnlyList<string> arguments = TemplateRenderer.RenderArguments(definition.GetArgumentTemplate(target), context);

            Assert.NotEmpty(arguments);
            Assert.DoesNotContain(arguments, a => a.Contains('{') && a.Contains('}') && !a.Contains('"'));
            Assert.Contains(arguments, a => a.Contains("holiday clip", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void FfmpegMp3RendersExpectedCommandLine()
    {
        BackendDefinition ffmpeg = BackendRegistry.LoadBuiltInDefinitions().Single(d => d.Definition.Id == "ffmpeg").Definition;
        ConversionTarget mp3 = ffmpeg.FindTarget("mp3")!;

        OptionValueSet values = OptionValueSet.FromSchema(ffmpeg.GetOptionSchema(mp3), new Dictionary<string, string>
        {
            ["Mode"] = "VBR",
            ["Quality"] = "0",
            ["KeepMetadata"] = "false",
        });

        VariableContext context = VariableContext.Create(@"C:\in\song.wav", @"C:\in\song.mp3", @"C:\tmp", values);
        IReadOnlyList<string> arguments = TemplateRenderer.RenderArguments(ffmpeg.GetArgumentTemplate(mp3), context);

        Assert.Equal(
            ["-hide_banner", "-nostdin", "-y", "-i", @"C:\in\song.wav", "-vn", "-c:a", "libmp3lame", "-q:a", "0", "-map_metadata", "-1", @"C:\in\song.mp3"],
            arguments);
    }

    [Fact]
    public void LibreOfficePdfFilterOptionsRenderAsJson()
    {
        BackendDefinition soffice = BackendRegistry.LoadBuiltInDefinitions().Single(d => d.Definition.Id == "libreoffice").Definition;
        ConversionTarget pdf = soffice.FindTarget("pdf")!;

        OptionValueSet values = OptionValueSet.FromSchema(soffice.GetOptionSchema(pdf), new Dictionary<string, string> { ["PdfA"] = "true" });
        VariableContext context = VariableContext.CreateSample("pdf", values);
        IReadOnlyList<string> arguments = TemplateRenderer.RenderArguments(soffice.GetArgumentTemplate(pdf), context);

        Assert.Contains("pdf:writer_pdf_Export:{\"Quality\":90,\"SelectPdfVersion\":2}", arguments);
    }

    [Fact]
    public void RoundTripsThroughJson()
    {
        foreach ((_, BackendDefinition definition) in BackendRegistry.LoadBuiltInDefinitions())
        {
            string json = BackendSerializer.Serialize(definition);
            BackendDefinition again = BackendSerializer.Deserialize(json);

            Assert.Equal(definition.Id, again.Id);
            Assert.Equal(definition.Targets.Count, again.Targets.Count);
            Assert.Equal(definition.SharedOptions.Count, again.SharedOptions.Count);
            Assert.False(again.IsBuiltIn);
        }
    }
}
