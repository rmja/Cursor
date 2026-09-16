namespace Cursor;

public static class CursorPageExtensions
{
    /// <summary>
    /// Transforms a CursorPage of one type into a CursorPage of another type using the provided transformation function.
    /// </summary>
    /// <typeparam name="TSource">The type of the items in the source CursorPage.</typeparam>
    /// <typeparam name="TResult">The type of the items in the resulting CursorPage.</typeparam>
    /// <param name="page">The source CursorPage to transform.</param>
    /// <param name="transform">A function to transform each item in the source CursorPage.</param>
    /// <returns>A new CursorPage containing the transformed items.</returns>
    public static CursorPage<TResult> ToCursorPage<TSource, TResult>(
        this CursorPage<TSource> page,
        Func<TSource, TResult> transform
    ) =>
        new(page.Items.Select(transform))
        {
            NextCursor = page.NextCursor,
            HasMore = page.HasMore,
            TotalCount = page.TotalCount,
        };
}
