namespace Cursor;

public static class CursorPaginationInMemoryExtensions
{
    /// <summary>
    /// Convenience overload of
    /// <see cref="ToCursorPage{T, TKey}(IEnumerable{T}, Func{T, TKey}, IComparer{TKey}, int, string?, CursorOptions?)"/>
    /// that uses <see cref="Comparer{TKey}.Default"/>. Because the default comparer always
    /// compares in ascending order, this overload is only correct for sequences ordered
    /// ascending (<c>OrderBy</c>/<c>ThenBy</c>). For <c>OrderByDescending</c>/<c>ThenByDescending</c>
    /// sequences, use the overload that takes an explicit <see cref="IComparer{TKey}"/> and pass
    /// a comparer that sorts descending.
    /// </summary>
    /// <inheritdoc cref="ToCursorPage{T, TKey}(IEnumerable{T}, Func{T, TKey}, IComparer{TKey}, int, string?, CursorOptions?)"/>
    public static CursorPage<T> ToCursorPage<T, TKey>(
        this IEnumerable<T> source,
        Func<T, TKey> key,
        int limit,
        string? cursor = null,
        CursorOptions? options = null
    )
        where TKey : notnull =>
        ToCursorPage(source, key, Comparer<TKey>.Default, limit, cursor, options);

    /// <summary>
    /// A page over a sequence that is already in memory.
    ///
    /// <para>
    /// The sequence is walked once with a single enumerator. When <see cref="CursorOptions.ComputeTotalCount"/>
    /// is <see langword="false"/> (the default), enumeration stops as soon as <paramref name="limit"/> + 1
    /// items past the cursor have been buffered — items beyond that point, if any, are never
    /// visited. Only that trailing buffer, sized to <paramref name="limit"/> + 1 up front, is
    /// allocated; there is no intermediate list, no <c>SkipWhile</c>/<c>Take</c> pipeline, and no
    /// re-enumeration.
    /// </para>
    ///
    /// <para>
    /// When <see cref="CursorOptions.ComputeTotalCount"/> is <see langword="true"/>, the full
    /// sequence is enumerated to produce <see cref="CursorPage{T}.TotalCount"/> — mirroring the
    /// separate <c>COUNT</c> query issued by the Entity Framework Core <c>ToCursorPageAsync</c>.
    /// The count reflects every item in <paramref name="source"/>, not just those remaining
    /// after the cursor.
    /// </para>
    /// </summary>
    /// <param name="key">
    /// What the cursor is made of. It has to be unique across the sequence, for the same reason
    /// the database orderings need a tie-breaker — two equal keys either lose a row across the
    /// boundary or repeat one.
    /// </param>
    /// <param name="comparer">
    /// The one the sequence was ordered by. A page ordered case-insensitively and compared
    /// ordinally skips rows, and only for the names where the two disagree — which is the kind
    /// of bug that shows up once, in production, for one company.
    /// <para>
    /// The comparer must describe the sequence's actual iteration order, not the natural order
    /// of <typeparamref name="TKey"/>. For a sequence produced by <c>OrderByDescending</c>, pass
    /// a comparer that sorts descending (for example <c>Comparer&lt;TKey&gt;.Create((a, b) =&gt;
    /// b.CompareTo(a))</c>); the overload without a <paramref name="comparer"/> parameter always
    /// uses <see cref="Comparer{TKey}.Default"/> and is therefore only correct for ascending
    /// sequences.
    /// </para>
    /// </param>
    /// <param name="limit">
    /// The maximum number of items to return in the page. Zero is valid — for example to fetch
    /// only <see cref="CursorOptions.ComputeTotalCount"/> without any items — and produces an
    /// empty page with <see cref="CursorPage{T}.NextCursor"/> set to <see langword="null"/>,
    /// since no item was returned to resume from.
    /// </param>
    /// <param name="cursor">Optional. The cursor from a previous page to continue pagination from.</param>
    /// <param name="options">Options to control the behavior of the cursor pagination.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is negative.</exception>
    public static CursorPage<T> ToCursorPage<T, TKey>(
        this IEnumerable<T> source,
        Func<T, TKey> key,
        IComparer<TKey> comparer,
        int limit,
        string? cursor = null,
        CursorOptions? options = null
    )
        where TKey : notnull
    {
        if (limit < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                limit,
                "Limit cannot be negative."
            );
        }

        options ??= CursorOptions.Default;
        var computeTotalCount = options.ComputeTotalCount;

        var skipping = cursor is not null;
        var after = skipping ? options.CursorSerializer.DecodeCursor<TKey>(cursor!) : default;

        // Sized to hold one more than asked for up front, which is how "is there another page"
        // is answered without a second pass over the same sequence, and without ever growing.
        var items = new List<T>(limit + 1);
        long totalCount = 0;

        foreach (var item in source)
        {
            if (computeTotalCount)
            {
                totalCount++;
            }

            if (skipping)
            {
                if (comparer.Compare(key(item), after!) <= 0)
                {
                    continue;
                }

                skipping = false;
            }

            if (items.Count <= limit)
            {
                items.Add(item);
            }
            else if (!computeTotalCount)
            {
                // Nothing left to learn from the rest of the sequence: the page is full and the
                // total count was not asked for.
                break;
            }
        }

        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        // When limit is 0, hasMore may be true but there is no returned item to derive a cursor
        // from - nothing was handed out, so there is nothing to resume after.
        var nextCursor =
            hasMore && items.Count > 0
                ? options.CursorSerializer.EncodeCursor(key(items[^1]))
                : null;

        return new CursorPage<T>(items)
        {
            HasMore = hasMore,
            NextCursor = nextCursor,
            TotalCount = computeTotalCount ? totalCount : null,
        };
    }
}
