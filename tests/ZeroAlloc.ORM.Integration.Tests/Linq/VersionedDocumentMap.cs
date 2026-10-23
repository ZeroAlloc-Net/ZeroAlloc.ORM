namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// "VersionedDocuments" in the default schema; every column is named after its property.
[TableMap(typeof(VersionedDocumentEntity), "VersionedDocuments")]
internal static class VersionedDocumentMap;
