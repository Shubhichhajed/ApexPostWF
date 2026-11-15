using ApexPost.Application;
using ApexPost.Domain;

namespace ApexPost.UnitTests.Application;

/// <summary>
/// State-based fake repository shared by the application-service tests. A fake is used instead of a mock
/// here because these tests assert on what ends up stored, not on which repository methods were called.
/// </summary>
internal sealed class InMemoryRepository<T>(params T[] seed) : IRepository<T> where T : class, IEntity
{
    public List<T> Items { get; } = seed.ToList();

    public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<T>>(Items);

    public Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(x => x.Id == id));

    public Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(T entity, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        Items.RemoveAll(x => x.Id == id);
        return Task.CompletedTask;
    }
}
