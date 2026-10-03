using Dapper;
using DonationArchiving.Models;
using Npgsql;

namespace DonationArchiving.Repositories;

public class DonationRepository
{
    private readonly string _connectionString;

    public DonationRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private NpgsqlConnection CreateConnection()
    {
        return new NpgsqlConnection(_connectionString);
    }


    // =========================================================
    // INSERT DONATION
    // =========================================================

    public async Task<long> InsertDonationAsync(Donation donation)
    {
        ValidateDonation(donation);

        const string sql = """
            INSERT INTO donations
                (username, donation_money, donation_date)
            VALUES
                (@Username, @DonationMoney, @DonationDate)
            RETURNING id;
            """;

        await using var connection = CreateConnection();

        return await connection.ExecuteScalarAsync<long>(
            sql,
            donation
        );
    }


    // =========================================================
    // IMPORT DONATIONS
    // =========================================================

    public async Task<int> ImportDonationsAsync(
        IEnumerable<Donation> donations)
    {
        var list = donations.ToList();

        if (list.Count == 0)
            return 0;

        foreach (var donation in list)
        {
            ValidateDonation(donation);
        }

        const string sql = """
            INSERT INTO donations
                (username, donation_money, donation_date)
            VALUES
                (@Username, @DonationMoney, @DonationDate);
            """;

        await using var connection = CreateConnection();

        await connection.OpenAsync();

        await using var transaction =
            await connection.BeginTransactionAsync();

        try
        {
            var inserted = await connection.ExecuteAsync(
                sql,
                list,
                transaction
            );

            await transaction.CommitAsync();

            return inserted;
        }
        catch
        {
            await transaction.RollbackAsync();

            throw;
        }
    }


    // =========================================================
    // DISPLAY DONATIONS
    // =========================================================

    public async Task<List<Donation>> GetDonationsAsync()
    {
        const string sql = """
            SELECT
                id AS Id,
                username AS Username,
                donation_money AS DonationMoney,
                donation_date AS DonationDate,
                archive_batch_id AS ArchiveBatchId
            FROM donations
            ORDER BY donation_date DESC;
            """;

        await using var connection = CreateConnection();

        var donations =
            await connection.QueryAsync<Donation>(sql);

        return donations.ToList();
    }


    // =========================================================
    // GET PENDING ARCHIVE
    // =========================================================

    public async Task<ArchiveRecord?> GetPendingArchiveAsync()
    {
        const string sql = """
            SELECT
                archive_id AS ArchiveId,
                sequence_number AS SequenceNumber,
                previous_hash AS PreviousHash,
                current_hash AS CurrentHash,
                s3_key AS ArchivePath,
                record_count AS RecordCount,
                status AS Status,
                created_at AS CreatedAt,
                completed_at AS CompletedAt,
                last_error AS LastError
            FROM archive_history
            WHERE status = 'Pending'
            ORDER BY sequence_number
            LIMIT 1;
            """;

        await using var connection = CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<ArchiveRecord>(
            sql
        );
    }


    // =========================================================
    // CREATE / CLAIM ARCHIVE BATCH
    // =========================================================

    public async Task<ArchiveRecord?> CreateArchiveBatchAsync(
        DateTimeOffset cutoff,
        int batchSize = 1000)
    {
        await using var connection = CreateConnection();

        await connection.OpenAsync();

        await using var transaction =
            await connection.BeginTransactionAsync();

        try
        {
            var ids = (
                await connection.QueryAsync<long>(
                    """
                    SELECT id
                    FROM donations
                    WHERE donation_date < @Cutoff
                      AND archive_batch_id IS NULL
                    ORDER BY id
                    FOR UPDATE SKIP LOCKED
                    LIMIT @BatchSize;
                    """,
                    new
                    {
                        Cutoff = cutoff,
                        BatchSize = batchSize
                    },
                    transaction
                )
            ).ToArray();


            if (ids.Length == 0)
            {
                await transaction.RollbackAsync();

                return null;
            }


            var previousHash =
                await connection.QuerySingleOrDefaultAsync<string>(
                    """
                    SELECT current_hash
                    FROM archive_history
                    WHERE status = 'Completed'
                    ORDER BY sequence_number DESC
                    LIMIT 1;
                    """,
                    transaction: transaction
                )
                ?? "GENESIS";


            var archiveId = Guid.NewGuid();

            var archivePath =
                $"archives/donations/{archiveId:N}.json";


            var archive =
                await connection.QuerySingleAsync<ArchiveRecord>(
                    """
                    INSERT INTO archive_history
                    (
                        archive_id,
                        previous_hash,
                        s3_key,
                        record_count,
                        status
                    )
                    VALUES
                    (
                        @ArchiveId,
                        @PreviousHash,
                        @ArchivePath,
                        @RecordCount,
                        'Pending'
                    )

                    RETURNING
                        archive_id AS ArchiveId,
                        sequence_number AS SequenceNumber,
                        previous_hash AS PreviousHash,
                        current_hash AS CurrentHash,
                        s3_key AS ArchivePath,
                        record_count AS RecordCount,
                        status AS Status,
                        created_at AS CreatedAt,
                        completed_at AS CompletedAt,
                        last_error AS LastError;
                    """,
                    new
                    {
                        ArchiveId = archiveId,
                        PreviousHash = previousHash,
                        ArchivePath = archivePath,
                        RecordCount = ids.Length
                    },
                    transaction
                );


            await connection.ExecuteAsync(
                """
                UPDATE donations
                SET archive_batch_id = @ArchiveId
                WHERE id = ANY(@Ids);
                """,
                new
                {
                    ArchiveId = archiveId,
                    Ids = ids
                },
                transaction
            );


            await transaction.CommitAsync();

            return archive;
        }
        catch
        {
            await transaction.RollbackAsync();

            throw;
        }
    }


