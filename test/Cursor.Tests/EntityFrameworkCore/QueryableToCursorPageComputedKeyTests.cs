using System.Data.Common;
using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Cursor.Tests.EntityFrameworkCore;

/// <summary>
/// Ordering keys that the database computes: from navigations, subqueries and fallbacks. The cursor
/// must hold the values the database sorted on, whether or not the navigations are loaded and
/// whatever the query is projected to.
/// </summary>
public sealed class QueryableToCursorPageComputedKeyTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private PlaceDbContext _db = null!;
    private readonly CommandCounter _commands = new();

    public static CancellationToken CT => TestContext.Current.CancellationToken;

    // The display name's sort key: a custom name, else the first address, else the place number.
    private static readonly Expression<Func<Place, string>> NameKey = x =>
        x.Name
        ?? (
            x.Addresses.Count > 0
                ? x.Addresses.OrderBy(a => a.Street).First().Street
                : x.PlaceNumber
        )
        ?? "";

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<PlaceDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_commands)
            .Options;

        _db = new PlaceDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _db.Places.AddRange(
            new Place
            {
                Id = 1,
                PlaceNumber = "PLACE1",
                Addresses =
                {
                    new Address { Id = 1, Street = "vej 002" },
                },
            },
            new Place
            {
                Id = 2,
                Name = "aaa",
                PlaceNumber = "2",
            },
            new Place { Id = 3, PlaceNumber = "90712" },
            new Place
            {
                Id = 4,
                PlaceNumber = "10580",
                Addresses =
                {
                    new Address { Id = 2, Street = "vej 010" },
                    new Address { Id = 3, Street = "a 1" },
                },
            },
            new Place { Id = 5, PlaceNumber = "7649" },
            new Place { Id = 6 },
            new Place
            {
                Id = 7,
                PlaceNumber = "1",
                Addresses =
                {
                    new Address { Id = 4, Street = "vej 002" },
                },
            }
        );
        await _db.SaveChangesAsync(CT);
        _db.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private List<int> ExpectedIds(bool descending = false)
    {
        var query = descending
            ? _db.Places.AsNoTracking().OrderByDescending(NameKey).ThenByDescending(x => x.Id)
            : _db.Places.AsNoTracking().OrderBy(NameKey).ThenBy(x => x.Id);
        return query.Select(x => x.Id).ToList();
    }

    private static async Task<List<TItem>> AllPagesAsync<TItem>(
        Func<string?, Task<CursorPage<TItem>>> getPage
    )
    {
        var items = new List<TItem>();
        string? cursor = null;
        for (var pages = 0; pages < 20; pages++)
        {
            var page = await getPage(cursor);
            items.AddRange(page.Items);
            cursor = page.NextCursor;
            if (cursor is null)
            {
                return items;
            }
        }
        throw new InvalidOperationException("The pages do not end.");
    }

    [Fact]
    public async Task Projected_KeyOverNavigation_PagesInDatabaseOrder()
    {
        var items = await AllPagesAsync(cursor =>
            _db.Places.AsNoTracking()
                .OrderBy(NameKey)
                .ThenBy(x => x.Id)
                .Select(x => new PlaceDto { Id = x.Id, PlaceNumber = x.PlaceNumber })
                .ToCursorPageAsync(limit: 2, cursor: cursor, cancellationToken: CT)
        );

        Assert.Equal(ExpectedIds(), items.Select(x => x.Id));
    }

    [Fact]
    public async Task Projected_KeyOverNavigation_Descending_PagesInDatabaseOrder()
    {
        var items = await AllPagesAsync(cursor =>
            _db.Places.AsNoTracking()
                .OrderByDescending(NameKey)
                .ThenByDescending(x => x.Id)
                .Select(x => new PlaceDto { Id = x.Id })
                .ToCursorPageAsync(limit: 2, cursor: cursor, cancellationToken: CT)
        );

        Assert.Equal(ExpectedIds(descending: true), items.Select(x => x.Id));
    }

    [Fact]
    public async Task Entity_KeyOverNavigation_NotIncluded_PagesInDatabaseOrder()
    {
        var items = await AllPagesAsync(cursor =>
            _db.Places.AsNoTracking()
                .OrderBy(NameKey)
                .ThenBy(x => x.Id)
                .ToCursorPageAsync(limit: 2, cursor: cursor, cancellationToken: CT)
        );

        Assert.Equal(ExpectedIds(), items.Select(x => x.Id));
    }

    [Fact]
    public async Task Entity_CountOfNavigation_NotIncluded_PagesInDatabaseOrder()
    {
        var expected = _db
            .Places.AsNoTracking()
            .OrderByDescending(x => x.Addresses.Count)
            .ThenBy(x => x.Id)
            .Select(x => x.Id)
            .ToList();

        var items = await AllPagesAsync(cursor =>
            _db.Places.AsNoTracking()
                .OrderByDescending(x => x.Addresses.Count)
                .ThenBy(x => x.Id)
                .ToCursorPageAsync(limit: 2, cursor: cursor, cancellationToken: CT)
        );

        Assert.Equal(expected, items.Select(x => x.Id));
    }

    [Fact]
    public async Task Entity_Include_IsStillLoaded()
    {
        var page = await _db
            .Places.AsNoTracking()
            .Include(x => x.Addresses)
            .OrderBy(x => x.Id)
            .ToCursorPageAsync(limit: 4, cancellationToken: CT);

        Assert.Equal([1, 2, 3, 4], page.Items.Select(x => x.Id));
        Assert.Equal(2, page.Items.Single(x => x.Id == 4).Addresses.Count);
    }

    [Fact]
    public async Task ProjectedBeforeAndAfterCursorPage_PagesInDatabaseOrder()
    {
        var items = await AllPagesAsync(cursor =>
            _db.Places.AsNoTracking()
                .OrderBy(NameKey)
                .ThenBy(x => x.Id)
                .Select(x => new PlaceDto { Id = x.Id, PlaceNumber = x.PlaceNumber })
                .CursorPage(limit: 2, cursor: cursor)
                .Select(x => new { x.Id, Label = "#" + x.PlaceNumber })
                .ToCursorPageAsync(CT)
        );

        Assert.Equal(ExpectedIds(), items.Select(x => x.Id));
        Assert.Contains(items, x => x.Id == 3 && x.Label == "#90712");
    }

    [Fact]
    public async Task OrderedAfterProjection_PagesInOrder()
    {
        var expected = _db
            .Places.AsNoTracking()
            .Select(x => new PlaceDto { Id = x.Id, PlaceNumber = x.PlaceNumber })
            .OrderBy(x => x.PlaceNumber)
            .ThenBy(x => x.Id)
            .Select(x => x.Id)
            .ToList();

        var items = await AllPagesAsync(cursor =>
            _db.Places.AsNoTracking()
                .Select(x => new PlaceDto { Id = x.Id, PlaceNumber = x.PlaceNumber })
                .OrderBy(x => x.PlaceNumber)
                .ThenBy(x => x.Id)
                .ToCursorPageAsync(limit: 2, cursor: cursor, cancellationToken: CT)
        );

        Assert.Equal(expected, items.Select(x => x.Id));
    }

    [Fact]
    public async Task APage_IsOneQuery()
    {
        var first = await _db
            .Places.AsNoTracking()
            .OrderBy(NameKey)
            .ThenBy(x => x.Id)
            .Select(x => new PlaceDto { Id = x.Id })
            .ToCursorPageAsync(limit: 2, cancellationToken: CT);
        _commands.Count = 0;

        var second = await _db
            .Places.AsNoTracking()
            .OrderBy(NameKey)
            .ThenBy(x => x.Id)
            .Select(x => new PlaceDto { Id = x.Id })
            .ToCursorPageAsync(limit: 2, cursor: first.NextCursor, cancellationToken: CT);

        Assert.NotNull(second.NextCursor);
        Assert.Equal(1, _commands.Count);
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class PlaceDbContext(DbContextOptions<PlaceDbContext> options)
        : DbContext(options)
    {
        public DbSet<Place> Places { get; set; }
    }

    private sealed class Place
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? PlaceNumber { get; set; }
        public List<Address> Addresses { get; set; } = [];
    }

    private sealed class Address
    {
        public int Id { get; set; }
        public int PlaceId { get; set; }
        public string Street { get; set; } = "";
    }

    private sealed class PlaceDto
    {
        public int Id { get; set; }
        public string? PlaceNumber { get; set; }
    }
}
