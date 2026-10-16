-- Reporting views. One definition of each business metric, shared by the dashboard, the reports and
-- (from phase P6) the AI "ask your data" feature, which may query ONLY these views.
--
-- Idempotent: run after every migration by DbObjectsInitializer. Batches are separated by GO lines.
-- Conventions:
--   * Cancelled invoices are excluded everywhere: "sales" always means issued, non-cancelled invoices.
--   * Sales value = taxable value (excluding GST) unless a column says otherwise.
--   * Financial year runs April to March; FinancialYear is labelled like '2026-27'.
--   * "Today" is the date in India.

IF SCHEMA_ID('rpt') IS NULL EXEC('CREATE SCHEMA rpt AUTHORIZATION dbo');
GO
IF SCHEMA_ID('rpt_mgr') IS NULL EXEC('CREATE SCHEMA rpt_mgr AUTHORIZATION dbo');
GO

-- One row per invoice line: the grain for "sales by anything".
CREATE OR ALTER VIEW rpt.SalesLines AS
SELECT
    il.Id                                           AS InvoiceLineId,
    i.Id                                            AS InvoiceId,
    i.Number                                        AS InvoiceNumber,
    i.InvoiceDate,
    DATEFROMPARTS(YEAR(i.InvoiceDate), MONTH(i.InvoiceDate), 1) AS MonthStart,
    i.FinancialYear                                 AS FinancialYearStart,
    CONCAT(i.FinancialYear, '-', RIGHT(CAST(i.FinancialYear + 1 AS varchar(4)), 2)) AS FinancialYear,
    i.CustomerId,
    c.Code                                          AS CustomerCode,
    i.CustomerName,
    c.BillingCity                                   AS CustomerCity,
    i.PlaceOfSupplyStateCode                        AS StateCode,
    i.PlaceOfSupplyStateName                        AS StateName,
    CASE WHEN i.CustomerGstin IS NULL THEN 'B2C' ELSE 'B2B' END AS SupplyType,
    i.IsInterState,
    rep.FullName                                    AS SalesRepName,
    il.ProductId,
    il.ProductSku                                   AS Sku,
    il.ProductName,
    cat.Name                                        AS CategoryName,
    il.HsnCode,
    il.Unit,
    il.Quantity,
    il.UnitPrice,
    il.DiscountPercent,
    il.TaxableValue,
    il.GstRate,
    il.CgstAmount,
    il.SgstAmount,
    il.IgstAmount,
    il.CgstAmount + il.SgstAmount + il.IgstAmount   AS TaxAmount,
    il.LineTotal
FROM dbo.InvoiceLines il
JOIN dbo.Invoices i     ON i.Id = il.InvoiceId
JOIN dbo.Customers c    ON c.Id = i.CustomerId
JOIN dbo.Products p     ON p.Id = il.ProductId
JOIN dbo.Categories cat ON cat.Id = p.CategoryId
LEFT JOIN dbo.AspNetUsers rep ON rep.Id = c.AssignedRepId
WHERE i.Status <> 'Cancelled';
GO

-- One row per issued (non-cancelled) invoice, with what's still owed.
CREATE OR ALTER VIEW rpt.Invoices AS
SELECT
    i.Id                                            AS InvoiceId,
    i.Number                                        AS InvoiceNumber,
    i.InvoiceDate,
    i.DueDate,
    DATEFROMPARTS(YEAR(i.InvoiceDate), MONTH(i.InvoiceDate), 1) AS MonthStart,
    i.FinancialYear                                 AS FinancialYearStart,
    CONCAT(i.FinancialYear, '-', RIGHT(CAST(i.FinancialYear + 1 AS varchar(4)), 2)) AS FinancialYear,
    i.CustomerId,
    i.CustomerName,
    i.PlaceOfSupplyStateName                        AS StateName,
    CASE WHEN i.CustomerGstin IS NULL THEN 'B2C' ELSE 'B2B' END AS SupplyType,
    i.IsInterState,
    i.Status,
    i.TaxableValue,
    i.CgstAmount,
    i.SgstAmount,
    i.IgstAmount,
    i.CgstAmount + i.SgstAmount + i.IgstAmount      AS TaxAmount,
    i.RoundOff,
    i.GrandTotal,
    i.AmountPaid,
    i.GrandTotal - i.AmountPaid                     AS Outstanding,
    CASE WHEN i.Status IN ('Unpaid', 'PartiallyPaid') AND i.DueDate < t.Today
         THEN DATEDIFF(day, i.DueDate, t.Today) ELSE 0 END AS DaysOverdue
FROM dbo.Invoices i
CROSS APPLY (SELECT CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'India Standard Time' AS date) AS Today) t
WHERE i.Status <> 'Cancelled';
GO

CREATE OR ALTER VIEW rpt.Payments AS
SELECT
    p.Id                                            AS PaymentId,
    p.PaymentDate,
    DATEFROMPARTS(YEAR(p.PaymentDate), MONTH(p.PaymentDate), 1) AS MonthStart,
    p.Amount,
    p.Mode,
    p.InvoiceId,
    i.Number                                        AS InvoiceNumber,
    i.CustomerId,
    i.CustomerName
FROM dbo.Payments p
JOIN dbo.Invoices i ON i.Id = p.InvoiceId;
GO

-- Customers without contact details (phone, email, notes are deliberately left out).
CREATE OR ALTER VIEW rpt.Customers AS
SELECT
    c.Id                                            AS CustomerId,
    c.Code,
    c.Name,
    c.TradeName,
    c.BillingCity                                   AS City,
    c.BillingStateCode                              AS StateCode,
    s.Name                                          AS StateName,
    CAST(CASE WHEN c.Gstin IS NULL THEN 0 ELSE 1 END AS bit) AS IsGstRegistered,
    c.CreditLimit,
    c.PaymentTermsDays,
    rep.FullName                                    AS SalesRepName,
    c.IsActive,
    agg.FirstInvoiceDate,
    agg.LastInvoiceDate
