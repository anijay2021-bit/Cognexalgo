using System.Linq.Expressions;
using AlgoTrader.Core.Interfaces;
using LiteDB;

namespace AlgoTrader.Data.LiteDb;

/// <summary>Generic LiteDB repository implementing IRepository.</summary>
public class LiteRepository<T> : IRepository<T> where T : class
{
    protected readonly ILiteCollection<T> _collection;

    public LiteRepository(LiteDbContext context, string? collectionName = null)
    {
        _collection = context.GetCollection<T>(collectionName);
    }

    public void Upsert(T entity) => _collection.Upsert(entity);
    public void UpsertAll(IEnumerable<T> entities)
    {
        foreach (var entity in entities)
            _collection.Upsert(entity);
    }

    public T? FindById(object id) => _collection.FindById(new BsonValue(id));
    public IEnumerable<T> FindAll() => _collection.FindAll();
    public IEnumerable<T> Find(Expression<Func<T, bool>> predicate) => _collection.Find(predicate);
    public bool Delete(object id) => _collection.Delete(new BsonValue(id));
    public int DeleteMany(Expression<Func<T, bool>> predicate) => _collection.DeleteMany(predicate);
}
