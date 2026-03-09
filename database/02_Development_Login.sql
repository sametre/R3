/*
  R3 DEVELOPMENT LOGIN ONLY
  Store/Branch: MERKEZ
  User name:    admin
  PIN:          1234

  Do not run this script in production.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
USE EngineeringERP;
GO

BEGIN TRANSACTION;

DECLARE @CompanyId int,
        @BranchId int,
        @UserId int,
        @RoleId int;

SELECT @CompanyId = CompanyId
FROM core.Company
WHERE CompanyCode = 'R3';

IF @CompanyId IS NULL
BEGIN
    INSERT INTO core.Company
    (
        CompanyCode,
        LegalName,
        TradeName,
        TaxOffice,
        TaxNumber,
        DefaultCurrencyCode
    )
    VALUES
    (
        'R3',
        N'R3 Demo İşletmesi A.Ş.',
        N'R3 Demo',
        N'Merkez',
        '0000000000',
        'TRY'
    );

    SET @CompanyId = CONVERT(int, SCOPE_IDENTITY());
END;

UPDATE core.Company
SET LegalName = N'R3 Demo İşletmesi A.Ş.',
    TradeName = N'R3 Demo'
WHERE CompanyId = @CompanyId;

SELECT @BranchId = BranchId
FROM core.Branch
WHERE CompanyId = @CompanyId
  AND BranchCode = 'MERKEZ';

IF @BranchId IS NULL
BEGIN
    INSERT INTO core.Branch
    (
        CompanyId,
        BranchCode,
        BranchName,
        IsHeadOffice
    )
    VALUES
    (
        @CompanyId,
        'MERKEZ',
        N'Merkez Mağaza',
        1
    );

    SET @BranchId = CONVERT(int, SCOPE_IDENTITY());
END;

UPDATE core.Branch
SET BranchName = N'Merkez Mağaza'
WHERE BranchId = @BranchId;

SELECT @UserId = UserId
FROM sec.AppUser
WHERE NormalizedUserName = N'ADMIN';

IF @UserId IS NULL
BEGIN
    INSERT INTO sec.AppUser
    (
        UserName,
        NormalizedUserName,
        DisplayName,
        Email,
        PasswordHash,
        IsActive,
        IsLocked
    )
    VALUES
    (
        N'admin',
        N'ADMIN',
        N'R3 Sistem Yöneticisi',
        'admin@localhost',
        N'R3PIN$150000$sQxErLw6GyPs4fQaFOFqOA==$ixvXmaclKU4PWNRSON9fgyP1/2DzDUj0PScNBz3kpNE=',
        1,
        0
    );

    SET @UserId = CONVERT(int, SCOPE_IDENTITY());
END;

UPDATE sec.AppUser
SET DisplayName = N'R3 Sistem Yöneticisi'
WHERE UserId = @UserId;

IF NOT EXISTS
(
    SELECT 1
    FROM sec.UserCompany
    WHERE UserId = @UserId
      AND CompanyId = @CompanyId
)
BEGIN
    INSERT INTO sec.UserCompany(UserId, CompanyId, DefaultBranchId, IsDefault)
    VALUES (@UserId, @CompanyId, @BranchId, 1);
END;

SELECT @RoleId = RoleId
FROM sec.Role
WHERE RoleCode = 'SYSTEM_ADMIN';

IF @RoleId IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sec.UserRole
       WHERE UserId = @UserId
         AND CompanyId = @CompanyId
         AND RoleId = @RoleId
   )
BEGIN
    INSERT INTO sec.UserRole(UserId, CompanyId, RoleId)
    VALUES (@UserId, @CompanyId, @RoleId);
END;

COMMIT TRANSACTION;
GO
