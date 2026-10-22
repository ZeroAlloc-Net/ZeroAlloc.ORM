namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// A schema shaped like Daedalus' EF-created one: quoted PascalCase names, a reserved word
// ("Order") as a column, a typed id, an enum stored as int and one stored as text, a text[],
// a nullable timestamptz, and nullable int, real and int-enum columns for the null semantics. A
// task's nullable "ParentTaskId" makes it a sub-task.
// "Projects" lives in its own schema so the map's Schema is used. "Executions" is Daedalus'
// task-execution log, which the aggregates read, and "Sessions" its chat sessions, which the
// grouped token report reads. "AnalysisRequests" is Daedalus' code analysis request, whose owned
// Repository, Location and optional Outcome are columns of the table: some under the bare names
// Daedalus gives them with HasColumnName, the rest under EF's {Owner}_{Property} default.
// "BrainstormSessions" and "BrainstormMessages" are Daedalus' brainstorm session and its
// messages, which load together as a parent with its children. "AnalysisIterations" are the
// iterations of a code analysis request, which load with their request the same way.
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
            "Category" int NULL,
            "Score" real NOT NULL DEFAULT 0,
            "ParentTaskId" uuid NULL
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
        CREATE TABLE "AnalysisRequests" (
            "Id" uuid PRIMARY KEY,
            "Title" text NOT NULL,
            "RepositoryUrl" text NOT NULL,
            "RepositoryBranch" text NULL,
            "Repository_CommitSha" text NULL,
            "Repository_Platform" int NOT NULL,
            "FilePath" text NOT NULL,
            "Location_StartLine" int NULL,
            "Location_EndLine" int NULL,
            "Outcome_PullRequestUrl" text NULL,
            "Outcome_ValidationResult" jsonb NULL,
            "Outcome_HasFailedValidation" boolean NULL,
            "Review_Reviewer" text NULL,
            "Review_Approved" boolean NULL
        );
        CREATE TABLE "BrainstormSessions" (
            "Id" uuid PRIMARY KEY,
            "Topic" text NOT NULL
        );
        CREATE TABLE "BrainstormMessages" (
            "Id" uuid PRIMARY KEY,
            "SessionId" uuid NOT NULL,
            "Role" text NOT NULL,
            "Content" text NOT NULL,
            "CreatedAt" timestamptz NOT NULL
        );
        CREATE TABLE "AnalysisIterations" (
            "Id" uuid PRIMARY KEY,
            "RequestId" uuid NOT NULL,
            "Number" int NOT NULL,
            "Summary" text NOT NULL
        );
        """;
}
