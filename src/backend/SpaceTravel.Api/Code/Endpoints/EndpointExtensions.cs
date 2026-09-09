using System.Reflection;

namespace SpaceTravel.Api.Code.Endpoints;

public static class EndpointExtensions
{
    /// <summary>
    /// Finds every <see cref="IEndpoint"/> in the assembly and invokes its static
    /// Map method. Slices register themselves; the composition root stays empty.
    /// </summary>
    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder app)
    {
        var endpoints = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && t.IsAssignableTo(typeof(IEndpoint)));

        foreach (var endpoint in endpoints)
        {
            var map = endpoint.GetMethod(
                nameof(IEndpoint.Map),
                BindingFlags.Public | BindingFlags.Static);

            map?.Invoke(null, [app]);
        }

        return app;
    }
}
