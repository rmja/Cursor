using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cursor.Tests.EntityFrameworkCore;

public class ToCursorPageTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TestDbContext _db = null!;

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TestDbContext(options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Ascending_FirstPage()
    {
        _db.Items.AddRange(
            new TestEntity { Id = 1, Name = "A" },
            new TestEntity { Id = 2, Name = "B" },
            new TestEntity { Id = 3, Name = "C" },
            new TestEntity { Id = 4, Name = "D" },
            new TestEntity { Id = 5, Name = "E" }
        );
        await _db.SaveChangesAsync();

        var page = await _db.Items.ToCursorPageAsync(x => x.Id, limit: 3);

        Assert.Equal([1, 2, 3], page.Items.Select(x => x.Id));
        Assert.NotNull(page.NextCursor);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task Ascending_SecondPage()
    {
        _db.Items.AddRange(
            new TestEntity { Id = 1, Name = "A" },
            new TestEntity { Id = 2, Name = "B" },
            new TestEntity { Id = 3, Name = "C" },
            new TestEntity { Id = 4, Name = "D" },
            new TestEntity { Id = 5, Name = "E" }
        );
        await _db.SaveChangesAsync();

        var first = await _db.Items.ToCursorPageAsync(x => x.Id, limit: 3);
        var second = await _db.Items.ToCursorPageAsync(x => x.Id, limit: 3, cursor: first.NextCursor);

        Assert.Equal([4, 5], second.Items.Select(x => x.Id));
        Assert.Null(second.NextCursor);
        Assert.False(second.HasMore);
    }

    [Fact]
    public async Task Descending_FirstPage()
    {
        _db.Items.AddRange(
            new TestEntity { Id = 1, Name = "A" },
            new TestEntity { Id = 2, Name = "B" },
            new TestEntity { Id = 3, Name = "C" },
            new TestEntity { Id = 4, Name = "D" },
            new TestEntity { Id = 5, Name = "E" }
        );
        await _db.SaveChangesAsync();

        var page = await _db.Items.ToCursorPageDescendingAsync(x => x.Id, limit: 3);

        Assert.Equal([5, 4, 3], page.Items.Select(x => x.Id));
        Assert.NotNull(page.NextCursor);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task Descending_SecondPage()
    {
        _db.Items.AddRange(
            new TestEntity { Id = 1, Name = "A" },
            new TestEntity { Id = 2, Name = "B" },
            new TestEntity { Id = 3, Name = "C" },
            new TestEntity { Id = 4, Name = "D" },
            new TestEntity { Id = 5, Name = "E" }
        );
        await _db.SaveChangesAsync();

        var first = await _db.Items.ToCursorPageDescendingAsync(x => x.Id, limit: 3);
        var second = await _db.Items.ToCursorPageDescendingAsync(x => x.Id, limit: 3, cursor: first.NextCursor);

        Assert.Equal([2, 1], second.Items.Select(x => x.Id));
        Assert.Null(second.NextCursor);
        Assert.False(second.HasMore);
    }

    [Fact]
    public async Task Ascending_EmptyResult()
    {
        var page = await _db.Items.ToCursorPageAsync(x => x.Id, limit: 10);

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Ascending_ExactLimit_NoMorePages()
    {
        _db.Items.AddRange(
            new TestEntity { Id = 1, Name = "A" },
            new TestEntity { Id = 2, Name = "B" },
            new TestEntity { Id = 3, Name = "C" }
        );
        await _db.SaveChangesAsync();

        var page = await _db.Items.ToCursorPageAsync(x => x.Id, limit: 3);

        Assert.Equal([1, 2, 3], page.Items.Select(x => x.Id));
        Assert.Null(page.NextCursor);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task CompoundKey_Ascending_FirstPage()
    {
        _db.Items.AddRange(
            new TestEntity { Id = 1, Name = "B", CategoryId = 1 },
            new TestEntity { Id = 2, Name = "A", CategoryId = 1 },
            new TestEntity { Id = 3, Name = "C", CategoryId = 2 },
            new TestEntity { Id = 4, Name = "D", CategoryId = 2 },
            new TestEntity { Id = 5, Name = "E", CategoryId = 3 }
        );
        await _db.SaveChangesAsync();

        var page = await _db.Items.ToCursorPageAsync(x => new { x.CategoryId, x.Id }, limit: 3);

        Assert.Equal([1, 2, 3], page.Items.Select(x => x.Id));
        Assert.NotNull(page.NextCursor);
    }

    [Fact]
    public async Task CompoundKey_Ascending_SecondPage()
    {
        _db.Items.AddRange(
            new TestEntity { Id = 1, Name = "B", CategoryId = 1 },
            new TestEntity { Id = 2, Name = "A", CategoryId = 1 },
            new TestEntity { Id = 3, Name = "C", CategoryId = 2 },
            new TestEntity { Id = 4, Name = "D", CategoryId = 2 },
            new TestEntity { Id = 5, Name = "E", CategoryId = 3 }
        );
        await _db.SaveChangesAsync();

        var first = await _db.Items.ToCursorPageAsync(x => new { x.CategoryId, x.Id }, limit: 3);
        var second = await _db.Items.ToCursorPageAsync(x => new { x.CategoryId, x.Id }, limit: 3, cursor: first.NextCursor);

        Assert.Equal([4, 5], second.Items.Select(x => x.Id));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task CompoundKey_Descending_FirstPage()
    {
        _db.Items.AddRange(
            new TestEntity { Id = 1, Name = "B", CategoryId = 1 },
            new TestEntity { Id = 2, Name = "A", CategoryId = 1 },
            new TestEntity { Id = 3, Name = "C", CategoryId = 2 },
            new TestEntity { Id = 4, Name = "D", CategoryId = 2 },
            new TestEntity { Id = 5, Name = "E", CategoryId = 3 }
        );
        await _db.SaveChangesAsync();

        var page = await _db.Items.ToCursorPageDescendingAsync(x => new { x.CategoryId, x.Id }, limit: 3);

        Assert.Equal([5, 4, 3], page.Items.Select(x => x.Id));
        Assert.NotNull(page.NextCursor);
    }

    [Fact]
    public async Task CompoundKey_Descending_SecondPage()
    {
        _db.Items.AddRange(
            new TestEntity { Id = 1, Name = "B", CategoryId = 1 },
            new TestEntity { Id = 2, Name = "A", CategoryId = 1 },
            new TestEntity { Id = 3, Name = "C", CategoryId = 2 },
            new TestEntity { Id = 4, Name = "D", CategoryId = 2 },
            new TestEntity { Id = 5, Name = "E", CategoryId = 3 }
        );
        await _db.SaveChangesAsync();

        var first = await _db.Items.ToCursorPageDescendingAsync(x => new { x.CategoryId, x.Id }, limit: 3);
        var second = await _db.Items.ToCursorPageDescendingAsync(x => new { x.CategoryId, x.Id }, limit: 3, cursor: first.NextCursor);

        Assert.Equal([2, 1], second.Items.Select(x => x.Id));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task ComputeTotalCount_ReturnsCount()
    {
        _db.Items.AddRange(
            new TestEntity { Id = 1, Name = "A" },
            new TestEntity { Id = 2, Name = "B" },
            new TestEntity { Id = 3, Name = "C" },
            new TestEntity { Id = 4, Name = "D" },
            new TestEntity { Id = 5, Name = "E" }
        );
        await _db.SaveChangesAsync();

        var page = await _db.Items.ToCursorPageAsync(x => x.Id, limit: 3, computeTotalCount: true);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal([1, 2, 3], page.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task ComputeTotalCount_False_ReturnsNull()
    {
        _db.Items.AddRange(
            new TestEntity { Id = 1, Name = "A" },
            new TestEntity { Id = 2, Name = "B" }
        );
        await _db.SaveChangesAsync();

        var page = await _db.Items.ToCursorPageAsync(x => x.Id, limit: 10);

        Assert.Null(page.TotalCount);
    }

    [Fact]
    public async Task ComputeTotalCount_Descending_ReturnsCount()
    {
        _db.Items.AddRange(
            new TestEntity { Id = 1, Name = "A" },
            new TestEntity { Id = 2, Name = "B" },
            new TestEntity { Id = 3, Name = "C" }
        );
        await _db.SaveChangesAsync();

        var page = await _db.Items.ToCursorPageDescendingAsync(x => x.Id, limit: 2, computeTotalCount: true);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal([3, 2], page.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task ComputeTotalCount_CompoundKey_ReturnsCount()
    {
        _db.Items.AddRange(
            new TestEntity { Id = 1, Name = "A", CategoryId = 1 },
            new TestEntity { Id = 2, Name = "B", CategoryId = 1 },
            new TestEntity { Id = 3, Name = "C", CategoryId = 2 }
        );
        await _db.SaveChangesAsync();

        var page = await _db.Items.ToCursorPageAsync(
            x => new { x.CategoryId, x.Id }, limit: 2, computeTotalCount: true);

        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task ComputeTotalCount_WithPreFilter_CountsFilteredSet()
    {
        _db.Items.AddRange(
            new TestEntity { Id = 1, Name = "A", CategoryId = 1 },
            new TestEntity { Id = 2, Name = "B", CategoryId = 2 },
            new TestEntity { Id = 3, Name = "C", CategoryId = 1 },
            new TestEntity { Id = 4, Name = "D", CategoryId = 2 },
            new TestEntity { Id = 5, Name = "E", CategoryId = 1 }
        );
        await _db.SaveChangesAsync();

        var page = await _db.Items
            .Where(x => x.CategoryId == 1)
            .ToCursorPageAsync(x => x.Id, limit: 2, computeTotalCount: true);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal([1, 3], page.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task ComputeTotalCount_SecondPage_StillReturnsTotalCount()
    {
        _db.Items.AddRange(
            new TestEntity { Id = 1, Name = "A" },
            new TestEntity { Id = 2, Name = "B" },
            new TestEntity { Id = 3, Name = "C" },
            new TestEntity { Id = 4, Name = "D" },
            new TestEntity { Id = 5, Name = "E" }
        );
        await _db.SaveChangesAsync();

        var first = await _db.Items.ToCursorPageAsync(x => x.Id, limit: 3, computeTotalCount: true);
        var second = await _db.Items.ToCursorPageAsync(x => x.Id, limit: 3, cursor: first.NextCursor, computeTotalCount: true);

        Assert.Equal(5, first.TotalCount);
        Assert.Equal(5, second.TotalCount);
        Assert.Equal([4, 5], second.Items.Select(x => x.Id));
    }

    private class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<TestEntity> Items { get; set; }
    }

    private class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int CategoryId { get; set; }
    }
}
