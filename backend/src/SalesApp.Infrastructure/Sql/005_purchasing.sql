-- Purchasing views, for Ask your data and the assistant. They show what goods cost, so they live in rpt_mgr: only
-- users who may see cost prices can query them (see 001_reporting_views.sql for the users and grants).
--
-- Idempotent: run after every migration by DbObjectsInitializer.
-- Conventions, as for sales:
--   * Cancelled bills and purchase orders are excluded everywhere.
--   * Values are before GST unless a column says otherwise; GST on supplier bills is the input tax credit.
--   * Financial year runs April to March; "today" is the date in India.

CREATE OR ALTER VIEW rpt_mgr.Suppliers AS
SELECT
    s.Id                                            AS SupplierId,
    s.Code,
    s.Name,
    s.TradeName,
    s.City,
    s.StateCode,
    st.Name                                         AS StateName,
    CAST(CASE WHEN s.Gstin IS NULL THEN 0 ELSE 1 END AS bit) AS IsGstRegistered,
    s.PaymentTermsDays,
    s.IsActive
FROM dbo.Suppliers s
JOIN dbo.States st ON st.Code = s.StateCode;
GO

-- One row per purchase order line, with what has arrived and been billed.
CREATE OR ALTER VIEW rpt_mgr.PurchaseOrderLines AS
SELECT
    l.Id                                            AS PurchaseOrderLineId,
    o.Id                                            AS PurchaseOrderId,
    o.Number                                        AS PurchaseOrderNumber,
    o.Status,
    o.OrderDate,
    o.ExpectedDate,
    DATEFROMPARTS(YEAR(o.OrderDate), MONTH(o.OrderDate), 1) AS MonthStart,
    fy.StartYear                                    AS FinancialYearStart,
    CONCAT(fy.StartYear, '-', RIGHT(CAST(fy.StartYear + 1 AS varchar(4)), 2)) AS FinancialYear,
    o.SupplierId,
    s.Name                                          AS SupplierName,
    l.ProductId,
    p.Sku,
    p.Name                                          AS ProductName,
    cat.Name                                        AS CategoryName,
    p.Unit,
    l.Quantity                                      AS QuantityOrdered,
    l.QuantityReceived,
    l.QuantityBilled,
    CASE WHEN o.Status IN ('Ordered', 'PartlyReceived') AND l.Quantity > l.QuantityReceived
         THEN l.Quantity - l.QuantityReceived ELSE 0 END AS QuantityPending,
    l.UnitPrice,
    l.GstRate,
    l.Quantity * l.UnitPrice                        AS OrderedValue,
    CASE WHEN o.Status IN ('Ordered', 'PartlyReceived') AND o.ExpectedDate < t.Today
         THEN DATEDIFF(day, o.ExpectedDate, t.Today) ELSE 0 END AS DaysLate
FROM dbo.PurchaseOrderLines l
JOIN dbo.PurchaseOrders o ON o.Id = l.PurchaseOrderId
JOIN dbo.Suppliers s      ON s.Id = o.SupplierId
JOIN dbo.Products p       ON p.Id = l.ProductId
JOIN dbo.Categories cat   ON cat.Id = p.CategoryId
CROSS APPLY (SELECT CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'India Standard Time' AS date) AS Today) t
CROSS APPLY (SELECT CASE WHEN MONTH(o.OrderDate) >= 4 THEN YEAR(o.OrderDate) ELSE YEAR(o.OrderDate) - 1 END AS StartYear) fy
WHERE o.Status <> 'Cancelled';
GO

-- One row per supplier bill, with what we still owe.
CREATE OR ALTER VIEW rpt_mgr.SupplierBills AS
SELECT
    b.Id                                            AS BillId,
    b.Number                                        AS BillNumber,
    b.SupplierInvoiceNumber,
    b.BillDate,
    b.DueDate,
    DATEFROMPARTS(YEAR(b.BillDate), MONTH(b.BillDate), 1) AS MonthStart,
    b.FinancialYear                                 AS FinancialYearStart,
    CONCAT(b.FinancialYear, '-', RIGHT(CAST(b.FinancialYear + 1 AS varchar(4)), 2)) AS FinancialYear,
    b.SupplierId,
    b.SupplierName,
    b.IsInterState,
    o.Number                                        AS PurchaseOrderNumber,
    b.Status,
    b.MatchStatus,
    b.TaxableValue                                  AS GoodsValue,
    b.ChargesAmount,
    b.TaxableValue + b.ChargesAmount                AS TaxableValue,
    b.CgstAmount,
    b.SgstAmount,
    b.IgstAmount,
    b.CgstAmount + b.SgstAmount + b.IgstAmount      AS TaxAmount,
    b.GrandTotal,
    b.AmountPaid,
    b.GrandTotal - b.AmountPaid                     AS Outstanding,
    CASE WHEN b.Status IN ('Unpaid', 'PartiallyPaid') AND b.DueDate < t.Today
         THEN DATEDIFF(day, b.DueDate, t.Today) ELSE 0 END AS DaysOverdue
FROM dbo.SupplierBills b
JOIN dbo.PurchaseOrders o ON o.Id = b.PurchaseOrderId
CROSS APPLY (SELECT CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'India Standard Time' AS date) AS Today) t
WHERE b.Status <> 'Cancelled';
GO

-- One row per supplier bill line: the grain for "what did we pay for each product".
CREATE OR ALTER VIEW rpt_mgr.SupplierBillLines AS
SELECT
    bl.Id                                           AS BillLineId,
    b.Id                                            AS BillId,
    b.Number                                        AS BillNumber,
    b.BillDate,
    DATEFROMPARTS(YEAR(b.BillDate), MONTH(b.BillDate), 1) AS MonthStart,
    b.FinancialYear                                 AS FinancialYearStart,
    CONCAT(b.FinancialYear, '-', RIGHT(CAST(b.FinancialYear + 1 AS varchar(4)), 2)) AS FinancialYear,
    b.SupplierId,
    b.SupplierName,
    bl.ProductId,
    bl.ProductSku                                   AS Sku,
    bl.ProductName,
    cat.Name                                        AS CategoryName,
    bl.HsnCode,
    bl.Unit,
    bl.Quantity,
    bl.UnitPrice,
    bl.OrderedPrice,
    bl.TaxableValue,
    bl.GstRate,
    bl.CgstAmount + bl.SgstAmount + bl.IgstAmount   AS TaxAmount,
    bl.LineTotal
FROM dbo.SupplierBillLines bl
JOIN dbo.SupplierBills b ON b.Id = bl.SupplierBillId
JOIN dbo.Products p      ON p.Id = bl.ProductId
JOIN dbo.Categories cat  ON cat.Id = p.CategoryId
WHERE b.Status <> 'Cancelled';
GO

CREATE OR ALTER VIEW rpt_mgr.SupplierPayments AS
SELECT
    p.Id                                            AS PaymentId,
    p.PaymentDate,
    DATEFROMPARTS(YEAR(p.PaymentDate), MONTH(p.PaymentDate), 1) AS MonthStart,
    p.Amount,
    p.Mode,
    b.Id                                            AS BillId,
    b.Number                                        AS BillNumber,
    b.SupplierInvoiceNumber,
    b.SupplierId,
    b.SupplierName
FROM dbo.SupplierPayments p
JOIN dbo.SupplierBills b ON b.Id = p.SupplierBillId;
GO
