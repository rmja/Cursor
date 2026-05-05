using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Cursor.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Cursor;

/// <summary>
/// Provides extension methods for Entity Framework Core queries to implement cursor-based pagination.
/// </summary>
/// <remarks>
/// Cursor pagination is driven by the ordering already applied to the query: pass an <see cref="IQueryable{T}"/>
/// that has been ordered with <see cref="Queryable.OrderBy"/>, <see cref="Queryable.OrderByDescending"/>,
/// <see cref="Queryable.ThenBy"/>, and/or <see cref="Queryable.ThenByDescending"/>. Mixed-direction
/// compound orderings (for example, <c>OrderByDescending(x =&gt; x.Priority).ThenBy(x =&gt; x.Id)</c>)
/// are supported.
/// <para>
/// Projections (<c>Select</c>) are allowed between the ordering chain and the call to <c>CursorPage</c>
/// or <c>ToCursorPageAsync</c>, e.g. <c>query.OrderBy(x =&gt; x.Id).Select(x =&gt; new Dto { ... }).ToCursorPageAsync(20)</c>.
/// In this case the cursor filter is injected at the source-entity level (above the ordering chain,
/// below the projection).
/// </para>
/// <para>
/// <b>Stable pagination requires a unique ordering.</b> If your ordering keys can contain duplicate values
/// (for example, <c>OrderBy(x =&gt; x.Name)</c>), append a tiebreaker such as the primary key:
/// <c>OrderBy(x =&gt; x.Name).ThenBy(x =&gt; x.Id)</c>. Without a unique ordering, pages may skip or repeat
/// rows when items share a key value with the cursor.
/// </para>
/// </remarks>
public static partial class CursorPaginationExtensions
{
    private static readonly ConditionalWeakTable<Expression, CursorPageInfo> _cursorPageInfo = [];

    /// <summary>
    /// Applies cursor-based pagination to an ordered query.
    /// </summary>
    /// <typeparam name="T">The element type of the query (may be a projection of the source entity).</typeparam>
    /// <param name="query">
    /// A query whose outermost operators form an ordering chain
    /// (<see cref="Queryable.OrderBy"/> / <see cref="Queryable.OrderByDescending"/>, optionally
    /// followed by <see cref="Queryable.ThenBy"/> / <see cref="Queryable.ThenByDescending"/>),
    /// optionally with a single <see cref="Queryable.Select"/> projection on top.
    /// Mixed directions are allowed.
    /// </param>
    /// <param name="limit">The maximum number of items to return per page.</param>
    /// <param name="cursor">Optional. The cursor from a previous page to continue pagination from.</param>
    /// <param name="options">Options to control the behavior of the cursor pagination.</param>
    /// <returns>An <see cref="IQueryable{T}"/> with cursor filtering and take applied. Call <see cref="ToCursorPageAsync{T}(IQueryable{T}, CancellationToken)"/> to materialize the results.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="query"/> does not have an ordering operator at (or below a Select projection on) its outermost call chain.</exception>
    /// <remarks>
    /// You may apply additional LINQ operators such as <c>.Select()</c> after this method and before
    /// <see cref="ToCursorPageAsync{T}(IQueryable{T}, CancellationToken)"/>; pagination metadata is
    /// carried through the resulting expression tree.
    /// <para>
    /// <b>Stable pagination requires a unique ordering.</b> If the ordering keys can contain duplicates,
    /// append a tiebreaker (for example, <c>.ThenBy(x =&gt; x.Id)</c>) to ensure consistent paging.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Project after CursorPage
    /// var page = await dbContext.Users
    ///     .OrderBy(x => x.Id)
    ///     .CursorPage(limit: 20, cursor: previousCursor)
    ///     .Select(x => new UserDto { Id = x.Id, Name = x.Name })
    ///     .ToCursorPageAsync();
    ///
    /// // Project before CursorPage (also supported)
    /// var page2 = await dbContext.Users
    ///     .OrderBy(x => x.Id)
    ///     .Select(x => new UserDto { Id = x.Id, Name = x.Name })
    ///     .CursorPage(limit: 20)
    ///     .ToCursorPageAsync();
    ///
    /// // Mixed-direction compound ordering
    /// var tasks = await dbContext.Tasks
    ///     .OrderByDescending(x => x.Priority)
    ///     .ThenBy(x => x.Id)
    ///     .CursorPage(limit: 20)
    ///     .ToCursorPageAsync();
    /// </code>
    /// </example>
    public static IQueryable<T> CursorPage<T>(
        this IQueryable<T> query,
        int limit,
        string? cursor = null,
        CursorOptions? options = null
    )
    {
        options ??= CursorOptions.Default;

        var orderingInfo = OrderingHelpers.ExtractOrderingInfo(query.Expression);
        if (orderingInfo is null)
        {
            throw new InvalidOperationException(
                "CursorPage requires the query to be ordered. Apply OrderBy/OrderByDescending "
                    + "(and optionally ThenBy/ThenByDescending) to the query before calling CursorPage. "
                    + "A Select projection between the ordering chain and CursorPage is allowed."
            );
        }

        var originalExpression = query.Expression;
        var sourceType = orderingInfo.SourceType;

        // Default ordered query expression (used by SingleSourceEntityAsync to fetch the
        // last source entity for cursor encoding when projection has changed the type).
        Expression orderedQueryExpression = orderingInfo.OrderingChainNode;
        Expression rewrittenQueryExpression = originalExpression;

        if (cursor is not null)
        {
            var keys = orderingInfo.Keys;
            var keyTypes = keys.Select(k => k.KeyType).ToList();
            var lastKeyValues = options.CursorSerializer.DecodeCompoundCursor(cursor, keyTypes);

            var sourceParameter = Expression.Parameter(sourceType, "x");
            var rewrittenBodies = OrderingHelpers.RewriteKeyBodies(keys, sourceParameter);
            var descendings = keys.Select(k => k.Descending).ToList();
            var comparison = OrderingHelpers.BuildCursorComparison(
                rewrittenBodies,
                descendings,
                lastKeyValues
            );
            var predicateType = typeof(Func<,>).MakeGenericType(sourceType, typeof(bool));
            var wherePredicate = Expression.Lambda(predicateType, comparison, sourceParameter);

            // Wrap the ordering chain in Where(orderingChain, predicate). This stays at
            // the source-entity level, so any Select projection above it still works.
            var whereCall = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Where),
                [sourceType],
                orderingInfo.OrderingChainNode,
                Expression.Quote(wherePredicate)
            );

