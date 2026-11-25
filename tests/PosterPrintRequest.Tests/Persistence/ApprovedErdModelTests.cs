using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Tests.Persistence;

public sealed class ApprovedErdModelTests
{
    [Fact]
    public void Model_contains_the_approved_entities()
    {
        using var context = CreateContext();

        var names = context.Model.GetEntityTypes()
            .Where(entity => entity.ClrType.Namespace == typeof(PosterRequest).Namespace)
            .Select(entity => entity.ClrType.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(
            new[]
            {
                nameof(ApprovalSheet),
                nameof(ConsumableType),
                nameof(Department),
                nameof(PosterFile),
                nameof(PosterProcessing),
                nameof(PosterReplacement),
                nameof(PosterRequest),
                nameof(PosterRetentionAudit),
                nameof(PrinterModel),
                nameof(PrinterModelConsumable),
                nameof(PrintingConsumable),
                nameof(PrintingStockEntry),
                nameof(PrintingStockMovement),
                nameof(Reason)
            },
            names);
        Assert.DoesNotContain(
            context.Model.GetEntityTypes(),
            entity => entity.GetTableName() is { } table && table.StartsWith("AspNet", StringComparison.Ordinal));
        Assert.DoesNotContain(
            context.Model.GetEntityTypes(),
            entity => entity.ClrType.Name == "ApplicationUser");
    }

    [Fact]
    public void Authorities_and_request_keep_separate_properties()
    {
        Assert.Equal(
            new[] { nameof(Department.Active), nameof(Department.DepartmentId), nameof(Department.Name), nameof(Department.PosterRequests) },
            PropertyNames(typeof(Department)));

        Assert.Equal(
            new[]
            {
                nameof(Reason.Active),
                nameof(Reason.Name),
                nameof(Reason.PosterRequests),
                nameof(Reason.ReasonId),
                nameof(Reason.RequiresApprovalSheet),
                nameof(Reason.RequiresMentor)
            },
            PropertyNames(typeof(Reason)));

        Assert.Equal(
            new[]
            {
                nameof(PosterRequest.ApprovalSheet),
                nameof(PosterRequest.ApprovalSheetUploaded),
                nameof(PosterRequest.DateIn),
                nameof(PosterRequest.Department),
                nameof(PosterRequest.DepartmentId),
                nameof(PosterRequest.DepartmentName),
                nameof(PosterRequest.Email),
                nameof(PosterRequest.LaminationRequested),
                nameof(PosterRequest.Mentor),
                nameof(PosterRequest.Name),
                nameof(PosterRequest.OriginalDirectory),
                nameof(PosterRequest.PermanentlyDeletedAt),
                nameof(PosterRequest.Phone),
                nameof(PosterRequest.PosterFile),
                nameof(PosterRequest.PosterId),
                nameof(PosterRequest.PosterProcessing),
                nameof(PosterRequest.PosterRequestId),
                nameof(PosterRequest.PosterTitle),
                nameof(PosterRequest.Reason),
                nameof(PosterRequest.ReasonId),
                nameof(PosterRequest.ReasonName),
                nameof(PosterRequest.Replacements),
                nameof(PosterRequest.Room),
                nameof(PosterRequest.SubmittedByAccountName),
                nameof(PosterRequest.SubmittedByObjectId),
                nameof(PosterRequest.SubmittedBySecurityIdentifier),
                nameof(PosterRequest.TdxClaimedUntil),
                nameof(PosterRequest.TdxTicketId),
                nameof(PosterRequest.TdxTicketState),
                nameof(PosterRequest.TemporaryDeletionAt)
            },
            PropertyNames(typeof(PosterRequest)));

        Assert.Equal(
            new[]
            {
                nameof(PosterFile.DetectedFormat),
                nameof(PosterFile.Length),
                nameof(PosterFile.OriginalFileName),
                nameof(PosterFile.PageCount),
                nameof(PosterFile.PosterFileId),
                nameof(PosterFile.PosterRequest),
                nameof(PosterFile.PosterRequestId),
                nameof(PosterFile.StoragePath),
                nameof(PosterFile.Width)
            },
            PropertyNames(typeof(PosterFile)));

        Assert.Equal(
            new[]
            {
                nameof(ApprovalSheet.ApprovalSheetId),
                nameof(ApprovalSheet.FileName),
                nameof(ApprovalSheet.PosterRequest),
                nameof(ApprovalSheet.PosterRequestId),
                nameof(ApprovalSheet.StoragePath)
            },
            PropertyNames(typeof(ApprovalSheet)));

        Assert.Equal(
            new[]
            {
                nameof(PosterProcessing.Comments),
                nameof(PosterProcessing.DateOut),
                nameof(PosterProcessing.Laminated),
                nameof(PosterProcessing.Notified),
                nameof(PosterProcessing.PickedUpBy),
                nameof(PosterProcessing.PosterProcessingId),
                nameof(PosterProcessing.PosterRequest),
                nameof(PosterProcessing.PosterRequestId),
                nameof(PosterProcessing.Printed),
                nameof(PosterProcessing.ProcessedByAccountName),
                nameof(PosterProcessing.ProcessedByObjectId),
                nameof(PosterProcessing.ProcessedBySecurityIdentifier),
                nameof(PosterProcessing.Received)
            },
            PropertyNames(typeof(PosterProcessing)));
    }

    [Fact]
    public void Poster_id_is_required_unique_and_can_store_the_recommended_format()
    {
        using var context = CreateContext();
        var posterId = context.Model.FindEntityType(typeof(PosterRequest))!
            .FindProperty(nameof(PosterRequest.PosterId))!;

        Assert.False(posterId.IsNullable);
        var maxLength = posterId.GetMaxLength();
        Assert.NotNull(maxLength);
        Assert.True(maxLength >= "POSTER-YYYY-NNNNNN".Length);

        var index = context.Model.FindEntityType(typeof(PosterRequest))!
            .GetIndexes()
            .Single(candidate => candidate.Properties.Count == 1 && candidate.Properties[0].Name == nameof(PosterRequest.PosterId));
        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Relationships_match_the_approved_erd()
    {
        using var context = CreateContext();
        var request = context.Model.FindEntityType(typeof(PosterRequest))!;

        var department = request.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(Department));
        Assert.True(department.IsRequired);
        Assert.False(department.IsUnique);
        Assert.Equal(DeleteBehavior.Restrict, department.DeleteBehavior);

        var reason = request.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(Reason));
        Assert.False(reason.IsRequired);
        Assert.False(reason.IsUnique);
        Assert.Equal(DeleteBehavior.Restrict, reason.DeleteBehavior);

        var posterFile = request.FindNavigation(nameof(PosterRequest.PosterFile))!;
        Assert.False(posterFile.IsCollection);
        Assert.True(posterFile.ForeignKey.IsUnique);
        Assert.True(posterFile.ForeignKey.IsRequired);
        Assert.True(posterFile.ForeignKey.IsRequiredDependent);
        Assert.Equal(DeleteBehavior.Cascade, posterFile.ForeignKey.DeleteBehavior);

        var approvalSheet = request.FindNavigation(nameof(PosterRequest.ApprovalSheet))!;
        Assert.False(approvalSheet.IsCollection);
        Assert.True(approvalSheet.ForeignKey.IsUnique);
        Assert.True(approvalSheet.ForeignKey.IsRequired);
        Assert.False(approvalSheet.ForeignKey.IsRequiredDependent);
        Assert.Equal(DeleteBehavior.Cascade, approvalSheet.ForeignKey.DeleteBehavior);

        var processing = request.FindNavigation(nameof(PosterRequest.PosterProcessing))!;
        Assert.False(processing.IsCollection);
        Assert.True(processing.ForeignKey.IsUnique);
        Assert.True(processing.ForeignKey.IsRequired);
        Assert.True(processing.ForeignKey.IsRequiredDependent);
        Assert.Equal(DeleteBehavior.Cascade, processing.ForeignKey.DeleteBehavior);
    }

