using System;
using System.Linq;
using Xunit;
using ZeroAlloc.ORM;                       // public CommandKind
using ZeroAlloc.ORM.Generator.Model;       // internal CommandKindModel

namespace ZeroAlloc.ORM.Generator.Tests.Model;

// Guards the invariant declared inline at CommandKindModel.BulkInsert:
// "must keep numeric values in sync with public CommandKind". The generator
// casts (CommandKindModel)kindValue where kindValue is read from the public
// enum, so name+value tuples must match across both enums.
public class CommandKindParityTests
{
    [Fact]
    public void Public_and_internal_enums_have_matching_name_value_tuples()
    {
        var publicTuples = Enum.GetValues<CommandKind>()
            .Select(v => (Name: v.ToString(), Value: (int)v))
            .OrderBy(t => t.Value)
            .ToArray();

        var internalTuples = Enum.GetValues<CommandKindModel>()
            .Select(v => (Name: v.ToString(), Value: (int)v))
            .OrderBy(t => t.Value)
            .ToArray();

        Assert.Equal(publicTuples, internalTuples);
    }
}
