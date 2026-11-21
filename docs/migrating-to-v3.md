# Migrating to ZeroAlloc.ORM v3

v3 has one breaking change, in the code the generator emits for five scalar return types.
Everything else in v2.x carries forward unchanged. The runtime packages target `net10.0` and
`net11.0`, as they did in v2.

## A non-nullable `int`, `long`, `bool`, `double` or `decimal` scalar throws on no row

### Why

[#409](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/issues/409): a `[Query]` returning
`Task<int>`, `Task<long>`, `Task<bool>`, `Task<double>` or `Task<decimal>`, or the `ValueTask`
form, read its value with `ExecuteScalarAsync` and `System.Convert`. When the query returned no
row, `Convert` turned the missing value into `0` or `false`, so the method answered as if a row
held that value. A NULL value threw a bare `InvalidCastException` that named nothing. Every other
non-nullable scalar, such as `Task<Guid>`, `Task<string>` or `Task<DateTimeOffset>`, already threw
`ZeroAllocOrmMaterializationException` for both.

v3 holds the five types to the same rule. No row and NULL each throw
`ZeroAllocOrmMaterializationException`, and the message names the method and the type:

```text
Query 'MyApp.OrderRepo.GetTotalAsync' returned no row, but its return type is the non-nullable 'decimal'. Declare it as 'decimal?' to receive null.
```

### Who is affected

Callers of a non-nullable `int`, `long`, `bool`, `double` or `decimal` `[Query]` whose SQL can
return no row or NULL, and code that catches `InvalidCastException` from one.

Not affected:

- `SELECT COUNT(*)`, `SELECT EXISTS (...)` and other SQL that always returns one non-NULL value.
- `[LinqQuery]` `Count`, `LongCount` and `Any`, which always return a row, and `Sum`, which is
  COALESCEd to `0`. `Average`, `Min` and `Max` already threw `InvalidOperationException`, as LINQ
  does, and still do.
- `[Command(Kind = Scalar)]` and `[Command(Kind = Identity)]`, which already threw.

### Before (v2.x)

```csharp
[Query("SELECT total FROM orders WHERE id = @id")]
public partial Task<decimal> GetTotalAsync(int id, CancellationToken ct);

var total = await repo.GetTotalAsync(missingId, ct); // 0m
```

### After (v3)

Declare the nullable type when no row is an expected answer, and map null where you need a
default:

```csharp
[Query("SELECT total FROM orders WHERE id = @id")]
public partial Task<decimal?> GetTotalAsync(int id, CancellationToken ct);

var total = await repo.GetTotalAsync(missingId, ct) ?? 0m;
```

Or make the SQL return a value for every input, for example with `COALESCE`:

```sql
SELECT COALESCE((SELECT total FROM orders WHERE id = @id), 0)
```

Keep the non-nullable type when a missing row is a bug, and catch
`ZeroAllocOrmMaterializationException` where you used to catch `InvalidCastException`.
