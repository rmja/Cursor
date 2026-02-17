using Cursor;
using Refit;

namespace Example;

public interface IExampleApi
{
    [Get("/something")]
    [GenerateEnumerator]
    Task<CursorPage<string>> ListSomethingAsync(
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default
    );

    // Using custom parameter names
    [Get("/something-else")]
    [GenerateEnumerator(LimitParameterName = "pageSize", CursorParameterName = "nextToken")]
    Task<CursorPage<string>> ListSomethingElseAsync(
        int? pageSize,
        string? nextToken,
        CancellationToken cancellationToken
    );

    // .. Or using a custom attribute
    [Get("/something-else")]
    [MyCustomEnumerator]
    Task<CursorPage<string>> ListSomethingElseAlternativeAsync(
        int? pageSize,
        string? nextToken,
        CancellationToken cancellationToken
    );

    [Get("/something-third")]
    [GenerateEnumerator]
    Task<CustomCursorPage<string>> ListSomethingThirdAsync(
        int? limit,
        string? cursor = null,
        CancellationToken cancellationToken = default
    );

    [Get("/something-fourth")]
    [GenerateEnumerator(CursorParameterName = "offset")]
    Task<CursorPage<string>> ListSomethingFourthAsync(
        int offset = 0,
        int? limit = null,
        CancellationToken cancellationToken = default
    );

    [Get("/something-fifth")]
    [GenerateEnumerator(CursorParameterName = "offset")]
    Task<CursorPage<string>> ListSomethingFifthAsync(
        long offset = 0,
        int? limit = null,
        CancellationToken cancellationToken = default
    );
}
