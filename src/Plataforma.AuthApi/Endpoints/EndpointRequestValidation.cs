using System.ComponentModel.DataAnnotations;

namespace Plataforma.AuthApi.Endpoints;

internal static class EndpointRequestValidation
{
    public static Dictionary<string, string[]> Validate(object request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

        return results
            .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                .Select(memberName => new { MemberName = memberName, result.ErrorMessage }))
            .GroupBy(result => result.MemberName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(result => result.ErrorMessage ?? "The value is invalid.").ToArray());
    }
}