FROM dbo.Customers c
JOIN dbo.States s ON s.Code = c.BillingStateCode
LEFT JOIN dbo.AspNetUsers rep ON rep.Id = c.AssignedRepId
OUTER APPLY (
    SELECT MIN(i.InvoiceDate) AS FirstInvoiceDate, MAX(i.InvoiceDate) AS LastInvoiceDate
    FROM dbo.Invoices i
    WHERE i.CustomerId = c.Id AND i.Status <> 'Cancelled'
) agg;
GO

-- Products with stock position. Cost price is deliberately left out (see rpt_mgr.Margins).
CREATE OR ALTER VIEW rpt.Products AS
SELECT
    p.Id                                            AS ProductId,
    p.Sku,
    p.Name,
    cat.Name                                        AS CategoryName,
    h.Code                                          AS HsnCode,
    h.GstRate,
    p.Unit,
    p.ListPrice,
    ISNULL(cr.Rate, p.ListPrice)                    AS CurrentPrice,
    p.ReorderLevel,
    p.ReorderQuantity,
    p.LeadTimeDays,
    ISNULL(sl.QuantityOnHand, 0)                    AS QuantityOnHand,
    CASE WHEN ISNULL(sl.QuantityOnHand, 0) <= 0 THEN 'OutOfStock'
         WHEN ISNULL(sl.QuantityOnHand, 0) <= p.ReorderLevel THEN 'Low'
         ELSE 'InStock' END                         AS StockStatus,
    sl.LastMovementAtUtc,
    p.IsActive
FROM dbo.Products p
JOIN dbo.Categories cat ON cat.Id = p.CategoryId
JOIN dbo.HsnCodes h     ON h.Id = p.HsnCodeId
LEFT JOIN dbo.StockLevels sl ON sl.ProductId = p.Id
CROSS APPLY (SELECT CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'India Standard Time' AS date) AS Today) t
-- Today's rate from the rate lists, when a period covers today.
OUTER APPLY (SELECT TOP (1) r.Rate FROM dbo.ProductRates r
             WHERE r.ProductId = p.Id AND r.EffectiveFrom <= t.Today AND (r.EffectiveTo IS NULL OR r.EffectiveTo >= t.Today)
             ORDER BY r.EffectiveFrom DESC) cr;
GO

CREATE OR ALTER VIEW rpt.StockMovements AS
SELECT
    m.Id                                            AS MovementId,
    m.MovementDate,
    DATEFROMPARTS(YEAR(m.MovementDate), MONTH(m.MovementDate), 1) AS MonthStart,
    m.ProductId,
    p.Sku,
    p.Name                                          AS ProductName,
    m.Type                                          AS MovementType,
    m.Quantity,
    m.BalanceAfter,
    m.Reason,
    m.ReferenceType,
    m.ReferenceNumber
FROM dbo.StockMovements m
JOIN dbo.Products p ON p.Id = m.ProductId;
GO

CREATE OR ALTER VIEW rpt.Orders AS
SELECT
    o.Id                                            AS OrderId,
    o.Number                                        AS OrderNumber,
    o.QuotationNumber,
    o.Status,
    o.Source,
    o.OrderDate,
    DATEFROMPARTS(YEAR(o.OrderDate), MONTH(o.OrderDate), 1) AS MonthStart,
    o.CustomerId,
    c.Name                                          AS CustomerName,
    o.TaxableValue,
    o.TaxAmount,
    o.GrandTotal
FROM dbo.SalesOrders o
JOIN dbo.Customers c ON c.Id = o.CustomerId;
GO

-- Managers only: margin per invoice line at the product's CURRENT cost price (invoices don't record cost).
CREATE OR ALTER VIEW rpt_mgr.Margins AS
SELECT
    sl.InvoiceLineId,
    sl.InvoiceId,
    sl.InvoiceNumber,
    sl.InvoiceDate,
    sl.MonthStart,
    sl.FinancialYear,
    sl.CustomerId,
    sl.CustomerName,
    sl.ProductId,
    sl.Sku,
    sl.ProductName,
    sl.CategoryName,
    sl.Quantity,
    sl.TaxableValue,
    p.CostPrice,
    sl.Quantity * p.CostPrice                       AS CostValue,
    sl.TaxableValue - sl.Quantity * p.CostPrice     AS GrossMargin
FROM rpt.SalesLines sl
JOIN dbo.Products p ON p.Id = sl.ProductId
WHERE p.CostPrice IS NOT NULL;
GO

-- Database users for the AI query path (phase P6). They have no login: the API impersonates them with
-- EXECUTE AS USER ... WITH NO REVERT on a dedicated connection. They can read the reporting views and
-- nothing else; ownership chaining lets the views read the dbo tables beneath them.
IF DATABASE_PRINCIPAL_ID('ai_reader') IS NULL CREATE USER ai_reader WITHOUT LOGIN;
GO
IF DATABASE_PRINCIPAL_ID('ai_reader_mgr') IS NULL CREATE USER ai_reader_mgr WITHOUT LOGIN;
GO
GRANT SELECT ON SCHEMA::rpt TO ai_reader;
GRANT SELECT ON SCHEMA::rpt TO ai_reader_mgr;
GRANT SELECT ON SCHEMA::rpt_mgr TO ai_reader_mgr;
DENY SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::dbo TO ai_reader;
DENY SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::dbo TO ai_reader_mgr;
DENY SELECT ON SCHEMA::rpt_mgr TO ai_reader;
GO
