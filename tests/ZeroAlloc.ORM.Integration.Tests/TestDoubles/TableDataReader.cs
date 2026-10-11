using System.Collections;
using System.Data.Common;

namespace ZeroAlloc.ORM.Integration.Tests.TestDoubles;

// #326 — a reader over canned result sets that counts its GetOrdinal calls, so
// a test can prove the emitted materializer resolves each column name once per
// result set rather than once per row. A cell holding DBNull.Value reads as
// NULL: IsDBNull is true and the typed getters throw InvalidCastException, as
// a provider's do.
internal sealed class TableDataReader(params TableResult[] results) : DbDataReader
{
    private int _result;
    private int _row = -1;

    public int GetOrdinalCalls { get; private set; }

    private TableResult Current => results[_result];

    public override int FieldCount => Current.Columns.Count;

    public override bool HasRows => Current.Rows.Count > 0;

    public override bool IsClosed => false;

    public override int RecordsAffected => -1;

    public override int Depth => 0;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read() => ++_row < Current.Rows.Count;

    public override bool NextResult()
    {
        if (_result + 1 >= results.Length) return false;
        _result++;
        _row = -1;
        return true;
    }

    public override int GetOrdinal(string name)
    {
        GetOrdinalCalls++;
        for (var i = 0; i < Current.Columns.Count; i++)
        {
            if (string.Equals(Current.Columns[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        throw new ArgumentOutOfRangeException(nameof(name), name, "No such column.");
    }

    public override string GetName(int ordinal) => Current.Columns[ordinal];

    public override object GetValue(int ordinal) => Current.Rows[_row][ordinal];

    public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;

    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);

    public override string GetString(int ordinal) => (string)GetValue(ordinal);

    public override Type GetFieldType(int ordinal) => typeof(object);

    public override string GetDataTypeName(int ordinal) => "object";

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

    public override IEnumerator GetEnumerator() => throw new NotSupportedException();
}
