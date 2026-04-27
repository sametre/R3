using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using R3.Application.Abstractions;
using R3.Application.Authentication;

namespace R3.Infrastructure.Security;

internal sealed class SqlAuthenticationService : IAuthenticationService
{
    private readonly string _connectionString;

    public SqlAuthenticationService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("R3Database")
            ?? throw new InvalidOperationException("R3Database bağlantı dizesi bulunamadı.");
    }

    public async Task<IReadOnlyList<LoginBranchOption>> GetBranchesAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT b.BranchId, b.BranchCode, b.BranchName
            FROM core.Branch b
            JOIN core.Company c ON c.CompanyId = b.CompanyId
            WHERE b.IsActive = 1
              AND c.IsActive = 1
            ORDER BY b.IsHeadOffice DESC, b.BranchName;
            """;

        var result = new List<LoginBranchOption>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new LoginBranchOption(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2)));
        }
        return result;
    }

    public async Task<IReadOnlyList<LoginUserOption>> GetUsersAsync(
        int branchId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT DISTINCT u.UserId, u.UserName, u.DisplayName
            FROM sec.AppUser u
            JOIN sec.UserCompany uc ON uc.UserId = u.UserId
            JOIN core.Branch b ON b.CompanyId = uc.CompanyId
            WHERE b.BranchId = @BranchId
              AND b.IsActive = 1
              AND u.IsActive = 1
              AND u.IsLocked = 0
              AND (uc.DefaultBranchId IS NULL OR uc.DefaultBranchId = b.BranchId)
            ORDER BY u.DisplayName;
            """;

        var result = new List<LoginUserOption>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@BranchId", SqlDbType.Int).Value = branchId;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new LoginUserOption(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2)));
        }
        return result;
    }

    public async Task<LoginResult> AuthenticateAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        string storeNumber = request.StoreNumber.Trim().ToUpperInvariant();
        string normalizedUserName = request.UserName.Trim().ToUpperInvariant();

        if (storeNumber.Length is < 2 or > 20)
            return LoginResult.Failure("Şube / Mağaza No geçersiz.");
        if (normalizedUserName.Length is < 2 or > 100)
            return LoginResult.Failure("Kullanıcı adı geçersiz.");
        if (request.Pin.Length is < 4 or > 12 || request.Pin.Any(character => !char.IsDigit(character)))
            return LoginResult.Failure("PIN 4-12 rakamdan oluşmalıdır.");

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string query = """
            SELECT TOP (1)
                u.UserId,
                u.UserName,
                u.DisplayName,
                u.PasswordHash,
                u.IsActive,
                u.IsLocked,
                u.FailedLoginCount,
                c.CompanyId,
                c.LegalName AS CompanyName,
                b.BranchId,
                b.BranchCode,
                b.BranchName
            FROM sec.AppUser u
            JOIN sec.UserCompany uc ON uc.UserId = u.UserId
            JOIN core.Company c ON c.CompanyId = uc.CompanyId AND c.IsActive = 1
            JOIN core.Branch b ON b.CompanyId = c.CompanyId AND b.IsActive = 1
            WHERE u.NormalizedUserName = @UserName
              AND b.BranchCode = @StoreNumber
              AND (uc.DefaultBranchId IS NULL OR uc.DefaultBranchId = b.BranchId)
            ORDER BY uc.IsDefault DESC, b.BranchId;
            """;

        await using var command = new SqlCommand(query, connection);
        command.Parameters.Add("@UserName", SqlDbType.NVarChar, 100).Value = normalizedUserName;
        command.Parameters.Add("@StoreNumber", SqlDbType.VarChar, 20).Value = storeNumber;

        await using SqlDataReader reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow,
            cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return LoginResult.Failure("Giriş bilgileri doğrulanamadı.");

        int userId = reader.GetInt32(reader.GetOrdinal("UserId"));
        bool isActive = reader.GetBoolean(reader.GetOrdinal("IsActive"));
        bool isLocked = reader.GetBoolean(reader.GetOrdinal("IsLocked"));
        string passwordHash = reader.GetString(reader.GetOrdinal("PasswordHash"));

        var session = new UserSessionData(
            userId,
            reader.GetString(reader.GetOrdinal("UserName")),
            reader.GetString(reader.GetOrdinal("DisplayName")),
            reader.GetInt32(reader.GetOrdinal("CompanyId")),
            reader.GetString(reader.GetOrdinal("CompanyName")),
            reader.GetInt32(reader.GetOrdinal("BranchId")),
            reader.GetString(reader.GetOrdinal("BranchCode")),
            reader.GetString(reader.GetOrdinal("BranchName")));

        await reader.CloseAsync();

        if (!isActive)
            return LoginResult.Failure("Kullanıcı hesabı aktif değil.");
        if (isLocked)
            return LoginResult.Failure("Kullanıcı hesabı kilitli. Sistem yöneticinize başvurun.");

        if (!PinHasher.Verify(request.Pin, passwordHash))
        {
            await RegisterFailedAttemptAsync(connection, userId, cancellationToken);
            return LoginResult.Failure("Giriş bilgileri doğrulanamadı.");
        }

        await RegisterSuccessfulLoginAsync(connection, userId, cancellationToken);
        return LoginResult.Success(session);
    }

    private static async Task RegisterFailedAttemptAsync(
        SqlConnection connection,
        int userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE sec.AppUser
               SET FailedLoginCount = FailedLoginCount + 1,
                   IsLocked = CASE WHEN FailedLoginCount + 1 >= 5 THEN 1 ELSE IsLocked END
             WHERE UserId = @UserId;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RegisterSuccessfulLoginAsync(
        SqlConnection connection,
        int userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE sec.AppUser
               SET FailedLoginCount = 0,
                   LastLoginAtUtc = SYSUTCDATETIME()
             WHERE UserId = @UserId;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
