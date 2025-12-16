namespace Cursor;

/// <summary>
/// Marks a method that returns <see cref="Task{TResult}"/> where TResult implements <see cref="ICursorPage{T}"/> to generate an enumerator extension method.
/// </summary>
/// <remarks>
/// The method must:
/// - Return Task&lt;TPage&gt; where TPage implements ICursorPage&lt;T&gt;
/// - Have parameters for limit, cursor, and cancellationToken (names configurable via properties)
/// - Follow the naming pattern 'List*Async'
/// 
/// The <see cref="LimitParameterName"/> and <see cref="CursorParameterName"/> properties allow customization
/// of parameter names if your API uses different naming conventions.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public class GenerateEnumeratorAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the name of the limit parameter. Defaults to "limit".
    /// </summary>
    public string LimitParameterName { get; set; } = "limit";

    /// <summary>
    /// Gets or sets the name of the cursor parameter. Defaults to "cursor".
    /// </summary>
    public string CursorParameterName { get; set; } = "cursor";
}
