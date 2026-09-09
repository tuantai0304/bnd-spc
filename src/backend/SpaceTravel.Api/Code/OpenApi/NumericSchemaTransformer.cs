using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SpaceTravel.Api.Code.OpenApi;

/// <summary>
/// Drops the "or string" half of every numeric schema.
///
/// .NET's schema exporter types an <c>int</c> as <c>["integer","string"]</c> with a
/// digits-only pattern, because the binder would also accept a quoted number. That is
/// true of the input but not of anything this API emits — every numeric response value
/// is a JSON number. Left alone it makes generated clients type every id, count and
/// weight as <c>number | string</c>, which pushes a cast onto every call site for a
/// case that never occurs.
/// </summary>
public sealed class NumericSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (schema.Type is not { } type)
        {
            return Task.CompletedTask;
        }

        var isNumeric = type.HasFlag(JsonSchemaType.Integer) || type.HasFlag(JsonSchemaType.Number);

        if (isNumeric && type.HasFlag(JsonSchemaType.String))
        {
            // Keeps the null half intact, so int? stays ["null","integer"].
            schema.Type = type & ~JsonSchemaType.String;
            schema.Pattern = null;
        }

        return Task.CompletedTask;
    }
}