            orderedQueryExpression = whereCall;
            rewrittenQueryExpression = OrderingHelpers.ReplaceNode(
                originalExpression,
                orderingInfo.OrderingChainNode,
                whereCall
            );
        }

        var rewrittenQuery = query.Provider.CreateQuery<T>(rewrittenQueryExpression);
        var paged = rewrittenQuery.Take(limit < int.MaxValue ? limit + 1 : int.MaxValue);

        // Compile each key selector once for cursor encoding from the materialized last item.
        var compiledKeySelectors = orderingInfo.Keys.Select(k => k.KeySelector.Compile()).ToList();

        _cursorPageInfo.AddOrUpdate(
            paged.Expression,
            new CursorPageInfo
            {
                Limit = limit,
                Options = options,
                OriginalQueryExpression = originalExpression,
                SourceType = sourceType,
                OrderedQueryExpression = orderedQueryExpression,
                EncodeCursorFromSourceEntity = item =>
                {
                    var values = new List<object?>(compiledKeySelectors.Count);
                    foreach (var compiled in compiledKeySelectors)
                    {
                        values.Add(compiled.DynamicInvoke(item));
                    }
                    return options.CursorSerializer.EncodeCompoundCursor(values);
                },
            }
        );

        return paged;
    }

    /// <summary>
    /// Materializes a cursor-paginated query that was previously prepared with <see cref="CursorPage{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of items in the result set (may be a projection of the original entity type).</typeparam>
    /// <param name="query">The query previously prepared with <see cref="CursorPage{T}"/>, optionally followed by additional operators such as <c>.Select()</c>.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation, containing a <see cref="CursorPage{T}"/> with the results.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the query was not prepared with <see cref="CursorPage{T}"/>.</exception>
    /// <example>
    /// <code>
    /// var page = await dbContext.Users
    ///     .OrderBy(x => x.Id)
    ///     .CursorPage(limit: 20, cursor: previousCursor)
    ///     .Select(x => new UserDto { Id = x.Id, Name = x.Name })
    ///     .ToCursorPageAsync();
    /// </code>
    /// </example>
    public static async Task<CursorPage<T>> ToCursorPageAsync<T>(
        this IQueryable<T> query,
        CancellationToken cancellationToken = default
    )
    {
        var info =
            FindCursorPageInfo(query.Expression)
            ?? throw new InvalidOperationException(
                "ToCursorPageAsync() requires CursorPage() to be called first on the query."
            );

        long? totalCount = null;
        if (info.Options.ComputeTotalCount)
        {
            totalCount = await CountOriginalQueryAsync(
                query.Provider,
                info.OriginalQueryExpression,
                info.SourceType,
                cancellationToken
            );
        }

        var limit = info.Limit;
        var items = await query.ToListAsync(cancellationToken);
        var hasMore = items.Count > limit;

        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        string? nextCursor = null;
        if ((hasMore || items.Count == int.MaxValue) && items.Count > 0)
        {
            if (typeof(T) == info.SourceType)
            {
                nextCursor = info.EncodeCursorFromSourceEntity(items[^1]!);
            }
            else
            {
                var entity = await SingleSourceEntityAsync(
                    query.Provider,
                    info.OrderedQueryExpression,
                    info.SourceType,
                    items.Count - 1,
                    cancellationToken
                );
                nextCursor = info.EncodeCursorFromSourceEntity(entity);
            }
        }

        return new CursorPage<T>
        {
            Items = items,
            NextCursor = nextCursor,
            TotalCount = totalCount,
        };
    }

    private static CursorPageInfo? FindCursorPageInfo(Expression expression)
    {
        if (_cursorPageInfo.TryGetValue(expression, out var info))
            return info;

        if (expression is MethodCallExpression methodCall)
        {
            foreach (var arg in methodCall.Arguments)
            {
                var found = FindCursorPageInfo(arg);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    private static Task<long> CountOriginalQueryAsync(
        IQueryProvider provider,
        Expression expression,
        Type sourceType,
        CancellationToken cancellationToken
    )
    {
        return (Task<long>)
            typeof(CursorPaginationExtensions)
                .GetMethod(
                    nameof(CountQueryCoreAsync),
                    BindingFlags.NonPublic | BindingFlags.Static
                )!
                .MakeGenericMethod(sourceType)
                .Invoke(null, [provider, expression, cancellationToken])!;
    }

    private static Task<long> CountQueryCoreAsync<TSource>(
        IQueryProvider provider,
        Expression expression,
        CancellationToken cancellationToken
    )
    {
        var query = provider.CreateQuery<TSource>(expression);
        return query.LongCountAsync(cancellationToken);
    }

    private static Task<object> SingleSourceEntityAsync(
        IQueryProvider provider,
        Expression orderedQueryExpression,
        Type sourceType,
        int skip,
        CancellationToken cancellationToken
    )
    {
        var skipExpr = Expression.Call(
            typeof(Queryable),
            nameof(Enumerable.Skip),
            [sourceType],
            orderedQueryExpression,
            Expression.Constant(skip)
        );
        var takeOneExpr = Expression.Call(
            typeof(Queryable),
            nameof(Enumerable.Take),
            [sourceType],
            skipExpr,
            Expression.Constant(1)
        );
        return (Task<object>)
            typeof(CursorPaginationExtensions)
                .GetMethod(
                    nameof(SingleSourceEntityCoreAsync),
                    BindingFlags.NonPublic | BindingFlags.Static
                )!
                .MakeGenericMethod(sourceType)
                .Invoke(null, [provider, takeOneExpr, cancellationToken])!;
    }

    private static async Task<object> SingleSourceEntityCoreAsync<TSource>(
        IQueryProvider provider,
        Expression expression,
        CancellationToken cancellationToken
    )
    {
        var query = provider.CreateQuery<TSource>(expression);
        return (await query.SingleAsync(cancellationToken))!;
    }

    private sealed class CursorPageInfo
    {
        public required int Limit { get; init; }
        public required CursorOptions Options { get; init; }
        public required Expression OriginalQueryExpression { get; init; }
        public required Type SourceType { get; init; }
        public required Expression OrderedQueryExpression { get; init; }
        public required Func<object, string> EncodeCursorFromSourceEntity { get; init; }
    }
}
