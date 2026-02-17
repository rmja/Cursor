using Cursor;
using Example;
using Microsoft.EntityFrameworkCore;
using Refit;

var api = RestService.For<IExampleApi>("https://api.example.com");

var singlePage = await api.ListSomethingAsync();
var allItems = await api.EnumerateSomethingAsync().ToListAsync();
var allPages = await api.EnumerateSomethingPagesAsync().ToListAsync();

await api.EnumerateSomethingAsync(initialCursor: "start_from_here").ToListAsync();
await api.EnumerateSomethingElseAsync().ToListAsync();
await api.EnumerateSomethingThirdAsync().ToListAsync();
await api.EnumerateSomethingFourthAsync().ToListAsync();
await api.EnumerateSomethingFifthAsync(initialOffset: 123).ToListAsync();
await api.EnumerateSomethingFifthPagesAsync(initialOffset: 123).ToListAsync();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<MyDbContext>();

var app = builder.Build();

app.MapGet(
    "/entities",
    async (
        MyDbContext db,
        int limit = 100,
        string? cursor = null,
        CancellationToken cancellationToken = default
    ) =>
    {
        return await db.MyEntities.ToCursorPageAsync(x => x.Id, limit, cursor, cancellationToken);
    }
);

app.Run();

class MyDbContext : DbContext
{
    public MyDbContext(DbContextOptions<MyDbContext> options)
        : base(options) { }

    public DbSet<MyEntity> MyEntities { get; set; }
}

class MyEntity
{
    public int Id { get; set; }
    public required string Name { get; set; }
}