    [Fact]
    public void Measurement_and_date_columns_use_the_persistence_types()
    {
        using var context = CreateContext();

        var request = context.Model.FindEntityType(typeof(PosterRequest))!;
        Assert.Equal("datetime2", request.FindProperty(nameof(PosterRequest.DateIn))!.GetColumnType());
        Assert.True(request.FindProperty(nameof(PosterRequest.Mentor))!.IsNullable);
        Assert.False(request.FindProperty(nameof(PosterRequest.DepartmentId))!.IsNullable);
        Assert.True(request.FindProperty(nameof(PosterRequest.ReasonId))!.IsNullable);

        var file = context.Model.FindEntityType(typeof(PosterFile))!;
        Assert.Equal(18, file.FindProperty(nameof(PosterFile.Width))!.GetPrecision());
        Assert.Equal(4, file.FindProperty(nameof(PosterFile.Width))!.GetScale());
        Assert.Equal(18, file.FindProperty(nameof(PosterFile.Length))!.GetPrecision());
        Assert.Equal(4, file.FindProperty(nameof(PosterFile.Length))!.GetScale());

        var processing = context.Model.FindEntityType(typeof(PosterProcessing))!;
        Assert.Equal("date", processing.FindProperty(nameof(PosterProcessing.Received))!.GetColumnType());
        Assert.Equal("date", processing.FindProperty(nameof(PosterProcessing.DateOut))!.GetColumnType());
        Assert.True(processing.FindProperty(nameof(PosterProcessing.ProcessedByAccountName))!.IsNullable);
        Assert.True(processing.FindProperty(nameof(PosterProcessing.ProcessedBySecurityIdentifier))!.IsNullable);
        Assert.True(processing.FindProperty(nameof(PosterProcessing.ProcessedByObjectId))!.IsNullable);
        Assert.Equal(typeof(Guid?), processing.FindProperty(nameof(PosterProcessing.ProcessedByObjectId))!.ClrType);
        Assert.True(request.FindProperty(nameof(PosterRequest.SubmittedByObjectId))!.IsNullable);
        Assert.Equal(typeof(Guid?), request.FindProperty(nameof(PosterRequest.SubmittedByObjectId))!.ClrType);
        Assert.Equal(256, processing.FindProperty(nameof(PosterProcessing.ProcessedByAccountName))!.GetMaxLength());
        Assert.Equal(184, processing.FindProperty(nameof(PosterProcessing.ProcessedBySecurityIdentifier))!.GetMaxLength());
        Assert.True(processing.FindProperty(nameof(PosterProcessing.Received))!.IsNullable);
        Assert.True(processing.FindProperty(nameof(PosterProcessing.DateOut))!.IsNullable);
        Assert.True(processing.FindProperty(nameof(PosterProcessing.PickedUpBy))!.IsNullable);
        Assert.True(processing.FindProperty(nameof(PosterProcessing.Comments))!.IsNullable);
        Assert.False(processing.FindProperty(nameof(PosterProcessing.Printed))!.IsNullable);
        Assert.False(processing.FindProperty(nameof(PosterProcessing.Laminated))!.IsNullable);
        Assert.False(processing.FindProperty(nameof(PosterProcessing.Notified))!.IsNullable);

        var consumable = context.Model.FindEntityType(typeof(PrintingConsumable))!;
        Assert.Null(consumable.FindProperty("CurrentQuantity"));
        Assert.Null(consumable.FindProperty("ExpirationDate"));
        Assert.True(consumable.FindProperty(nameof(PrintingConsumable.Code))!.IsNullable);
        Assert.False(consumable.FindProperty(nameof(PrintingConsumable.HasExpirationDate))!.IsNullable);
        var codeIndex = consumable.GetIndexes().Single(index => index.Properties.Count == 1 && index.Properties[0].Name == nameof(PrintingConsumable.Code));
        Assert.True(codeIndex.IsUnique);
        var entries = consumable.FindNavigation(nameof(PrintingConsumable.StockEntries))!;
        Assert.True(entries.IsCollection);
        Assert.Equal(DeleteBehavior.Restrict, entries.ForeignKey.DeleteBehavior);

        // Consumables Inventory Contract sections 4.1, 4.4 and 7.1 (PHASE 16).
        Assert.Null(consumable.FindProperty("Category"));
        var typeKey = consumable.FindNavigation(nameof(PrintingConsumable.ConsumableType))!.ForeignKey;
        Assert.Equal(DeleteBehavior.Restrict, typeKey.DeleteBehavior);
        Assert.True(typeKey.IsRequired);
        var consumableType = context.Model.FindEntityType(typeof(ConsumableType))!;
        Assert.Equal(64, consumableType.FindProperty(nameof(ConsumableType.Name))!.GetMaxLength());
        Assert.Equal("UPPER([Name]) COLLATE Latin1_General_100_BIN2", consumableType.FindProperty(nameof(ConsumableType.NameKey))!.GetComputedColumnSql());
        Assert.True(consumableType.FindProperty(nameof(ConsumableType.NameKey))!.GetIsStored());
        Assert.Contains(consumableType.GetIndexes(), index => index.IsUnique && index.Properties.Single().Name == nameof(ConsumableType.NameKey));
        var movement = context.Model.FindEntityType(typeof(PrintingStockMovement))!;
        Assert.Equal(DeleteBehavior.Restrict, movement.GetForeignKeys().Single().DeleteBehavior);
        Assert.Equal(500, movement.FindProperty(nameof(PrintingStockMovement.Reason))!.GetMaxLength());
        Assert.True(movement.FindProperty(nameof(PrintingStockMovement.Reason))!.IsNullable);
        Assert.Equal(
            ["CK_PrintingStockMovements_LotQuantityAfter", "CK_PrintingStockMovements_MovementType", "CK_PrintingStockMovements_OutOfOrder", "CK_PrintingStockMovements_QuantityChange", "CK_PrintingStockMovements_Reason"],
            context.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.FindEntityType(typeof(PrintingStockMovement))!.GetCheckConstraints().Select(check => check.ModelName).OrderBy(name => name, StringComparer.Ordinal).ToArray());

        var stockEntry = context.Model.FindEntityType(typeof(PrintingStockEntry))!;
        Assert.Equal("date", stockEntry.FindProperty(nameof(PrintingStockEntry.ExpirationDate))!.GetColumnType());
        Assert.True(stockEntry.FindProperty(nameof(PrintingStockEntry.ExpirationDate))!.IsNullable);
        Assert.False(stockEntry.FindProperty(nameof(PrintingStockEntry.Quantity))!.IsNullable);
        Assert.False(stockEntry.FindProperty(nameof(PrintingStockEntry.PrintingConsumableId))!.IsNullable);

        var department = context.Model.FindEntityType(typeof(Department))!;
        Assert.False(department.FindProperty(nameof(Department.Active))!.IsNullable);
        Assert.Equal(DepartmentConfiguration.NameMaxLength, department.FindProperty(nameof(Department.Name))!.GetMaxLength());
        Assert.Contains(department.GetIndexes(), index => index.IsUnique && index.Properties.Any(property => property.Name == nameof(Department.Name)));

        var reasonEntity = context.Model.FindEntityType(typeof(Reason))!;
        Assert.False(reasonEntity.FindProperty(nameof(Reason.Active))!.IsNullable);
        Assert.Equal(ReasonConfiguration.NameMaxLength, reasonEntity.FindProperty(nameof(Reason.Name))!.GetMaxLength());

        Assert.Equal(
            new[]
            {
                nameof(PrinterModel.Active),
                nameof(PrinterModel.CompatibleConsumables),
                nameof(PrinterModel.Name),
                nameof(PrinterModel.PrinterModelId)
            },
            PropertyNames(typeof(PrinterModel)));
        Assert.Equal(
            new[]
            {
                nameof(PrinterModelConsumable.Active),
                nameof(PrinterModelConsumable.PrinterModel),
                nameof(PrinterModelConsumable.PrinterModelConsumableId),
                nameof(PrinterModelConsumable.PrinterModelId),
                nameof(PrinterModelConsumable.PrintingConsumable),
                nameof(PrinterModelConsumable.PrintingConsumableId)
            },
            PropertyNames(typeof(PrinterModelConsumable)));

        var link = context.Model.FindEntityType(typeof(PrinterModelConsumable))!;
        Assert.All(link.GetForeignKeys(), foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
        Assert.Contains(
            link.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(property => property.Name).OrderBy(name => name).SequenceEqual(
                    new[] { nameof(PrinterModelConsumable.PrinterModelId), nameof(PrinterModelConsumable.PrintingConsumableId) }));
    }

    private static string[] PropertyNames(Type type) =>
        type.GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray();

    private static PosterPrintRequestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PosterPrintRequestDbContext>()
            .UseSqlServer("Server=localhost;Database=PosterPrintRequest_ModelInspection;Integrated Security=True;TrustServerCertificate=True")
            .Options;

        return new PosterPrintRequestDbContext(options);
    }
}
