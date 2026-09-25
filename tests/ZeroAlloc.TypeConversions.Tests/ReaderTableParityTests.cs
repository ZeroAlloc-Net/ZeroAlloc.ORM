using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.TypeConversions.Tests;

// v2.0, #235 — PrimitiveCatalog keys two inverse tables on the reader method that
// GetScalarReaderMethod returns: GetScalarCastTypeFromReader and
// GetDbTypeNameFromReader. Both throw on a reader they do not know, and inside the
// generator that throw becomes CS8785, which drops every generated file. A new
// entry in GetScalarReaderMethod must therefore land in both inverse tables.
//
// The sweep asks GetScalarReaderMethod about every special type and a list of
// well-known framework types, including the deferred ones, so a reader added for
// any of them later is checked here without editing this test.
public class ReaderTableParityTests
{
    private static readonly string[] WellKnownTypes =
    [
        "System.Guid",
        "System.DateTimeOffset",
        "System.TimeSpan",
        "System.DateOnly",
        "System.TimeOnly",
        "System.Half",
        "System.Int128",
        "System.UInt128",
        "System.Numerics.BigInteger",
    ];

    public static TheoryData<string> Readers()
    {
        var data = new TheoryData<string>();
        foreach (var reader in SweepReaders()) data.Add(reader);
        return data;
    }

    private static string[] SweepReaders()
    {
        var compilation = TypeFixture.CreateCompilation("public class Anchor {}");
        var types = new List<ITypeSymbol>();
        foreach (var special in Enum.GetValues<SpecialType>())
        {
            if (special == SpecialType.None) continue;
            var type = compilation.GetSpecialType(special);
            if (type.TypeKind != TypeKind.Error) types.Add(type);
        }
        foreach (var name in WellKnownTypes)
        {
            if (compilation.GetTypeByMetadataName(name) is { } type) types.Add(type);
        }
        foreach (var element in types.ToArray())
        {
            if (element.TypeKind != TypeKind.Error && !element.IsStatic)
                types.Add(compilation.CreateArrayTypeSymbol(element));
        }

        var readers = new SortedSet<string>(StringComparer.Ordinal)
        {
            // The generator hands GetString to both tables for a [StoreAsString] enum.
            "GetString",
        };
        foreach (var type in types)
        {
            if (PrimitiveCatalog.GetScalarReaderMethod(type) is { } reader) readers.Add(reader);
        }

        return readers.ToArray();
    }

    [Theory]
    [MemberData(nameof(Readers))]
    public void Every_reader_has_a_scalar_cast_type(string reader)
    {
        var act = () => PrimitiveCatalog.GetScalarCastTypeFromReader(reader);

        act.Should().NotThrow().Which.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(Readers))]
    public void Every_reader_has_a_DbType(string reader)
    {
        var act = () => PrimitiveCatalog.GetDbTypeNameFromReader(reader);

        var name = act.Should().NotThrow().Which;
        Enum.IsDefined(typeof(System.Data.DbType), name).Should().BeTrue(
            "'{0}' must name a System.Data.DbType member, because the generator emits it as one", name);
    }

    // Guards the sweep itself: if it stopped finding readers, the two theories
    // above would pass without checking anything.
    [Fact]
    public void Sweep_finds_every_v1_primitive_reader()
    {
        var readers = SweepReaders();

        readers.Should().Contain(
        [
            "GetInt32", "GetInt64", "GetInt16", "GetByte", "GetBoolean", "GetDecimal",
            "GetDouble", "GetFloat", "GetString", "GetDateTime", "GetGuid",
            "GetFieldValue<global::System.DateTimeOffset>",
            "GetFieldValue<global::System.TimeSpan>",
            "GetFieldValue<byte[]>",
        ]);
    }
}
