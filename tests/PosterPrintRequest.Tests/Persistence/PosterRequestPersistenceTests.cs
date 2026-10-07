using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Tests.Persistence;

public sealed class PosterRequestPersistenceTests : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _database;

    public PosterRequestPersistenceTests(SqlServerDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public void Initial_schema_matches_the_approved_erd()
    {
        using var context = _database.CreateContext();
        var columns = ReadColumns(context);
        var foreignKeys = ReadForeignKeys(context);
        var uniqueColumns = ReadUniqueColumns(context);

        AssertColumn(columns, "Departments", "DepartmentId", "int", nullable: false, identity: true);
        AssertColumn(columns, "Departments", "Name", "nvarchar", nullable: false, characterMaximumLength: -1);

        AssertColumn(columns, "Reasons", "ReasonId", "int", nullable: false, identity: true);
        AssertColumn(columns, "Reasons", "Name", "nvarchar", nullable: false, characterMaximumLength: -1);
        AssertColumn(columns, "Reasons", "RequiresMentor", "bit", nullable: false);
        AssertColumn(columns, "Reasons", "RequiresApprovalSheet", "bit", nullable: false);

        AssertColumn(columns, "PosterRequests", "PosterRequestId", "int", nullable: false, identity: true);
        AssertColumn(columns, "PosterRequests", "PosterId", "nvarchar", nullable: false, characterMaximumLength: 64);
        AssertColumn(columns, "PosterRequests", "Name", "nvarchar", nullable: false, characterMaximumLength: -1);
        AssertColumn(columns, "PosterRequests", "Mentor", "nvarchar", nullable: true, characterMaximumLength: -1);
        AssertColumn(columns, "PosterRequests", "DepartmentId", "int", nullable: false);
        AssertColumn(columns, "PosterRequests", "Room", "nvarchar", nullable: false, characterMaximumLength: -1);
        AssertColumn(columns, "PosterRequests", "Phone", "nvarchar", nullable: false, characterMaximumLength: -1);
        AssertColumn(columns, "PosterRequests", "Email", "nvarchar", nullable: false, characterMaximumLength: -1);
        AssertColumn(columns, "PosterRequests", "SubmittedByUserName", "nvarchar", nullable: true, characterMaximumLength: 256);
        AssertColumn(columns, "PosterRequests", "ReasonId", "int", nullable: true);
        AssertColumn(columns, "PosterRequests", "LaminationRequested", "bit", nullable: false);
        AssertColumn(columns, "PosterRequests", "ApprovalSheetUploaded", "bit", nullable: false);
        AssertColumn(columns, "PosterRequests", "DateIn", "datetime2", nullable: false);

        AssertColumn(columns, "PosterFiles", "PosterFileId", "int", nullable: false, identity: true);
        AssertColumn(columns, "PosterFiles", "PosterRequestId", "int", nullable: false);
        AssertColumn(columns, "PosterFiles", "OriginalFileName", "nvarchar", nullable: false, characterMaximumLength: -1);
        AssertColumn(columns, "PosterFiles", "DetectedFormat", "nvarchar", nullable: false, characterMaximumLength: -1);
        AssertColumn(columns, "PosterFiles", "PageCount", "int", nullable: false);
        AssertColumn(columns, "PosterFiles", "Width", "decimal", nullable: false, precision: 18, scale: 4);
        AssertColumn(columns, "PosterFiles", "Length", "decimal", nullable: false, precision: 18, scale: 4);
        AssertColumn(columns, "PosterFiles", "StoragePath", "nvarchar", nullable: false, characterMaximumLength: -1);

        AssertColumn(columns, "ApprovalSheets", "ApprovalSheetId", "int", nullable: false, identity: true);
        AssertColumn(columns, "ApprovalSheets", "PosterRequestId", "int", nullable: false);
        AssertColumn(columns, "ApprovalSheets", "FileName", "nvarchar", nullable: false, characterMaximumLength: -1);
        AssertColumn(columns, "ApprovalSheets", "StoragePath", "nvarchar", nullable: false, characterMaximumLength: -1);

        AssertColumn(columns, "PosterProcessings", "PosterProcessingId", "int", nullable: false, identity: true);
        AssertColumn(columns, "PosterProcessings", "PosterRequestId", "int", nullable: false);
        AssertColumn(columns, "PosterProcessings", "ITPerson", "nvarchar", nullable: true, characterMaximumLength: -1);
        AssertColumn(columns, "PosterProcessings", "Received", "date", nullable: true);
        AssertColumn(columns, "PosterProcessings", "Printed", "bit", nullable: false);
        AssertColumn(columns, "PosterProcessings", "Laminated", "bit", nullable: false);
        AssertColumn(columns, "PosterProcessings", "Notified", "bit", nullable: false);
        AssertColumn(columns, "PosterProcessings", "DateOut", "date", nullable: true);
        AssertColumn(columns, "PosterProcessings", "PickedUpBy", "nvarchar", nullable: true, characterMaximumLength: -1);
        AssertColumn(columns, "PosterProcessings", "Comments", "nvarchar", nullable: true, characterMaximumLength: -1);

        var domainTables = new HashSet<string>(StringComparer.Ordinal)
        {
            "Departments",
            "Reasons",
            "PosterRequests",
            "PosterFiles",
            "ApprovalSheets",
            "PosterProcessings"
        };
        Assert.Equal(41, columns.Count(pair => domainTables.Contains(pair.Key.Split('.')[0])));
        Assert.Contains(columns.Keys, key => key.StartsWith("AspNetUsers.", StringComparison.Ordinal));
        Assert.Equal("NO_ACTION", foreignKeys["PosterRequests.DepartmentId"]);
        Assert.Equal("NO_ACTION", foreignKeys["PosterRequests.ReasonId"]);
        Assert.Equal("CASCADE", foreignKeys["PosterFiles.PosterRequestId"]);
        Assert.Equal("CASCADE", foreignKeys["ApprovalSheets.PosterRequestId"]);
        Assert.Equal("CASCADE", foreignKeys["PosterProcessings.PosterRequestId"]);

        Assert.Contains("PosterRequests.PosterId", uniqueColumns);
        Assert.Contains("PosterFiles.PosterRequestId", uniqueColumns);
        Assert.Contains("ApprovalSheets.PosterRequestId", uniqueColumns);
        Assert.Contains("PosterProcessings.PosterRequestId", uniqueColumns);
    }

    [Fact]
    public void Request_with_event_persists_authorities_file_approval_sheet_and_processing()
    {
        var posterId = "POSTER-2026-000123";
        int departmentId;
        int reasonId;

        using (var context = _database.CreateContext())
        {
            var department = new Department { Name = "College of Arts" };
            var reason = new Reason
            {
                Name = "CRD",
                RequiresMentor = true,
                RequiresApprovalSheet = true
            };

            context.PosterRequests.Add(CreateRequest(
                department,
                posterId,
                reason,
                mentor: "Grace Hopper",
                laminationRequested: true,
                approvalSheetUploaded: true,
                approvalSheet: new ApprovalSheet
                {
                    FileName = "Approval-Sheet.pdf",
                    StoragePath = "CRD/POSTER-2026-000123/Approval-Sheet.pdf"
                }));
            context.SaveChanges();
            departmentId = department.DepartmentId;
            reasonId = reason.ReasonId;
        }

        using (var context = _database.CreateContext())
        {
            var request = context.PosterRequests
                .Include(item => item.Department)
                .Include(item => item.Reason)
                .Include(item => item.PosterFile)
                .Include(item => item.ApprovalSheet)
                .Include(item => item.PosterProcessing)
                .Single(item => item.PosterId == posterId);

            Assert.Equal("John Smith", request.Name);
            Assert.Equal("Grace Hopper", request.Mentor);
            Assert.Equal(departmentId, request.DepartmentId);
            Assert.Equal("College of Arts", request.Department.Name);
            Assert.Equal("214", request.Room);
            Assert.Equal("555-0100", request.Phone);
            Assert.Equal("john.smith@example.edu", request.Email);
            Assert.Equal(reasonId, request.ReasonId);
            Assert.Equal("CRD", request.Reason!.Name);
            Assert.True(request.Reason.RequiresMentor);
            Assert.True(request.Reason.RequiresApprovalSheet);
            Assert.True(request.LaminationRequested);
            Assert.True(request.ApprovalSheetUploaded);
            Assert.Equal(new DateTime(2026, 10, 6, 16, 34, 0), request.DateIn);
            Assert.Equal("Poster.pdf", request.PosterFile.OriginalFileName);
            Assert.Equal("PDF", request.PosterFile.DetectedFormat);
            Assert.Equal(1, request.PosterFile.PageCount);
            Assert.Equal(24.5m, request.PosterFile.Width);
            Assert.Equal(48.25m, request.PosterFile.Length);
            Assert.Equal("CRD/POSTER-2026-000123/Poster.pdf", request.PosterFile.StoragePath);
            Assert.Equal("Approval-Sheet.pdf", request.ApprovalSheet!.FileName);
            Assert.Equal("CRD/POSTER-2026-000123/Approval-Sheet.pdf", request.ApprovalSheet.StoragePath);
            Assert.Null(request.PosterProcessing.ITPerson);
            Assert.Null(request.PosterProcessing.Received);
            Assert.False(request.PosterProcessing.Printed);
            Assert.False(request.PosterProcessing.Laminated);
            Assert.False(request.PosterProcessing.Notified);
            Assert.Null(request.PosterProcessing.DateOut);
            Assert.Null(request.PosterProcessing.PickedUpBy);
            Assert.Null(request.PosterProcessing.Comments);
        }
    }

    [Fact]
    public void Request_without_event_persists_null_reason_mentor_and_approval_sheet()
    {
        var posterId = "POSTER-2026-000124";

        using (var context = _database.CreateContext())
        {
            context.PosterRequests.Add(CreateRequest(new Department { Name = "Library" }, posterId));
            context.SaveChanges();
        }

        using (var context = _database.CreateContext())
        {
            var request = context.PosterRequests
                .Include(item => item.ApprovalSheet)
                .Include(item => item.Reason)
                .Single(item => item.PosterId == posterId);

            Assert.Null(request.Mentor);
            Assert.Null(request.ReasonId);
            Assert.Null(request.Reason);
            Assert.False(request.ApprovalSheetUploaded);
            Assert.Null(request.ApprovalSheet);
            Assert.False(request.LaminationRequested);
        }
    }

    [Fact]
    public void Department_can_own_many_requests()
    {
        using var context = _database.CreateContext();
        var department = new Department { Name = "Shared Department" };
        context.PosterRequests.Add(CreateRequest(department, "POSTER-2026-000201"));
        context.PosterRequests.Add(CreateRequest(department, "POSTER-2026-000202"));
        context.SaveChanges();

        using var verify = _database.CreateContext();
        var count = verify.PosterRequests.Count(item => item.DepartmentId == department.DepartmentId);
        Assert.Equal(2, count);
    }

    [Fact]
    public void Technician_fields_persist_on_processing_without_changing_the_request()
    {
        var posterId = "POSTER-2026-000301";

        using (var context = _database.CreateContext())
        {
            var request = CreateRequest(new Department { Name = "Nursing" }, posterId);
            context.PosterRequests.Add(request);
            context.SaveChanges();

            request.PosterProcessing.ITPerson = "Alex Rivera";
            request.PosterProcessing.Received = new DateOnly(2026, 10, 7);
            request.PosterProcessing.Printed = true;
            request.PosterProcessing.Laminated = false;
            request.PosterProcessing.Notified = true;
            request.PosterProcessing.DateOut = new DateOnly(2026, 10, 8);
            request.PosterProcessing.PickedUpBy = "John Smith";
            request.PosterProcessing.Comments = "Ready at the front desk.";
            context.SaveChanges();
        }

        using (var context = _database.CreateContext())
        {
            var request = context.PosterRequests
                .Include(item => item.PosterProcessing)
                .Single(item => item.PosterId == posterId);

            Assert.Equal("John Smith", request.Name);
            Assert.Equal("Alex Rivera", request.PosterProcessing.ITPerson);
            Assert.Equal(new DateOnly(2026, 10, 7), request.PosterProcessing.Received);
            Assert.True(request.PosterProcessing.Printed);
            Assert.False(request.PosterProcessing.Laminated);
            Assert.True(request.PosterProcessing.Notified);
            Assert.Equal(new DateOnly(2026, 10, 8), request.PosterProcessing.DateOut);
            Assert.Equal("John Smith", request.PosterProcessing.PickedUpBy);
            Assert.Equal("Ready at the front desk.", request.PosterProcessing.Comments);
        }
    }

    [Fact]
    public void Duplicate_poster_id_is_rejected()
    {
        using var context = _database.CreateContext();
        var department = new Department { Name = "Duplicate Check" };
        context.PosterRequests.Add(CreateRequest(department, "POSTER-2026-000401"));
        context.SaveChanges();

        context.PosterRequests.Add(CreateRequest(department, "POSTER-2026-000401"));
        var exception = Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        var sqlException = Assert.IsType<SqlException>(exception.InnerException);
        Assert.Contains(sqlException.Number, new[] { 2601, 2627 });
    }

    [Fact]
    public void Department_in_use_cannot_be_deleted()
    {
        int departmentId;
        using (var context = _database.CreateContext())
        {
            var department = new Department { Name = "Protected Department" };
            context.PosterRequests.Add(CreateRequest(department, "POSTER-2026-000501"));
            context.SaveChanges();
            departmentId = department.DepartmentId;
        }

        using (var context = _database.CreateContext())
        {
            var department = context.Departments.Single(item => item.DepartmentId == departmentId);
            context.Departments.Remove(department);
            var exception = Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            var sqlException = Assert.IsType<SqlException>(exception.InnerException);
            Assert.Equal(547, sqlException.Number);
        }
    }

    [Fact]
    public void Reason_in_use_cannot_be_deleted()
    {
        int reasonId;
        using (var context = _database.CreateContext())
        {
            var reason = new Reason
            {
                Name = "Protected Event",
                RequiresMentor = false,
                RequiresApprovalSheet = false
            };
            context.PosterRequests.Add(CreateRequest(
                new Department { Name = "Reason Protection" },
                "POSTER-2026-000502",
                reason));
            context.SaveChanges();
            reasonId = reason.ReasonId;
        }

        using (var context = _database.CreateContext())
        {
            var reason = context.Reasons.Single(item => item.ReasonId == reasonId);
            context.Reasons.Remove(reason);
            var exception = Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            var sqlException = Assert.IsType<SqlException>(exception.InnerException);
            Assert.Equal(547, sqlException.Number);
        }
    }

    [Fact]
    public void Deleting_a_request_deletes_its_file_approval_sheet_and_processing()
    {
        var posterId = "POSTER-2026-000601";
        int requestId;
        int departmentId;

        using (var context = _database.CreateContext())
        {
            var department = new Department { Name = "Cascade Department" };
            var request = CreateRequest(
                department,
                posterId,
                approvalSheet: new ApprovalSheet
                {
                    FileName = "Approval-Sheet.pdf",
                    StoragePath = "CRD/POSTER-2026-000601/Approval-Sheet.pdf"
                });
            context.PosterRequests.Add(request);
            context.SaveChanges();
            requestId = request.PosterRequestId;
            departmentId = department.DepartmentId;

            context.PosterRequests.Remove(request);
            context.SaveChanges();
        }

        using (var context = _database.CreateContext())
        {
            Assert.False(context.PosterRequests.Any(item => item.PosterRequestId == requestId));
            Assert.False(context.PosterFiles.Any(item => item.PosterRequestId == requestId));
            Assert.False(context.ApprovalSheets.Any(item => item.PosterRequestId == requestId));
            Assert.False(context.PosterProcessings.Any(item => item.PosterRequestId == requestId));
            Assert.True(context.Departments.Any(item => item.DepartmentId == departmentId));
        }
    }

    [Fact]
    public void Second_poster_file_for_the_same_request_is_rejected()
    {
        int requestId;
        using (var context = _database.CreateContext())
        {
            var request = CreateRequest(new Department { Name = "File Cardinality" }, "POSTER-2026-000701");
            context.PosterRequests.Add(request);
            context.SaveChanges();
            requestId = request.PosterRequestId;
        }

        using (var context = _database.CreateContext())
        {
            context.PosterFiles.Add(new PosterFile
            {
                PosterRequestId = requestId,
                OriginalFileName = "Other.pdf",
                DetectedFormat = "PDF",
                PageCount = 1,
                Width = 10m,
                Length = 20m,
                StoragePath = "CRD/POSTER-2026-000701/Other.pdf"
            });

            var exception = Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            var sqlException = Assert.IsType<SqlException>(exception.InnerException);
            Assert.Contains(sqlException.Number, new[] { 2601, 2627 });
        }
    }

    private static PosterRequest CreateRequest(
        Department department,
        string posterId,
        Reason? reason = null,
        string? mentor = null,
        bool laminationRequested = false,
        bool approvalSheetUploaded = false,
        ApprovalSheet? approvalSheet = null)
    {
        return new PosterRequest
        {
            PosterId = posterId,
            Name = "John Smith",
            Mentor = mentor,
            Department = department,
            Room = "214",
            Phone = "555-0100",
            Email = "john.smith@example.edu",
            Reason = reason,
            LaminationRequested = laminationRequested,
            ApprovalSheetUploaded = approvalSheetUploaded,
            DateIn = new DateTime(2026, 10, 6, 16, 34, 0),
            PosterFile = new PosterFile
            {
                OriginalFileName = "Poster.pdf",
                DetectedFormat = "PDF",
                PageCount = 1,
                Width = 24.5m,
                Length = 48.25m,
                StoragePath = "CRD/" + posterId + "/Poster.pdf"
            },
            ApprovalSheet = approvalSheet,
            PosterProcessing = new PosterProcessing()
        };
    }

    private static void AssertColumn(
        IReadOnlyDictionary<string, ColumnInfo> columns,
        string table,
        string column,
        string dataType,
        bool nullable,
        bool identity = false,
        int? characterMaximumLength = null,
        byte? precision = null,
        int? scale = null)
    {
        var key = table + "." + column;
        Assert.True(columns.TryGetValue(key, out var actual), "Missing column " + key);
        Assert.Equal(dataType, actual.DataType);
        Assert.Equal(nullable, actual.IsNullable);
        Assert.Equal(identity, actual.IsIdentity);
        Assert.Equal(characterMaximumLength, actual.CharacterMaximumLength);
        if (dataType == "decimal")
        {
            Assert.Equal(precision, actual.NumericPrecision);
            Assert.Equal(scale, actual.NumericScale);
        }
    }

    private static Dictionary<string, ColumnInfo> ReadColumns(PosterPrintRequestDbContext context)
    {
        const string sql = """
            SELECT
                c.TABLE_NAME,
                c.COLUMN_NAME,
                c.DATA_TYPE,
                c.IS_NULLABLE,
                c.CHARACTER_MAXIMUM_LENGTH,
                c.NUMERIC_PRECISION,
                c.NUMERIC_SCALE,
                COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)), c.COLUMN_NAME, 'IsIdentity')
            FROM INFORMATION_SCHEMA.COLUMNS c
            WHERE c.TABLE_SCHEMA = 'dbo'
              AND c.TABLE_NAME <> '__EFMigrationsHistory'
            """;

        var columns = new Dictionary<string, ColumnInfo>(StringComparer.Ordinal);
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        context.Database.OpenConnection();
        try
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0) + "." + reader.GetString(1);
                columns.Add(key, new ColumnInfo(
                    reader.GetString(2),
                    reader.GetString(3) == "YES",
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetByte(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.GetInt32(7) == 1));
            }
        }
        finally
        {
            context.Database.CloseConnection();
        }

        return columns;
    }

    private static Dictionary<string, string> ReadForeignKeys(PosterPrintRequestDbContext context)
    {
        const string sql = """
            SELECT
                OBJECT_NAME(fk.parent_object_id),
                COL_NAME(fkc.parent_object_id, fkc.parent_column_id),
                fk.delete_referential_action_desc
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            """;

        var foreignKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        context.Database.OpenConnection();
        try
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                foreignKeys.Add(reader.GetString(0) + "." + reader.GetString(1), reader.GetString(2));
            }
        }
        finally
        {
            context.Database.CloseConnection();
        }

        return foreignKeys;
    }

    private static HashSet<string> ReadUniqueColumns(PosterPrintRequestDbContext context)
    {
        const string sql = """
            SELECT t.name, c.name
            FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            JOIN sys.tables t ON i.object_id = t.object_id
            WHERE i.is_unique = 1
            """;

        var columns = new HashSet<string>(StringComparer.Ordinal);
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        context.Database.OpenConnection();
        try
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(0) + "." + reader.GetString(1));
            }
        }
        finally
        {
            context.Database.CloseConnection();
        }

        return columns;
    }

    private sealed record ColumnInfo(
        string DataType,
        bool IsNullable,
        int? CharacterMaximumLength,
        byte? NumericPrecision,
        int? NumericScale,
        bool IsIdentity);
}
