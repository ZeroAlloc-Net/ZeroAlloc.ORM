namespace ZeroAlloc.ORM;

/// <summary>Execution mode for <see cref="CommandAttribute"/>-annotated methods.</summary>
public enum CommandKind
{
    /// <summary>ExecuteNonQuery — returns affected-row count.</summary>
    NonQuery,

    /// <summary>ExecuteScalar — returns the first column of the first row.</summary>
    Scalar,

    /// <summary>ExecuteScalar over an identity-returning fragment (e.g. <c>OUTPUT INSERTED.Id</c> / <c>RETURNING id</c>).</summary>
    Identity,
}
