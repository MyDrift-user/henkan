using Henkan.Core.Templating;

namespace Henkan.Core.Tests;

public class TemplateRendererTests
{
    private static VariableContext Context(params (string Key, string Value)[] options) =>
        VariableContext.Create(
            @"C:\media\my clip.mov",
            @"D:\out\my clip.mp4",
            @"C:\temp\job",
            options.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase),
            @"C:\tools\ffmpeg");

    [Fact]
    public void SubstitutesBuiltIns()
    {
        var context = Context();
        Assert.Equal(@"C:\media\my clip.mov", TemplateRenderer.RenderText("{input}", context.Lookup));
        Assert.Equal(@"C:\media", TemplateRenderer.RenderText("{inputDir}", context.Lookup));
        Assert.Equal("my clip", TemplateRenderer.RenderText("{inputName}", context.Lookup));
        Assert.Equal("mov", TemplateRenderer.RenderText("{inputExt}", context.Lookup));
        Assert.Equal("my clip.mov", TemplateRenderer.RenderText("{inputFileName}", context.Lookup));
        Assert.Equal(@"D:\out\my clip.mp4", TemplateRenderer.RenderText("{output}", context.Lookup));
        Assert.Equal("mp4", TemplateRenderer.RenderText("{outputExt}", context.Lookup));
        Assert.Equal(@"C:\temp\job", TemplateRenderer.RenderText("{tempDir}", context.Lookup));
        Assert.Equal(@"C:\tools\ffmpeg", TemplateRenderer.RenderText("{toolDir}", context.Lookup));
    }

    [Fact]
    public void SubstitutesOptionsCaseInsensitively()
    {
        var context = Context(("Bitrate", "192"));
        Assert.Equal("192k", TemplateRenderer.RenderText("{bitrate}k", context.Lookup));
        Assert.Equal("192", TemplateRenderer.RenderText("{option:Bitrate}", context.Lookup));
    }

    [Fact]
    public void BuiltInsShadowOptions()
    {
        var context = Context(("input", "hijacked"));
        Assert.Equal(@"C:\media\my clip.mov", TemplateRenderer.RenderText("{input}", context.Lookup));
        Assert.Equal("hijacked", TemplateRenderer.RenderText("{option:input}", context.Lookup));
    }

    [Fact]
    public void EscapesBraces()
    {
        Assert.Equal("{literal}", TemplateRenderer.RenderText("{{literal}}", _ => null));
        Assert.Equal("{\"Quality\":90}", TemplateRenderer.RenderText("{{\"Quality\":{Q}}}", n => n == "Q" ? "90" : null));
    }

    [Fact]
    public void UnknownPlaceholderBecomesEmpty()
    {
        Assert.Equal("ab", TemplateRenderer.RenderText("a{missing}b", _ => null));
    }

    [Theory]
    [InlineData("{unclosed")]
    [InlineData("{}")]
    public void RejectsMalformedPlaceholders(string template)
    {
        Assert.Throws<TemplateException>(() => TemplateRenderer.RenderText(template, _ => null));
    }

    [Fact]
    public void TokenizesRespectingQuotes()
    {
        Assert.Equal(["-i", "a b", "c"], TemplateRenderer.Tokenize("-i \"a b\" c"));
        Assert.Equal(["say \"hi\""], TemplateRenderer.Tokenize("\"say \"\"hi\"\"\""));
        Assert.Equal([""], TemplateRenderer.Tokenize("\"\""));
        Assert.Empty(TemplateRenderer.Tokenize("   "));
        Assert.Throws<TemplateException>(() => TemplateRenderer.Tokenize("\"open"));
    }

    [Fact]
    public void PathsWithSpacesStayOneArgument()
    {
        var fragments = new[]
        {
            new ArgumentFragment { Value = "-i \"{input}\"" },
            new ArgumentFragment { Value = "\"{output}\"" },
        };

        Assert.Equal(
            ["-i", @"C:\media\my clip.mov", @"D:\out\my clip.mp4"],
            TemplateRenderer.RenderArguments(fragments, Context()));
    }

    [Fact]
    public void UnquotedPlaceholderWithSpacesStillStaysOneArgument()
    {
        // Quoting is resolved before substitution, so even a sloppy template
        // cannot let a file name split into two arguments.
        var fragments = new[] { new ArgumentFragment { Value = "-i {input}" } };
        Assert.Equal(["-i", @"C:\media\my clip.mov"], TemplateRenderer.RenderArguments(fragments, Context()));
    }

    [Fact]
    public void ConditionalFragmentsAreSkipped()
    {
        var fragments = new[]
        {
            new ArgumentFragment { Value = "-b:a {Bitrate}k", When = "Mode == 'Bitrate'" },
            new ArgumentFragment { Value = "-q:a {Quality}", When = "Mode == 'VBR'" },
        };

        Assert.Equal(["-b:a", "192k"], TemplateRenderer.RenderArguments(fragments, Context(("Mode", "Bitrate"), ("Bitrate", "192"), ("Quality", "2"))));
        Assert.Equal(["-q:a", "2"], TemplateRenderer.RenderArguments(fragments, Context(("Mode", "VBR"), ("Bitrate", "192"), ("Quality", "2"))));
    }

    [Fact]
    public void EmptyExpansionIsDropped()
    {
        var fragments = new[] { new ArgumentFragment { Value = "{Extra}" } };
        Assert.Empty(TemplateRenderer.RenderArguments(fragments, Context(("Extra", ""))));
    }

    [Fact]
    public void SplitAfterExpansionTokenisesTheValue()
    {
        var fragments = new[] { new ArgumentFragment { Value = "{Extra}", SplitAfterExpansion = true } };
        Assert.Equal(["-map", "0", "-sn"], TemplateRenderer.RenderArguments(fragments, Context(("Extra", "-map 0 -sn"))));
        Assert.Empty(TemplateRenderer.RenderArguments(fragments, Context(("Extra", ""))));
    }

    [Fact]
    public void CommandLinePreviewQuotesWhereNeeded()
    {
        var fragments = new[] { new ArgumentFragment { Value = "-i \"{input}\" -y" } };
        string preview = TemplateRenderer.RenderCommandLine(@"C:\tools\ffmpeg.exe", fragments, Context());
        Assert.Equal(@"C:\tools\ffmpeg.exe -i ""C:\media\my clip.mov"" -y", preview);
    }
}