    // =========================================================
    // GET BATCH DONATIONS
    // =========================================================

    public async Task<List<Donation>>
        GetArchiveDonationsAsync(Guid archiveId)
    {
        const string sql = """
            SELECT
                id AS Id,
                username AS Username,
                donation_money AS DonationMoney,
                donation_date AS DonationDate,
                archive_batch_id AS ArchiveBatchId
            FROM donations
            WHERE archive_batch_id = @ArchiveId
            ORDER BY id;
            """;

        await using var connection = CreateConnection();

        var donations =
            await connection.QueryAsync<Donation>(
                sql,
                new
                {
                    ArchiveId = archiveId
                }
            );

        return donations.ToList();
    }


    // =========================================================
    // SAVE HASH
    // =========================================================

    public async Task UpdateArchiveHashAsync(
        Guid archiveId,
        string currentHash)
    {
        const string sql = """
            UPDATE archive_history
            SET current_hash = @CurrentHash,
                last_error = NULL
            WHERE archive_id = @ArchiveId;
            """;

        await using var connection = CreateConnection();

        await connection.ExecuteAsync(
            sql,
            new
            {
                ArchiveId = archiveId,
                CurrentHash = currentHash
            }
        );
    }


    // =========================================================
    // SAVE ERROR
    // =========================================================

    public async Task SaveArchiveErrorAsync(
        Guid archiveId,
        string error)
    {
        const string sql = """
            UPDATE archive_history
            SET last_error = @Error
            WHERE archive_id = @ArchiveId;
            """;

        await using var connection = CreateConnection();

        await connection.ExecuteAsync(
            sql,
            new
            {
                ArchiveId = archiveId,
                Error = error
            }
        );
    }


    // =========================================================
    // COMPLETE ARCHIVE
    // =========================================================

    public async Task<IReadOnlyList<long>> CompleteArchiveAsync(
        ArchiveRecord archive,
        IReadOnlyCollection<long> expectedIds)
    {
        await using var connection = CreateConnection();

        await connection.OpenAsync();

        await using var transaction =
            await connection.BeginTransactionAsync();

        try
        {
            var deletedIds = (
                await connection.QueryAsync<long>(
                    """
                    DELETE FROM donations
                    WHERE archive_batch_id = @ArchiveId
                    RETURNING id;
                    """,
                    new
                    {
                        archive.ArchiveId
                    },
                    transaction
                )
            ).OrderBy(id => id).ToArray();

            var archivedIds = expectedIds.OrderBy(id => id).ToArray();

            if (!deletedIds.SequenceEqual(archivedIds))
            {
                throw new InvalidOperationException(
                    $"Archived IDs [{string.Join(",", archivedIds)}] do not match " +
                    $"deleted IDs [{string.Join(",", deletedIds)}]."
                );
            }

            var remainingIds = (
                await connection.QueryAsync<long>(
                    """
                    SELECT id
                    FROM donations
                    WHERE id = ANY(@Ids);
                    """,
                    new { Ids = archivedIds },
                    transaction
                )
            ).ToArray();

            if (remainingIds.Length != 0)
            {
                throw new InvalidOperationException(
                    $"Deleted donation IDs still exist: [{string.Join(",", remainingIds)}]."
                );
            }


            await connection.ExecuteAsync(
                """
                UPDATE archive_history
                SET status = 'Completed',
                    completed_at = NOW(),
                    last_error = NULL
                WHERE archive_id = @ArchiveId;
                """,
                new
                {
                    archive.ArchiveId
                },
                transaction
            );


            await transaction.CommitAsync();

            return deletedIds;
        }
        catch
        {
            await transaction.RollbackAsync();

            throw;
        }
    }


    // =========================================================
    // ARCHIVE HISTORY
    // =========================================================

    public async Task<List<ArchiveRecord>>
        GetCompletedArchivesAsync()
    {
        const string sql = """
            SELECT
                archive_id AS ArchiveId,
                sequence_number AS SequenceNumber,
                previous_hash AS PreviousHash,
                current_hash AS CurrentHash,
                s3_key AS ArchivePath,
                record_count AS RecordCount,
                status AS Status,
                created_at AS CreatedAt,
                completed_at AS CompletedAt,
                last_error AS LastError
            FROM archive_history
            WHERE status = 'Completed'
            ORDER BY sequence_number;
            """;

        await using var connection = CreateConnection();

        var result =
            await connection.QueryAsync<ArchiveRecord>(sql);

        return result.ToList();
    }


    private static void ValidateDonation(Donation donation)
    {
        if (string.IsNullOrWhiteSpace(donation.Username))
        {
            throw new ArgumentException(
                "Username is required."
            );
        }

        if (donation.DonationMoney <= 0)
        {
            throw new ArgumentException(
                "Donation money must be greater than zero."
            );
        }

        if (donation.DonationDate >
            DateTimeOffset.UtcNow.AddMinutes(1))
        {
            throw new ArgumentException(
                "Donation date cannot be in the future."
            );
        }
    }
}
