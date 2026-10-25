-- Copy a query into a sqlAssertion test with expect.rowCount=0.
-- All statements are read-only. Configure table/column names and batch scope first.

-- Required / whitespace values
SELECT ReservationId FROM dbo.Reservation
WHERE LoadBatchId=@batchId AND (GuestName IS NULL OR LTRIM(RTRIM(GuestName))=N'');

-- Unique composite business key (does not rely on a declared constraint)
SELECT PropertyCode, ReservationId, COUNT_BIG(*) AS DuplicateCount
FROM dbo.Reservation WHERE LoadBatchId=@batchId
GROUP BY PropertyCode, ReservationId HAVING COUNT_BIG(*) > 1;

-- Allowed values, explicitly allowing null here
SELECT ReservationId, Status FROM dbo.Reservation
WHERE LoadBatchId=@batchId AND Status NOT IN(N'Active',N'Inactive');

-- Numeric range, nulls fail explicitly
SELECT ReservationId, RevenueAmount FROM dbo.Reservation
WHERE LoadBatchId=@batchId AND (RevenueAmount IS NULL OR RevenueAmount<0 OR RevenueAmount>1000000);

-- Null business keys
SELECT PropertyCode, ReservationId FROM dbo.Reservation
WHERE LoadBatchId=@batchId AND (PropertyCode IS NULL OR ReservationId IS NULL);

-- Aggregate expectation: use expect.scalar {kind:"decimal", value:"..."}
SELECT CAST(SUM(RevenueAmount) AS decimal(18,2)) AS Revenue FROM dbo.Reservation WHERE LoadBatchId=@batchId;
