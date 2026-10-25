-- Isolated demonstration database only. Does not modify existing databases/tables.
USE master;
GO
IF DB_ID(N'ETLTeamDev') IS NULL CREATE DATABASE ETLTeamDev;
GO
USE ETLTeamDev;
GO
IF SCHEMA_ID(N'staging') IS NULL EXEC(N'CREATE SCHEMA staging');
GO
IF OBJECT_ID(N'staging.Reservation') IS NULL
CREATE TABLE staging.Reservation(PropertyCode nvarchar(10),ReservationId nvarchar(20),GuestName nvarchar(100),NetAmount decimal(18,2),Status nvarchar(20),ModifiedDate datetime2(3),BatchId nvarchar(40));
IF OBJECT_ID(N'dbo.Reservation') IS NULL
CREATE TABLE dbo.Reservation(PropertyCode nvarchar(10),ReservationId nvarchar(20),GuestName nvarchar(100),RevenueAmount decimal(18,2),Status nvarchar(20),ModifiedDate datetime2(3),LoadBatchId nvarchar(40));
IF OBJECT_ID(N'staging.Customer') IS NULL CREATE TABLE staging.Customer(CustomerId nvarchar(20),Name nvarchar(100),BatchId nvarchar(40));
IF OBJECT_ID(N'dbo.Customer') IS NULL CREATE TABLE dbo.Customer(CustomerId nvarchar(20),Name nvarchar(100),LoadBatchId nvarchar(40));
IF OBJECT_ID(N'staging.Payment') IS NULL CREATE TABLE staging.Payment(PaymentId nvarchar(20),PropertyCode nvarchar(10),ReservationId nvarchar(20),Amount decimal(18,2),BatchId nvarchar(40));
IF OBJECT_ID(N'dbo.Payment') IS NULL CREATE TABLE dbo.Payment(PaymentId nvarchar(20),PropertyCode nvarchar(10),ReservationId nvarchar(20),Amount decimal(18,2),LoadBatchId nvarchar(40));
GO
-- Replace only the explicitly named fixture batch, preserving all other batches.
BEGIN TRANSACTION;
DELETE staging.Reservation WHERE BatchId=N'B-20261024'; DELETE dbo.Reservation WHERE LoadBatchId=N'B-20261024';
DELETE staging.Customer WHERE BatchId=N'B-20261024'; DELETE dbo.Customer WHERE LoadBatchId=N'B-20261024';
DELETE staging.Payment WHERE BatchId=N'B-20261024'; DELETE dbo.Payment WHERE LoadBatchId=N'B-20261024';
INSERT staging.Reservation VALUES
 (N'SG',N'R001',N' Alice ',100,N'Active','2026-10-23T00:00:00',N'B-20261024'),
 (N'SG',N'R002',N'Bob',200,N'Active','2026-10-23T12:00:00',N'B-20261024'),
 (N'SG',N'R003',N'Missing',300,N'Active','2026-10-23T12:00:00',N'B-20261024'),
 (N'SG',N'R004',N'Duplicate',400,N'Active','2026-10-23T12:00:00',N'B-20261024'),
 (N'SG',N'R004',N'Duplicate again',400,N'Active','2026-10-23T12:00:00',N'B-20261024'),
 (N'SG',NULL,N'Null key',500,NULL,'2026-10-23T12:00:00',N'B-20261024'),
 (N'SG',N'R006',NULL,600,NULL,'2026-10-24T00:00:00',N'B-20261024');
INSERT dbo.Reservation VALUES
 (N'SG',N'R001',N'Alice',100.01,N'Active','2026-10-23T00:00:00',N'B-20261024'),
 (N'SG',N'R002',N'Changed Bob',225,N'Inactive','2026-10-23T12:00:00',N'B-20261024'),
 (N'SG',N'R005',N'Unexpected',300,N'Active','2026-10-23T12:00:00',N'B-20261024'),
 (N'SG',N'R004',N'Duplicate',400,N'Active','2026-10-23T12:00:00',N'B-20261024'),
 (N'SG',N'R004',N'Duplicate again',400,N'Active','2026-10-23T12:00:00',N'B-20261024'),
 (N'SG',NULL,N'Null key',500,NULL,'2026-10-23T12:00:00',N'B-20261024'),
 (N'SG',N'R006',NULL,600,NULL,'2026-10-24T00:00:00',N'B-20261024');
INSERT staging.Customer VALUES(N'C001',N' Jane ',N'B-20261024'),(N'C002',NULL,N'B-20261024');
INSERT dbo.Customer VALUES(N'C001',N'JANE',N'B-20261024'),(N'C002',NULL,N'B-20261024');
INSERT staging.Payment VALUES(N'P001',N'SG',N'R001',100,N'B-20261024'),(N'P002',N'SG',N'R999',20,N'B-20261024');
INSERT dbo.Payment VALUES(N'P001',N'SG',N'R001',100.01,N'B-20261024'),(N'P002',N'SG',N'R999',20.02,N'B-20261024');
COMMIT;
GO
