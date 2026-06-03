using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace SteadyCron.Cli.Infrastructure;

/// <summary>Resolves command and dependency instances from the built service provider.</summary>
public sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly IServiceProvider _provider;

    public TypeResolver(IServiceProvider provider)
    {
        _provider = provider;
    }

    public object? Resolve(Type? type) =>
        type is null ? null : _provider.GetService(type);

    public void Dispose()
    {
        if (_provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
