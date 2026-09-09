using FluentValidation;

namespace SpaceTravel.Api.Code.Validation;

/// <summary>
/// The "validate → handler" half of every slice's flow. Runs the slice's
/// <see cref="IValidator{T}"/> before the handler ever sees the request, so
/// handlers can assume their input is structurally valid.
/// </summary>
public sealed class ValidationFilter<TRequest> : IEndpointFilter
    where TRequest : class
{
    private readonly IValidator<TRequest> _validator;

    public ValidationFilter(IValidator<TRequest> validator) => _validator = validator;

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();

        if (request is null)
        {
            return TypedResults.Problem(
                title: "Malformed request",
                detail: $"The request body could not be bound to {typeof(TRequest).Name}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await _validator.ValidateAsync(request, context.HttpContext.RequestAborted);

        if (!result.IsValid)
        {
            return TypedResults.ValidationProblem(result.ToDictionary());
        }

        return await next(context);
    }
}

public static class ValidationFilterExtensions
{
    /// <summary>Applies <see cref="ValidationFilter{TRequest}"/> to a route.</summary>
    public static RouteHandlerBuilder WithValidation<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : class
        => builder
            .AddEndpointFilter<ValidationFilter<TRequest>>()
            .ProducesValidationProblem();
}
