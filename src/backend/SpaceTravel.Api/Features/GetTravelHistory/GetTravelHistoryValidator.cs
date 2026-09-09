using FluentValidation;

namespace SpaceTravel.Api.Features.GetTravelHistory;

public sealed class GetTravelHistoryValidator : AbstractValidator<GetTravelHistoryRequest>
{
    public const int MaxPageSize = 100;

    public GetTravelHistoryValidator()
    {
        RuleFor(r => r.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page starts at 1.");

        RuleFor(r => r.PageSize)
            .InclusiveBetween(1, MaxPageSize)
            .WithMessage($"Page size must be between 1 and {MaxPageSize}.");
    }
}
