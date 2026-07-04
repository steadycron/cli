namespace SteadyCron.Cli.Manifest.Generators;

/// <summary>
/// Renders a single resource as manifest-format text (a canonical 2-space-indented list item,
/// without any section header). One implementation per target format — <see cref="YamlBlockRenderer"/>
/// today; a future <c>HclBlockRenderer</c> would implement the same interface so the command flow
/// and models (<see cref="NewJobSpec"/> etc.) never change when Terraform output lands (SPEC-21 §4).
/// </summary>
public interface IManifestBlockRenderer
{
    string RenderJob(NewJobSpec spec);
    string RenderChannel(NewChannelSpec spec);
    string RenderTag(NewTagSpec spec);
    string RenderVariable(NewVariableSpec spec);
}
