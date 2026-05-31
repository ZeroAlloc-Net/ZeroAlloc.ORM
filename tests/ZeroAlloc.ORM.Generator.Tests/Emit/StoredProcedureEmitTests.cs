using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.4 Phase D.2 — [StoredProcedure] emit (single result set, no output params).
// Three coverage cells exercise the existing single-result-set shape table with
// the new sproc-flavoured CommandText / CommandType assignment:
//
//   * ScalarInt    — Task<int> on a stored procedure.
//   * FlatRow      — Task<RecordRow?> on a stored procedure.
//   * DomainEntity — Task<ClassRow?> on a stored procedure (column-name keyed).
//
// All three share the open-on-execute / close-on-finally lifecycle and the
// parameter binding shape with their [Query] counterparts; the only difference
// in the emit is the two-line block that sets `__cmd.CommandText = "usp_..."`
// followed immediately by `__cmd.CommandType = CommandType.StoredProcedure;`.
// Standalone Task<List<T>> is not yet a supported single-result-set shape (it
// only appears as a tuple element inside the MultiResultSet path, which Phase
// D.3 covers via the existing dispatch).
public class StoredProcedureEmitTests
{
    [Fact]
    public Task StoredProcedure_Task_int_emits_CommandType_and_ExecuteScalar()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_GetCount")]
                public partial Task<int> GetCountAsync(int customerId, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task StoredProcedure_Task_record_emits_CommandType_and_ExecuteReader()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_GetOrder")]
                public partial Task<OrderRow?> GetOrderAsync(int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task StoredProcedure_Task_domain_entity_emits_CommandType_and_GetOrdinal()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed class CustomerRow
            {
                public CustomerRow(int Id, string Name)
                {
                    this.Id = Id;
                    this.Name = Name;
                }
                public int Id { get; }
                public string Name { get; }
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_GetCustomer")]
                public partial Task<CustomerRow?> GetCustomerAsync(int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
