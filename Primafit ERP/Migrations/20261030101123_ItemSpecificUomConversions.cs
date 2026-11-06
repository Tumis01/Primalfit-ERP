using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Primafit_ERP.Migrations
{
    /// <inheritdoc />
    public partial class ItemSpecificUomConversions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "VendorReturnLines",
                type: "decimal(28,16)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "VendorReturnLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "StockTransfers",
                type: "decimal(28,16)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityReceived",
                table: "StockTransfers",
                type: "decimal(28,12)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityInUom",
                table: "StockTransfers",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "StockTransfers",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "StockTransferLines",
                type: "decimal(28,16)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityReceived",
                table: "StockTransferLines",
                type: "decimal(28,12)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityInUom",
                table: "StockTransferLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "StockTransferLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "StockLedgers",
                type: "decimal(28,16)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityInUom",
                table: "StockLedgers",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityChanged",
                table: "StockLedgers",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "SalesShipmentLines",
                type: "decimal(28,16)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QtyShipped",
                table: "SalesShipmentLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QtyOrdered",
                table: "SalesShipmentLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "SalesQuoteLines",
                type: "decimal(28,16)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "SalesQuoteLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "SalesOrderLines",
                type: "decimal(28,16)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "SalesOrderLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QtyShipped",
                table: "SalesOrderLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QtyInvoiced",
                table: "SalesOrderLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "SalesInvoiceLines",
                type: "decimal(28,16)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityInvoiced",
                table: "SalesInvoiceLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "ReceiptRefundLines",
                type: "decimal(28,16)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "ReceiptRefundLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "PurchaseRequestLines",
                type: "decimal(28,16)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityRequested",
                table: "PurchaseRequestLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "PurchaseOrderLines",
                type: "decimal(28,16)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityReceived",
                table: "PurchaseOrderLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityOrdered",
                table: "PurchaseOrderLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityBilled",
                table: "PurchaseOrderLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "PurchaseInvoiceLines",
                type: "decimal(28,16)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityBilled",
                table: "PurchaseInvoiceLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "ConversionFactorToBase",
                table: "ItemUomConversionLines",
                type: "decimal(28,16)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "GoodsReceiptLines",
                type: "decimal(28,16)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityReceived",
                table: "GoodsReceiptLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "GoodsReceiptDraftLines",
                type: "decimal(28,16)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityReceived",
                table: "GoodsReceiptDraftLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "DebitNoteLines",
                type: "decimal(28,16)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnfulfilledQuantity",
                table: "DebitNoteLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "DebitNoteLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "CreditNoteLines",
                type: "decimal(28,16)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnfulfilledQuantity",
                table: "CreditNoteLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            foreach (var column in new[] { ("ItemCostHistories", "OldQty"), ("ItemCostHistories", "NewQtyIn"), ("WaccHistories", "OldQty"), ("WaccHistories", "IncomingQty") })
                migrationBuilder.AlterColumn<decimal>(name: column.Item2, table: column.Item1, type: "decimal(28,12)", nullable: false, oldClrType: typeof(decimal), oldType: "decimal(18,4)");

            // Copy the former global graph once. Existing item rows, including
            // inactive rows, remain authoritative. Transaction snapshots stay intact.
            migrationBuilder.Sql("""
                UPDATE i SET UomId = u.Id
                FROM Items i
                CROSS APPLY (SELECT TOP (1) Id FROM UnitOfMeasures u
                             WHERE u.CompanyId = i.CompanyId AND u.Name = i.UoM ORDER BY u.Id) u
                WHERE i.UomId IS NULL AND i.IsService = 0;

                IF EXISTS (SELECT 1 FROM UomConversionRules r
                           WHERE r.IsActive = 1 AND r.ConversionFactor >= 1000000000000
                             AND EXISTS (SELECT 1 FROM Items i WHERE i.CompanyId = r.CompanyId AND i.IsService = 0))
                    THROW 51000, 'A legacy UOM conversion exceeds the supported item ratio. Review that conversion before upgrading.', 1;

                CREATE TABLE #ItemUomEdges (
                    CompanyId uniqueidentifier NOT NULL, FromId uniqueidentifier NOT NULL,
                    ToId uniqueidentifier NOT NULL, Factor decimal(28,16) NOT NULL,
                    Priority int NOT NULL, PRIMARY KEY (CompanyId, FromId, ToId));

                ;WITH Rules AS (
                    SELECT r.Id, r.CompanyId, r.FromUomId AS FromId, r.ToUomId AS ToId,
                           CAST(CASE WHEN r.ConversionFactor > 0 THEN r.ConversionFactor ELSE 1 END AS decimal(28,16)) AS Factor,
                           0 AS Priority
                    FROM UomConversionRules r
                    JOIN UnitOfMeasures a ON a.Id = r.FromUomId AND a.CompanyId = r.CompanyId
                    JOIN UnitOfMeasures b ON b.Id = r.ToUomId AND b.CompanyId = r.CompanyId
                    WHERE r.IsActive = 1 AND r.FromUomId <> r.ToUomId
                    UNION ALL
                    SELECT u.Id, u.CompanyId, u.Id, u.ConversionUomId,
                           CAST(CASE WHEN u.ConversionFactorValue > 0 THEN u.ConversionFactorValue ELSE 1 END AS decimal(28,16)), 1
                    FROM UnitOfMeasures u JOIN UnitOfMeasures t ON t.Id = u.ConversionUomId AND t.CompanyId = u.CompanyId
                    WHERE u.Id <> u.ConversionUomId
                      AND u.ConversionFactorValue < 1000000000000
                      AND NOT EXISTS (SELECT 1 FROM UomConversionRules r WHERE r.CompanyId = u.CompanyId AND r.FromUomId = u.Id AND r.ToUomId = u.ConversionUomId)
                ), Directions AS (
                    SELECT Id, CompanyId, FromId, ToId, Factor, Priority FROM Rules
                    UNION ALL
                    SELECT Id, CompanyId, ToId, FromId, CAST(1.0 / Factor AS decimal(28,16)), Priority + 2 FROM Rules
                ), Ranked AS (
                    SELECT *, ROW_NUMBER() OVER (PARTITION BY CompanyId, FromId, ToId ORDER BY Priority, Id) AS Position
                    FROM Directions WHERE Factor > 0
                )
                INSERT INTO #ItemUomEdges (CompanyId, FromId, ToId, Factor, Priority)
                SELECT CompanyId, FromId, ToId, Factor, Priority FROM Ranked WHERE Position = 1;

                CREATE TABLE #ItemUomPaths (
                    CompanyId uniqueidentifier NOT NULL, FromId uniqueidentifier NOT NULL,
                    ToId uniqueidentifier NOT NULL, Factor decimal(28,16) NOT NULL,
                    Depth int NOT NULL, PRIMARY KEY (CompanyId, FromId, ToId));
                INSERT INTO #ItemUomPaths (CompanyId, FromId, ToId, Factor, Depth)
                SELECT DISTINCT i.CompanyId, i.UomId, i.UomId, 1, 0
                FROM Items i JOIN UnitOfMeasures u ON u.Id = i.UomId AND u.CompanyId = i.CompanyId
                WHERE i.IsService = 0;

                DECLARE @depth int = 0, @added int = 1;
                WHILE @added > 0
                BEGIN
                    ;WITH NextPaths AS (
                        SELECT p.CompanyId, p.FromId, e.ToId,
                               CASE WHEN TRY_CONVERT(decimal(24,16), p.Factor) IS NOT NULL AND TRY_CONVERT(decimal(24,16), e.Factor) IS NOT NULL
                                    THEN TRY_CONVERT(decimal(28,16), TRY_CONVERT(decimal(24,16), p.Factor) * TRY_CONVERT(decimal(24,16), e.Factor))
                                    ELSE TRY_CONVERT(decimal(28,16), p.Factor * e.Factor) END AS Factor,
                               ROW_NUMBER() OVER (PARTITION BY p.CompanyId, p.FromId, e.ToId ORDER BY e.Priority, p.ToId) AS Position
                        FROM #ItemUomPaths p JOIN #ItemUomEdges e ON e.CompanyId = p.CompanyId AND e.FromId = p.ToId
                        WHERE p.Depth = @depth
                          AND NOT EXISTS (SELECT 1 FROM #ItemUomPaths seen WHERE seen.CompanyId = p.CompanyId AND seen.FromId = p.FromId AND seen.ToId = e.ToId)
                    )
                    INSERT INTO #ItemUomPaths (CompanyId, FromId, ToId, Factor, Depth)
                    SELECT CompanyId, FromId, ToId, Factor, @depth + 1 FROM NextPaths
                    WHERE Position = 1 AND Factor > 0.000000000001 AND Factor < 1000000000000;
                    SET @added = @@ROWCOUNT;
                    SET @depth = @depth + 1;
                END;

                INSERT INTO ItemUomConversionLines (Id, ItemId, UomId, ConversionFactorToBase, IsActive)
                SELECT NEWID(), i.Id, p.ToId, p.Factor, 1
                FROM Items i JOIN #ItemUomPaths p ON p.CompanyId = i.CompanyId AND p.FromId = i.UomId
                WHERE i.IsService = 0 AND p.FromId <> p.ToId
                  AND NOT EXISTS (SELECT 1 FROM ItemUomConversionLines c WHERE c.ItemId = i.Id AND c.UomId = p.ToId);
                DROP TABLE #ItemUomPaths;
                DROP TABLE #ItemUomEdges;
                """);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "CreditNoteLines",
                type: "decimal(28,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Keep copied item configuration on downgrade; it is business data.
            foreach (var column in new[] { ("ItemCostHistories", "OldQty"), ("ItemCostHistories", "NewQtyIn"), ("WaccHistories", "OldQty"), ("WaccHistories", "IncomingQty") })
                migrationBuilder.AlterColumn<decimal>(name: column.Item2, table: column.Item1, type: "decimal(18,4)", nullable: false, oldClrType: typeof(decimal), oldType: "decimal(28,12)");
            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "VendorReturnLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,16)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "VendorReturnLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "StockTransfers",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,16)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityReceived",
                table: "StockTransfers",
                type: "decimal(18,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityInUom",
                table: "StockTransfers",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "StockTransfers",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "StockTransferLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,16)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityReceived",
                table: "StockTransferLines",
                type: "decimal(18,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityInUom",
                table: "StockTransferLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "StockTransferLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "StockLedgers",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,16)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityInUom",
                table: "StockLedgers",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityChanged",
                table: "StockLedgers",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "SalesShipmentLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,16)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QtyShipped",
                table: "SalesShipmentLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QtyOrdered",
                table: "SalesShipmentLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "SalesQuoteLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,16)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "SalesQuoteLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "SalesOrderLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,16)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "SalesOrderLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QtyShipped",
                table: "SalesOrderLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QtyInvoiced",
                table: "SalesOrderLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "SalesInvoiceLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,16)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityInvoiced",
                table: "SalesInvoiceLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "ReceiptRefundLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,16)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "ReceiptRefundLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "PurchaseRequestLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,16)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityRequested",
                table: "PurchaseRequestLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "PurchaseOrderLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,16)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityReceived",
                table: "PurchaseOrderLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityOrdered",
                table: "PurchaseOrderLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityBilled",
                table: "PurchaseOrderLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "PurchaseInvoiceLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,16)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityBilled",
                table: "PurchaseInvoiceLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "ConversionFactorToBase",
                table: "ItemUomConversionLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,16)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "GoodsReceiptLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,16)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityReceived",
                table: "GoodsReceiptLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "GoodsReceiptDraftLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,16)");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityReceived",
                table: "GoodsReceiptDraftLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "DebitNoteLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,16)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnfulfilledQuantity",
                table: "DebitNoteLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "DebitNoteLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UomConversionFactor",
                table: "CreditNoteLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,16)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnfulfilledQuantity",
                table: "CreditNoteLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "CreditNoteLines",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,12)");
        }
    }
}
