using Cursor.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cursor.Tests.EntityFrameworkCore;

/// <summary>
/// Paging by key types that an expression tree cannot compare with a plain greater-than.
/// <see cref="string"/>, <see cref="Guid"/>, <see cref="bool"/> and enums define no
/// <c>op_GreaterThan</c> operator, so building the cursor filter for one of them used to fail with
/// "The binary operator GreaterThan is not defined for the types ..." as soon as a cursor was
/// passed - that is, on the second page and never on the first.
/// </summary>
public sealed class QueryableToCursorPageKeyTypeTests : IAsyncLifetime
{
    private static readonly Guid[] References =
    [
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        Guid.Parse("44444444-4444-4444-4444-444444444444"),
        Guid.Parse("55555555-5555-5555-5555-555555555555"),
    ];

    private SqliteConnection _connection = null!;
    private KeyTypeDbContext _db = null!;

    public static CancellationToken CT => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<KeyTypeDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new KeyTypeDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        // Deliberately seeded so that no key orders the rows the same way Id does, and so that
        // Category, Status and Flag all contain ties that the Id tie-breaker has to resolve.
        _db.Items.AddRange(
            NewItem(1, "delta", "x", References[2], Status.Active, true, 3),
            NewItem(2, "alpha", "x", References[4], Status.Draft, false, 1),
            NewItem(3, "echo", "y", References[0], Status.Archived, true, 5),
            NewItem(4, "bravo", "y", References[3], Status.Draft, false, 2),
            NewItem(5, "charlie", "y", References[1], Status.Active, true, 4)
        );
        await _db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task StringKey_SecondPage_ContinuesAfterTheCursor()
    {
        // alpha(2), bravo(4), charlie(5), delta(1), echo(3)
        var first = await _db
            .Items.OrderBy(x => x.Name)
            .ToCursorPageAsync(limit: 2, cancellationToken: CT);
        Assert.Equal([2, 4], first.Items.Select(x => x.Id));
        Assert.NotNull(first.NextCursor);

        var second = await _db
            .Items.OrderBy(x => x.Name)
            .ToCursorPageAsync(limit: 2, cursor: first.NextCursor, cancellationToken: CT);

        Assert.Equal([5, 1], second.Items.Select(x => x.Id));
        Assert.True(second.HasMore);
    }

    [Fact]
    public async Task StringKey_Descending_SecondPage_ContinuesAfterTheCursor()
    {
        // echo(3), delta(1), charlie(5), bravo(4), alpha(2)
        var first = await _db
            .Items.OrderByDescending(x => x.Name)
            .ToCursorPageAsync(limit: 2, cancellationToken: CT);
        Assert.Equal([3, 1], first.Items.Select(x => x.Id));

        var second = await _db
            .Items.OrderByDescending(x => x.Name)
            .ToCursorPageAsync(limit: 2, cursor: first.NextCursor, cancellationToken: CT);

        Assert.Equal([5, 4], second.Items.Select(x => x.Id));
    }

    [Fact]
    public Task StringKey_PagesThroughEveryRowExactlyOnce() =>
        AssertPagesMatchOrdering(() => _db.Items.OrderBy(x => x.Name));

    [Fact]
    public Task StringKey_Descending_PagesThroughEveryRowExactlyOnce() =>
        AssertPagesMatchOrdering(() => _db.Items.OrderByDescending(x => x.Name));

    /// <summary>
    /// A tied string key exercises the equality part of the lexicographic comparison as well as
    /// the greater-than part: <c>Category = @c AND Id &gt; @id</c>.
    /// </summary>
    [Fact]
    public Task CompoundStringAndIntKey_PagesThroughEveryRowExactlyOnce() =>
        AssertPagesMatchOrdering(() => _db.Items.OrderBy(x => x.Category).ThenBy(x => x.Id));

    [Fact]
    public Task CompoundStringAndIntKey_Descending_PagesThroughEveryRowExactlyOnce() =>
        AssertPagesMatchOrdering(() =>
            _db.Items.OrderByDescending(x => x.Category).ThenByDescending(x => x.Id)
        );

    [Fact]
    public Task GuidKey_PagesThroughEveryRowExactlyOnce() =>
        AssertPagesMatchOrdering(() => _db.Items.OrderBy(x => x.Reference));

    [Fact]
    public Task EnumKey_PagesThroughEveryRowExactlyOnce() =>
        AssertPagesMatchOrdering(() => _db.Items.OrderBy(x => x.Status).ThenBy(x => x.Id));

    [Fact]
    public Task BoolKey_PagesThroughEveryRowExactlyOnce() =>
        AssertPagesMatchOrdering(() => _db.Items.OrderBy(x => x.Flag).ThenBy(x => x.Id));

    /// <summary>
    /// A key type that does define the operator, so it takes the other branch. Guards against a
    /// fix for the types above regressing the types that already worked.
    /// </summary>
    /// <remarks>
    /// <see cref="DateTimeOffset"/> would be the more interesting case here, but SQLite rejects
    /// it outright - "SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY
    /// clauses" - as it does <see cref="TimeSpan"/> and <see cref="ulong"/>. That is a provider
    /// limitation on ordering itself, reached before any cursor is involved, so it cannot be
    /// covered from here.
    /// </remarks>
    [Fact]
    public Task DateTimeKey_PagesThroughEveryRowExactlyOnce() =>
        AssertPagesMatchOrdering(() => _db.Items.OrderBy(x => x.CreatedAt));

    [Fact]
    public Task IntKey_PagesThroughEveryRowExactlyOnce() =>
        AssertPagesMatchOrdering(() => _db.Items.OrderBy(x => x.Id));

    /// <summary>
    /// Pages through <paramref name="ordered"/> one cursor at a time and asserts that the rows come
    /// back in exactly the order the database itself returns them - no row skipped, none repeated.
    /// </summary>
    private async Task AssertPagesMatchOrdering(
        Func<IOrderedQueryable<KeyTypeEntity>> ordered,
        int limit = 2
    )
    {
        var expected = await ordered().Select(x => x.Id).ToListAsync(CT);

        var actual = new List<int>();
        string? cursor = null;
        for (var page = 0; ; page++)
        {
            Assert.True(page < 20, "Pagination did not terminate; the cursor is not advancing.");

            var current = await ordered()
                .ToCursorPageAsync(limit: limit, cursor: cursor, cancellationToken: CT);
            actual.AddRange(current.Items.Select(x => x.Id));

            cursor = current.NextCursor;
            if (cursor is null)
            {
                break;
            }
        }

        Assert.Equal(expected, actual);
    }

    private static KeyTypeEntity NewItem(
        int id,
        string name,
        string category,
        Guid reference,
        Status status,
        bool flag,
        int createdAtMonth
    ) =>
        new()
        {
            Id = id,
            Name = name,
            Category = category,
            Reference = reference,
            Status = status,
            Flag = flag,
            CreatedAt = new DateTime(2024, createdAtMonth, 1, 0, 0, 0, DateTimeKind.Utc),
        };

    private enum Status
    {
        Draft = 0,
        Active = 1,
        Archived = 2,
    }

    private class KeyTypeDbContext(DbContextOptions<KeyTypeDbContext> options) : DbContext(options)
    {
        public DbSet<KeyTypeEntity> Items { get; set; }
    }

    private class KeyTypeEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public Guid Reference { get; set; }
        public Status Status { get; set; }
        public bool Flag { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
