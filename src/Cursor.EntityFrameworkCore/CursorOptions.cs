namespace Cursor.EntityFrameworkCore;

public class CursorOptions
{
    public static CursorOptions Default { get; } = new(bootstrap: true);

    public CursorOptions()
    {
        ComputeTotalCount = Default.ComputeTotalCount;
        CursorSerializer = Default.CursorSerializer;
    }

#pragma warning disable IDE0060 // Remove unused parameter
    private CursorOptions(bool bootstrap)
#pragma warning restore IDE0060 // Remove unused parameter
    {
        ComputeTotalCount = false;
        CursorSerializer = new JsonCursorSerializer();
    }

    /// <summary>
    /// Whether to compute the total count of items across all pages. This may be expensive for large datasets.
    /// </summary>
    public bool ComputeTotalCount { get; set; }

    /// <summary>
    /// Gets or sets the serializer used to encode and decode cursor values.
    /// Defaults to <see cref="JsonCursorSerializer"/> with default json options.
    /// </summary>
    public ICursorSerializer CursorSerializer { get; set; }
}
