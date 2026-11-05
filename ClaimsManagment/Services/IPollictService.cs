using System.Text.Json;

namespace ClaimsManagment.Services;

public interface IPollictService
{
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken);

    Task<CreatedPollictRow> CreateAsync(
        IReadOnlyDictionary<string, JsonElement> values,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
        string id,
        IReadOnlyDictionary<string, JsonElement> values,
        CancellationToken cancellationToken);
}

public sealed record CreatedPollictRow(
    string PrimaryKeyValue,
    IReadOnlyDictionary<string, object?> Row);
