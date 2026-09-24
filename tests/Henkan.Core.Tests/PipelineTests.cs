using Henkan.Core.Backends;

namespace Henkan.Core.Tests;

/// <summary>
/// The multi-step definitions. These check the wiring rather than the
/// conversions: whether a route points at targets that exist, whether its
/// options reach the leg that uses them, and whether the alternatives in a step
/// really are alternatives. The conversions themselves are checked against real
/// files by <c>henkanc verify</c>.
/// </summary>
public class PipelineTests
{
    private static readonly IReadOnlyList<BackendDefinition> Definitions =
        [.. BackendRegistry.LoadBuiltInDefinitions().Select(d => d.Definition)];

    private static BackendDefinition Pipeline => Definitions.Single(d => d.Id == "pipeline");

    public static IEnumerable<object[]> Routes() =>
        Pipeline.Targets.Select(t => new object[] { t.Id, t });

    [Theory]
    [MemberData(nameof(Routes))]
    public void EveryStepNamesTargetsThatExist(string id, ConversionTarget route)
    {
        Assert.NotEmpty(route.Steps);

        foreach (PipelineStep step in route.Steps)
        {
            Assert.NotEmpty(step.Targets);

            foreach (string key in step.Targets)
            {
                string[] parts = key.Split('/', 2);
                Assert.Equal(2, parts.Length);

                BackendDefinition? backend = Definitions.FirstOrDefault(d => d.Id == parts[0]);
                Assert.True(backend is not null, $"{id}: no backend \"{parts[0]}\".");
                Assert.True(backend.FindTarget(parts[1]) is not null, $"{id}: {parts[0]} has no target \"{parts[1]}\".");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void AlternativesWithinAStepProduceTheSameFormat(string id, ConversionTarget route)
    {
        foreach (PipelineStep step in route.Steps)
        {
            string[] extensions = [.. step.Targets.Select(Extension).Distinct(StringComparer.OrdinalIgnoreCase)];

            Assert.True(
                extensions.Length == 1,
                $"{id}: a step offers {string.Join(" or ", step.Targets)}, which do not agree on an output format.");
        }
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void TheLastStepProducesWhatTheRoutePromises(string id, ConversionTarget route)
    {
        string produced = Extension(route.Steps[^1].Targets[0]);

        Assert.True(
            produced.Equals(route.OutputExtension, StringComparison.OrdinalIgnoreCase),
            $"{id}: promises .{route.OutputExtension} but its last step writes .{produced}.");
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void EveryOptionReferenceResolvesToAnOptionTheRouteDeclares(string id, ConversionTarget route)
    {
        var declared = route.Options.Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (PipelineStep step in route.Steps)
        {
            foreach ((string name, string value) in step.Options ?? new Dictionary<string, string>())
            {
                if (!value.StartsWith('{') || !value.EndsWith('}'))
                {
                    continue;
                }

                string referenced = value[1..^1];

                Assert.True(
                    declared.Contains(referenced),
                    $"{id}: passes {{{referenced}}} to {name}, but the route declares no such option.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void EveryOptionPassedToAStepExistsOnThatStep(string id, ConversionTarget route)
    {
        foreach (PipelineStep step in route.Steps)
        {
            foreach (string key in step.Targets)
            {
                string[] parts = key.Split('/', 2);
                BackendDefinition backend = Definitions.Single(d => d.Id == parts[0]);
                ConversionTarget leg = backend.FindTarget(parts[1])!;

                var available = backend.GetOptionSchema(leg).Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (string name in (step.Options ?? new Dictionary<string, string>()).Keys)
                {
                    Assert.True(
                        available.Contains(name),
                        $"{id}: sets \"{name}\" on {key}, which has no such option.");
                }
            }
        }
    }

    [Fact]
    public void NoRouteChainsABackendToItself()
    {
        foreach (ConversionTarget route in Pipeline.Targets)
        {
            Assert.DoesNotContain(route.Steps, step => step.Targets.Any(t => t.StartsWith("pipeline/", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void OfficeTargetsCarryTheFormatCodeTheyNeed()
    {
        BackendDefinition office = Definitions.Single(d => d.Id == "office");

        foreach (ConversionTarget target in office.Targets)
        {
            Assert.False(string.IsNullOrWhiteSpace(target.Operation), $"{target.Id}: no operation.");

            if (target.Setting("mode") == "saveas")
            {
                Assert.True(
                    int.TryParse(target.Setting("format"), out _),
                    $"{target.Id}: saveas without a numeric format code.");
            }
        }
    }

    private static string Extension(string key)
    {
        string[] parts = key.Split('/', 2);
        return Definitions.Single(d => d.Id == parts[0]).FindTarget(parts[1])!.OutputExtension;
    }
}
