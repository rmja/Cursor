using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Cursor.Tests.EntityFrameworkCore;

/// <summary>
/// The values decoded from a cursor, and the page size, must reach the database as parameters. If
/// they were inlined as literals, every page would have its own SQL text: EF Core would compile
/// the query again for each page, and the database would cache a separate plan for each page.
/// </summary>
public sealed class QueryableToCursorPageParameterizationTests : IAsyncLifetime
{
    private static readonly Guid[] References =
    [
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        Guid.Parse("44444444-4444-4444-4444-444444444444"),
        Guid.Parse("55555555-5555-5555-5555-555555555555"),
        Guid.Parse("66666666-6666-6666-6666-666666666666"),
    ];

    private SqliteConnection _connection = null!;
    private ItemDbContext _db = null!;
    private readonly CommandRecorder _commands = new();
    private readonly List<string> _compilations = [];

    public static CancellationToken CT => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ItemDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_commands)
            .LogTo(_compilations.Add, [CoreEventId.QueryCompilationStarting])
            .Options;

        _db = new ItemDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _db.Items.AddRange(
            new Item(1, "foxtrot", Status.Active, References[3]),
            new Item(2, "alpha", Status.Draft, References[5]),
            new Item(3, "echo", Status.Archived, References[0]),
            new Item(4, "bravo", Status.Draft, References[2]),
            new Item(5, "delta", Status.Active, References[4]),
            new Item(6, "charlie", Status.Archived, References[1])
        );
        await _db.SaveChangesAsync(CT);
        _commands.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task SecondPage_SendsCursorValuesAsParameters()
    {
        // alpha(2), bravo(4), charlie(6), delta(5), echo(3), foxtrot(1)
        var first = await _db
            .Items.OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .ToCursorPageAsync(limit: 2, cancellationToken: CT);
        _commands.Clear();

        var second = await _db
            .Items.OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .ToCursorPageAsync(limit: 2, cursor: first.NextCursor, cancellationToken: CT);

        Assert.Equal([6, 5], second.Items.Select(x => x.Id));
        var command = Assert.Single(_commands.Commands);
        Assert.DoesNotContain("bravo", command.Sql);
        Assert.Contains("bravo", command.ParameterValues);
        Assert.Contains(4, command.ParameterValues);
    }

    [Fact]
    public async Task LaterPages_ShareTheSameSql()
    {
        var pages = await ReadAllPagesAsync(
            () => _db.Items.OrderBy(x => x.Name).ThenBy(x => x.Id),
            limit: 2
        );

        Assert.Equal([2, 4, 6, 5, 3, 1], pages.SelectMany(x => x.Items).Select(x => x.Id));

        // The first page has no cursor filter; every page after it must be the same statement.
        var continuationSql = _commands.Commands.Skip(1).Select(x => x.Sql).Distinct();
        Assert.Single(continuationSql);
    }

    [Fact]
    public async Task LaterPages_ReuseTheCompiledQuery()
    {
        var first = await _db
            .Items.OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .ToCursorPageAsync(limit: 2, cancellationToken: CT);
        var second = await _db
            .Items.OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .ToCursorPageAsync(limit: 2, cursor: first.NextCursor, cancellationToken: CT);

        // The query with a cursor filter has now been compiled once; another cursor must not
        // compile it again.
        _compilations.Clear();
        var third = await _db
            .Items.OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .ToCursorPageAsync(limit: 2, cursor: second.NextCursor, cancellationToken: CT);

        Assert.Equal([3, 1], third.Items.Select(x => x.Id));
        Assert.Empty(_compilations);
    }

    [Fact]
    public async Task DifferentLimits_ShareTheSameSql()
    {
        await _db.Items.OrderBy(x => x.Id).ToCursorPageAsync(limit: 2, cancellationToken: CT);
        await _db.Items.OrderBy(x => x.Id).ToCursorPageAsync(limit: 3, cancellationToken: CT);

        Assert.Equal(2, _commands.Commands.Count);
        Assert.Equal(_commands.Commands[0].Sql, _commands.Commands[1].Sql);
        Assert.Contains(3, _commands.Commands[0].ParameterValues);
        Assert.Contains(4, _commands.Commands[1].ParameterValues);
    }

    [Fact]
    public async Task SplitApi_SecondPage_SendsCursorValuesAsParameters()
    {
        var first = await _db
            .Items.OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .CursorPage(limit: 2)
            .ToCursorPageAsync(CT);

        var queryString = _db
            .Items.OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .CursorPage(limit: 2, cursor: first.NextCursor)
            .ToQueryString();

        // ToQueryString lists the parameter values in a header above the statement itself.
        var lines = queryString.Split('\n');
        var header = string.Join('\n', lines.TakeWhile(x => x.StartsWith(".param")));
        var sql = string.Join('\n', lines.SkipWhile(x => x.StartsWith(".param")));
        Assert.Contains("'bravo'", header);
        Assert.DoesNotContain("bravo", sql);
    }

    [Fact]
    public async Task EnumKey_PagesThroughWithParameters()
    {
        // Draft(2), Draft(4), Active(1), Active(5), Archived(3), Archived(6)
        var pages = await ReadAllPagesAsync(
            () => _db.Items.OrderBy(x => x.Status).ThenBy(x => x.Id),
            limit: 2
        );

        Assert.Equal([2, 4, 1, 5, 3, 6], pages.SelectMany(x => x.Items).Select(x => x.Id));
        Assert.Single(_commands.Commands.Skip(1).Select(x => x.Sql).Distinct());
    }

    [Fact]
    public async Task GuidKey_PagesThroughWithParameters()
    {
        // 1111(3), 2222(6), 3333(4), 4444(1), 5555(5), 6666(2)
        var pages = await ReadAllPagesAsync(
            () => _db.Items.OrderByDescending(x => x.Reference),
            limit: 2
        );

        Assert.Equal([2, 5, 1, 4, 6, 3], pages.SelectMany(x => x.Items).Select(x => x.Id));
        var continuationSql = _commands.Commands.Skip(1).Select(x => x.Sql).Distinct();
        var sql = Assert.Single(continuationSql);
        Assert.All(
            References,
            reference =>
                Assert.DoesNotContain(reference.ToString(), sql, StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task ProjectedQuery_SecondPage_SendsCursorValuesAsParameters()
    {
        var first = await _db
            .Items.OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Select(x => new ItemDto { Id = x.Id })
            .ToCursorPageAsync(limit: 2, cancellationToken: CT);
        _commands.Clear();

        var second = await _db
            .Items.OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Select(x => new ItemDto { Id = x.Id })
            .ToCursorPageAsync(limit: 2, cursor: first.NextCursor, cancellationToken: CT);

        Assert.Equal([6, 5], second.Items.Select(x => x.Id));
        var command = Assert.Single(_commands.Commands);
        Assert.DoesNotContain("bravo", command.Sql);
        Assert.Contains("bravo", command.ParameterValues);
    }

    private async Task<List<CursorPage<T>>> ReadAllPagesAsync<T>(
        Func<IQueryable<T>> query,
        int limit
    )
    {
        var pages = new List<CursorPage<T>>();
        string? cursor = null;
        do
        {
            var page = await query()
                .ToCursorPageAsync(limit: limit, cursor: cursor, cancellationToken: CT);
            pages.Add(page);
            cursor = page.NextCursor;
        } while (cursor is not null);

        return pages;
    }

    private sealed record RecordedCommand(string Sql, IReadOnlyList<object?> ParameterValues);

    private sealed class CommandRecorder : DbCommandInterceptor
    {
        private readonly List<RecordedCommand> _commands = [];

        public IReadOnlyList<RecordedCommand> Commands => _commands;

        public void Clear() => _commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            Record(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Record(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Record(DbCommand command)
        {
            if (!command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.Ordinal))
            {
                return;
            }

            var values = command
                .Parameters.Cast<DbParameter>()
                .Select(x => x.Value is DBNull ? null : x.Value)
                .ToList();
            _commands.Add(new RecordedCommand(command.CommandText, values));
        }
    }

    private sealed class ItemDbContext(DbContextOptions<ItemDbContext> options) : DbContext(options)
    {
        public DbSet<Item> Items { get; set; }
    }

    private enum Status
    {
        Draft = 0,
        Active = 1,
        Archived = 2,
    }

    private sealed class Item
    {
        public Item() { }

        public Item(int id, string name, Status status, Guid reference)
        {
            Id = id;
            Name = name;
            Status = status;
            Reference = reference;
        }

        public int Id { get; set; }
        public string Name { get; set; } = "";
        public Status Status { get; set; }
        public Guid Reference { get; set; }
    }

    private sealed class ItemDto
    {
        public int Id { get; set; }
    }
}
