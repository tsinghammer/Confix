using Confix.Tool.Abstractions;
using Confix.Tool.Commands.Logging;
using Confix.Tool.Common.Pipelines;
using Confix.Tool.Entities.Components;
using Confix.Tool.Entities.Components.DotNet;
using Confix.Tool.Middlewares;
using Confix.Tool.Middlewares.JsonSchemas;
using Confix.Tool.Schema;
using HotChocolate;
using Path = System.IO.Path;

namespace Confix.Tool.Commands.Project;

public sealed class ProjectReloadPipeline : Pipeline
{
    /// <inheritdoc />
    protected override void Configure(IPipelineDescriptor builder)
    {
        builder
            .Use<LoadConfigurationMiddleware>()
            .UseReadConfigurationFiles()
            .UseEnvironment()
            .Use<VariableMiddleware>()
            .Use<JsonSchemaCollectionMiddleware>()
            .Use<ConfigurationAdapterMiddleware>()
            .Use<BuildComponentProviderMiddleware>()
            .UseHandler<IProjectComposer, ISchemaStore>(InvokeAsync);
    }

    private static async Task InvokeAsync(
        IMiddlewareContext context,
        IProjectComposer projectComposer,
        ISchemaStore schemaStore)
    {
        context.SetStatus("Reloading the schema of the project...");

        var cancellationToken = context.CancellationToken;

        var jsonSchemas = context.Features.Get<JsonSchemaFeature>();
        var configuration = context.Features.Get<ConfigurationFeature>();
        var files = context.Features.Get<ConfigurationFileFeature>().Files;

        configuration.EnsureProjectScope();

        var project = configuration.EnsureProject();
        var solution = configuration.EnsureSolution();

        var componentProvider =
            context.Features.Get<ComponentProviderExecutorFeature>().Executor;

        var providerContext =
            new ComponentProviderContext(context.Logger, cancellationToken, project, solution);

        context.SetStatus("Loading components...");
        await componentProvider.ExecuteAsync(providerContext);
        var components = providerContext.Components;
        context.Logger.LogComponentsLoaded(components);

        context.SetStatus("Loading variables...");
        var variableResolver = context.Features.Get<VariableResolverFeature>().Resolver;
        var variables = await variableResolver.ListVariables(cancellationToken);

        context.SetStatus("Composing the schema...");
        var jsonSchema = projectComposer.Compose(components, variables);
        context.Logger.LogSchemaCompositionCompleted(project);

        var schemaFile = await schemaStore
            .StoreAsync(solution, project, jsonSchema, cancellationToken);

        var jsonSchemaDefinition = new JsonSchemaDefinition()
        {
            Project = project,
            Solution = solution.Directory!,
            FileMatch = files.Select(x => x.File.RelativeTo(solution.Directory!)).ToList(),
            SchemaFile = schemaFile,
            RelativePathToProject =
                Path.GetRelativePath(solution.Directory!.FullName, project.Directory!.FullName)
        };

        jsonSchemas.Schemas.Add(jsonSchemaDefinition);
    }
}

file static class Log
{
    public static void LogComponentsLoaded(
        this IConsoleLogger console,
        ICollection<Abstractions.Component> components)
    {
        console.Success($"Loaded {components.Count} components");
        foreach (var component in components)
        {
            console.Information($"-  @{component.Provider}/{component.ComponentName}");
        }
    }

    public static void LogSchemaCompositionCompleted(
        this IConsoleLogger console,
        ProjectDefinition project)
    {
        console.Success($"Schema composition completed for project {project.Name}");
    }
}