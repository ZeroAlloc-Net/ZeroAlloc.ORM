using System.Collections;
using System.Data.Common;

namespace ZeroAlloc.ORM.Integration.Tests.TestDoubles;

// #326 — the reader RecordingDbConnection returns for a RETURNING / OUTPUT
// BulkInsert: `count` rows of one non-null int column, valued first, first + 1, ...
// Only what the emitted identity readback calls is implemented.
internal sealed class IdReader(int first, int count) : DbDataReader
{
    private int _row = -1;

    public override int FieldCount => 1;

    public override bool HasRows => count > 0;

    public override bool IsClosed => false;

    public override int RecordsAffected => count;

    public override int Depth => 0;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read() => ++_row < count;

    public override bool NextResult() => false;

    public override int GetInt32(int ordinal) => ordinal == 0 ? first + _row : throw new ArgumentOutOfRangeException(nameof(ordinal));

    public override bool IsDBNull(int ordinal) => false;

    public override string GetName(int ordinal) => "Id";

    public override int GetOrdinal(string name) => 0;

    public override Type GetFieldType(int ordinal) => typeof(int);

    public override string GetDataTypeName(int ordinal) => "int";

    public override object GetValue(int ordinal) => GetInt32(ordinal);

    public override int GetValues(object[] values) => throw new NotSupportedException();

    public override bool GetBoolean(int ordinal) => throw new NotSupportedException();

    public override byte GetByte(int ordinal) => throw new NotSupportedException();

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();

    public override char GetChar(int ordinal) => throw new NotSupportedException();

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();

    public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();

    public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();

    public override double GetDouble(int ordinal) => throw new NotSupportedException();

    public override float GetFloat(int ordinal) => throw new NotSupportedException();

    public override Guid GetGuid(int ordinal) => throw new NotSupportedException();

    public override short GetInt16(int ordinal) => throw new NotSupportedException();

    public override long GetInt64(int ordinal) => throw new NotSupportedException();

    public override string GetString(int ordinal) => throw new NotSupportedException();

    public override IEnumerator GetEnumerator() => throw new NotSupportedException();
}
