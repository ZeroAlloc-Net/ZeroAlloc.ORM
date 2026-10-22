namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// A schema shaped like Daedalus' EF-created one: quoted PascalCase names, a reserved word
// ("Order") as a column, a typed id, an enum stored as int and one stored as text, a text[],
// a nullable timestamptz, and nullable int, real and int-enum columns for the null semantics.
// "Projects" lives in its own schema so the map's Schema is used. "Executions" is Daedalus'
// task-execution log, which the aggregates read, and "Sessions" its chat sessions, which the
// grouped token report reads.
internal static class LinqSchema
{
    public const string Ddl = """
        CREATE SCHEMA app;
        CREATE TABLE app."Projects" (
            "Id" uuid PRIMARY KEY,
            "Name" text NOT NULL,
            "Order" int NOT NULL
        );
        CREATE TABLE "Tasks" (
            "Id" uuid PRIMARY KEY,
            "ProjectId" uuid NOT NULL,
            "Title" text NOT NULL,
            "Status" int NOT NULL,
            "Kind" text NOT NULL,
            "CreatedAt" timestamptz NOT NULL,
            "DueAt" timestamptz NULL,
            "Tags" text[] NOT NULL,
            "Order" int NOT NULL,
            "StartedAt" timestamptz NULL,
            "Priority" int NULL,
            "Rating" real NULL,
            "Category" int NULL
        );
        CREATE TABLE "Executions" (
            "Id" uuid PRIMARY KEY,
            "TaskId" uuid NOT NULL,
            "ModelId" text NULL,
            "InputTokens" int NOT NULL,
            "OutputTokens" int NOT NULL,
            "ExecutedAt" timestamptz NOT NULL,
            "Sequence" int NULL,
            "TotalTokens" bigint NULL,
            "Cost" numeric NULL,
            "Weight" real NULL
        );
        CREATE TABLE "Sessions" (
            "Id" uuid PRIMARY KEY,
            "OwnerId" text NOT NULL,
            "CreatedAt" timestamptz NOT NULL,
            "TotalInputTokens" bigint NOT NULL,
            "TotalOutputTokens" bigint NOT NULL
        );
        """;
}
