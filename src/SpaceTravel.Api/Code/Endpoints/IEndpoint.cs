namespace SpaceTravel.Api.Code.Endpoints;

/// <summary>
/// Every vertical slice implements this so it self-registers. Adding a feature
/// means adding a folder under Features/ — never editing Program.cs.
/// </summary>
public interface IEndpoint
{
    static abstract void Map(IEndpointRouteBuilder app);
}
