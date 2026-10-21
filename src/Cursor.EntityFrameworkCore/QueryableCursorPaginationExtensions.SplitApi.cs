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
/// <para>
/// The values for the next cursor are read from the database in the same query as the page, as the
/// database computes the ordering keys. Keys may therefore be computed from navigations or subqueries
/// (<c>OrderBy(x =&gt; x.Name ?? x.Addresses.OrderBy(a =&gt; a.Street).First().Street)</c>), without
/// the navigations being loaded and whatever the query is projected to.
/// </para>
/// </remarks>
public static partial class QueryableCursorPaginationExtensions
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

        var orderingInfo =
            OrderingHelpers.ExtractOrderingInfo(query.Expression)
            ?? throw new InvalidOperationException(
                "CursorPage requires the query to be ordered. Apply OrderBy/OrderByDescending "
                    + "(and optionally ThenBy/ThenByDescending) to the query before calling CursorPage. "
                    + "A Select projection between the ordering chain and CursorPage is allowed."
            );
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
                PagedExpression = paged.Expression,
                Keys = orderingInfo.Keys,
                Limit = limit,
                Options = options,
                OriginalQueryExpression = originalExpression,
                QueryElementType = typeof(T),
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
                info.QueryElementType,
                cancellationToken
            );
        }

        var limit = info.Limit;

        // Preferably the page and the key values of its rows in one query, the keys computed by the
        // database exactly as it sorted and filtered on them.
        var keyedQuery = BuildKeyedQuery<T>(query.Provider, query.Expression, info);
        if (keyedQuery is not null)
        {
            var rows = await keyedQuery.ToListAsync(cancellationToken);
            var rowsHaveMore = rows.Count > limit;
            if (rowsHaveMore)
            {
                rows.RemoveAt(rows.Count - 1);
            }

            return new CursorPage<T>
            {
                Items = rows.Select(x => x.Item).ToList(),
                NextCursor =
                    (rowsHaveMore || rows.Count == int.MaxValue) && rows.Count > 0
                        ? info.Options.CursorSerializer.EncodeCompoundCursor([.. rows[^1].Keys])
                        : null,
                TotalCount = totalCount,
            };
        }

        // Operators after CursorPage other than Select: evaluate the keys in memory on the last
        // source entity. Only exact for keys that are plain columns of a loaded entity.
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

    /// <summary>
    /// Builds <c>ordered.Select(x =&gt; new CursorRow&lt;T&gt; { Item = projection(x), Keys = [k0(x), …] }).Take(n)</c>:
    /// the page with the key values of each row, in a single query. <c>projection</c> is the
    /// composition of the <c>Select</c>s between the ordering chain and the final query, on either
    /// side of <see cref="CursorPage{T}"/>.
    /// </summary>
    /// <returns><c>null</c> when the final query has other operators than <c>Select</c> on top of the ordering.</returns>
    private static IQueryable<CursorRow<T>>? BuildKeyedQuery<T>(
        IQueryProvider provider,
        Expression finalExpression,
        CursorPageInfo info
    )
    {
        // Select(Take(Select(ordered)))…: collect the projections, outermost first.
        var projections = new List<LambdaExpression>();
        var current = finalExpression;
        while (TryGetSelector(current, out var selector, out var source))
        {
            projections.Add(selector);
            current = source;
        }
        if (current != info.PagedExpression)
        {
            return null;
        }

        current = ((MethodCallExpression)current).Arguments[0];
        while (current != info.OrderedQueryExpression)
        {
            if (!TryGetSelector(current, out var selector, out var source))
            {
                return null;
            }
            projections.Add(selector);
            current = source;
        }

        var parameter = Expression.Parameter(info.SourceType, "x");
        Expression item = parameter;
        for (var i = projections.Count - 1; i >= 0; i--)
        {
            item = OrderingHelpers.Inline(projections[i], item);
        }
        if (item.Type != typeof(T))
        {
            return null;
        }

        var keys = OrderingHelpers
            .RewriteKeyBodies(info.Keys, parameter)
            .Select(x => Expression.Convert(x, typeof(object)));
        var row = Expression.MemberInit(
            Expression.New(typeof(CursorRow<T>)),
            Expression.Bind(typeof(CursorRow<T>).GetProperty(nameof(CursorRow<T>.Item))!, item),
            Expression.Bind(
                typeof(CursorRow<T>).GetProperty(nameof(CursorRow<T>.Keys))!,
                Expression.NewArrayInit(typeof(object), keys)
            )
        );
        var select = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Select),
            [info.SourceType, typeof(CursorRow<T>)],
            info.OrderedQueryExpression,
            Expression.Quote(Expression.Lambda(row, parameter))
        );
        var take = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Take),
            [typeof(CursorRow<T>)],
            select,
            Expression.Constant(info.Limit < int.MaxValue ? info.Limit + 1 : int.MaxValue)
        );
        return provider.CreateQuery<CursorRow<T>>(take);
    }

    private static bool TryGetSelector(
        Expression expression,
        out LambdaExpression selector,
        out Expression source
    )
    {
        if (
            expression
                is MethodCallExpression
                {
                    Method.Name: nameof(Queryable.Select),
                    Arguments: [var selectSource, var quoted],
                } call
            && call.Method.DeclaringType == typeof(Queryable)
            && quoted
                is UnaryExpression { Operand: LambdaExpression { Parameters.Count: 1 } lambda }
        )
        {
            selector = lambda;
            source = selectSource;
            return true;
        }

        selector = null!;
        source = null!;
        return false;
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
            typeof(QueryableCursorPaginationExtensions)
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
            typeof(QueryableCursorPaginationExtensions)
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

    /// <summary>A row of the keyed page query: the item and the values of its ordering keys.</summary>
    private sealed class CursorRow<TItem>
    {
        public TItem Item { get; init; } = default!;
        public object?[] Keys { get; init; } = [];
    }

    private sealed class CursorPageInfo
    {
        /// <summary>The <c>Take</c> that <see cref="CursorPage{T}"/> returns.</summary>
        public required Expression PagedExpression { get; init; }

        public required IReadOnlyList<OrderingKey> Keys { get; init; }
        public required int Limit { get; init; }
        public required CursorOptions Options { get; init; }
        public required Expression OriginalQueryExpression { get; init; }
        public required Type QueryElementType { get; init; }
        public required Type SourceType { get; init; }
        public required Expression OrderedQueryExpression { get; init; }
        public required Func<object, string> EncodeCursorFromSourceEntity { get; init; }
    }
}
