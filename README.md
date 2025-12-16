# Cursor Pagination Library

A C# source generator library that automatically creates async enumeration methods for cursor-based paginated APIs, making it effortless to iterate through paginated results.

## Features

- ✨ **Automatic Code Generation**: Uses C# source generators to create enumeration methods at compile-time
- 🚀 **Zero Runtime Overhead**: All code is generated during compilation
- 🔄 **IAsyncEnumerable Support**: Modern async streaming with `await foreach`
- 🎯 **Type-Safe**: Full IntelliSense support and compile-time checking
- 🔌 **API Agnostic**: Works with any HTTP client library (Refit, HttpClient, etc.)
- ⚙️ **Customizable**: Configure parameter names to match your API conventions
- 📦 **AOT Compatible**: Fully compatible with Native AOT compilation

## Installation

```bash
dotnet add package Cursor
```

## Quick Start

### 1. Define Your Cursor Page Model

Implement the `ICursorPage<T>` interface for your paginated response:

```csharp
using Cursor;

public class CursorPage<T> : ICursorPage<T>
{
    public List<T> Items { get; set; } = new();
    public string? NextCursor { get; set; }
}
```

### 2. Decorate Your API Methods

Add the `[GenerateEnumerator]` attribute to methods that return paginated results:

```csharp
using Cursor;
using Refit;

public interface IExampleApi
{
    [Get("/items")]
    [GenerateEnumerator]
    Task<CursorPage<Item>> ListItemsAsync(
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default
    );
}
```

### 3. Use the Generated Enumerators

The source generator automatically creates extension methods for you:

```csharp
var api = RestService.For<IExampleApi>("https://api.example.com");

// Enumerate all items across all pages
await foreach (var item in api.EnumerateItemsAsync())
{
    Console.WriteLine(item);
}

// Or collect all items at once
var allItems = await api.EnumerateItemsAsync().ToListAsync();

// Enumerate pages instead of individual items
await foreach (var page in api.EnumerateItemsPagesAsync())
{
    Console.WriteLine($"Page with {page.Items.Count} items");
}
```

## Requirements

Your API methods must:
- Return `Task<TPage>` where `TPage` implements `ICursorPage<T>`
- Follow the naming pattern `List*Async` (e.g., `ListItemsAsync`, `ListUsersAsync`)
- Have parameters for:
  - `limit` (or custom name) - page size
  - `cursor` (or custom name) - pagination token
  - `cancellationToken` - for async cancellation

## Advanced Usage

### Custom Parameter Names

If your API uses different parameter names, you can customize them:

```csharp
[Get("/items")]
[GenerateEnumerator(
    LimitParameterName = "pageSize", 
    CursorParameterName = "nextToken"
)]
Task<CursorPage<Item>> ListItemsAsync(
    int? pageSize,
    string? nextToken,
    CancellationToken cancellationToken
);
```

### Custom Cursor Page Implementation

You can use any class that implements `ICursorPage<T>`:

```csharp
public record CustomCursorPage<T> : ICursorPage<T>
{
    public required List<T> Data { get; init; }
    public string? Cursor { get; init; }

    List<T> ICursorPage<T>.Items => Data;
    string? ICursorPage<T>.NextCursor => Cursor;
}

[Get("/items")]
[GenerateEnumerator]
Task<CustomCursorPage<Item>> ListItemsAsync(
    int? limit,
    string? cursor = null,
    CancellationToken cancellationToken = default
);
```

### Passing Additional Parameters

The generator preserves any additional parameters in the generated methods:

```csharp
[Get("/items")]
[GenerateEnumerator]
Task<CursorPage<Item>> ListItemsAsync(
    string category,  // Additional parameter
    int? limit = null,
    string? cursor = null,
    CancellationToken cancellationToken = default
);

// Usage
await foreach (var item in api.EnumerateItemsAsync("electronics"))
{
    // Process items from "electronics" category
}
```

### Custom Attributes

You can create your own attribute that inherits from `GenerateEnumeratorAttribute`:

```csharp
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public class MyCustomEnumeratorAttribute : GenerateEnumeratorAttribute
{
    public MyCustomEnumeratorAttribute()
    {
        LimitParameterName = "pageSize";
        CursorParameterName = "nextToken";
    }
}

// Usage
[Get("/items")]
[MyCustomEnumerator]
Task<CursorPage<Item>> ListItemsAsync(
    int? pageSize,
    string? nextToken,
    CancellationToken cancellationToken
);
```

## Generated Code

For a method named `ListItemsAsync`, the generator creates:

1. **`EnumerateItemsAsync()`** - Returns `IAsyncEnumerable<T>` for iterating individual items
2. **`EnumerateItemsPagesAsync()`** - Returns `IAsyncEnumerable<ICursorPage<T>>` for iterating pages

Both methods:
- Automatically handle pagination by following the `NextCursor`
- Support `CancellationToken` for graceful cancellation
- Allow configuring the page size via an optional `pageSize` parameter

## How It Works

The library uses Roslyn source generators to analyze your code at compile-time:

1. Finds methods decorated with `[GenerateEnumerator]` or derived attributes
2. Validates the method signature and return type
3. Generates extension methods that handle the pagination loop
4. The generated code is added to your compilation automatically

No reflection or runtime code generation is involved, making it AOT-compatible and performant.

## Examples

### Example 1: Basic Usage with Refit

```csharp
using Cursor;
using Refit;

public interface IGitHubApi
{
    [Get("/users/{user}/repos")]
    [GenerateEnumerator]
    Task<CursorPage<Repository>> ListRepositoriesAsync(
        string user,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default
    );
}

// Usage
var api = RestService.For<IGitHubApi>("https://api.github.com");
await foreach (var repo in api.EnumerateRepositoriesAsync("octocat"))
{
    Console.WriteLine($"{repo.Name}: {repo.Description}");
}
```

### Example 2: Processing Pages

```csharp
await foreach (var page in api.EnumerateRepositoriesPagesAsync("octocat", limit: 50))
{
    Console.WriteLine($"Processing page with {page.Items.Count} repositories");
    
    // Process entire page at once
    foreach (var repo in page.Items)
    {
        // ...
    }
}
```

### Example 3: With Cancellation

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

try
{
    await foreach (var item in api.EnumerateItemsAsync(cancellationToken: cts.Token))
    {
        // Process item
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Operation timed out");
}
```

## License

MIT

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
