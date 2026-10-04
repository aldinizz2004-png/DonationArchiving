using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DonationArchiving.Models;
using DonationArchiving.Repositories;
using DonationArchiving.Storage;

namespace DonationArchiving.Services;

public class ArchivingService
{
    private readonly DonationRepository _repository;

    private readonly IArchiveStorage _storage;


    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNamingPolicy =
                JsonNamingPolicy.CamelCase,

            WriteIndented = true
        };


    private static readonly JsonSerializerOptions HashJsonOptions =
        new()
        {
            PropertyNamingPolicy =
                JsonNamingPolicy.CamelCase,

            WriteIndented = false
        };


    public ArchivingService(
        DonationRepository repository,
        IArchiveStorage storage)
    {
        _repository = repository;

        _storage = storage;
    }


    public async Task<bool> ArchiveOldDonationsAsync()
    {
        ArchiveRecord? archive = null;

        try
        {
            // First recover unfinished work.
            archive =
                await _repository.GetPendingArchiveAsync();


            if (archive is null)
            {
                var cutoff =
                    DateTimeOffset.UtcNow.AddDays(-7);

                archive =
                    await _repository
                        .CreateArchiveBatchAsync(cutoff);
            }


            if (archive is null)
            {
                Console.WriteLine(
                    "[ARCHIVE] No donations older than 7 days."
                );

                return true;
            }


            Console.WriteLine(
                $"[ARCHIVE] Processing archive " +
                $"{archive.SequenceNumber} " +
                $"({archive.ArchiveId})"
            );


            var donations =
                await _repository
                    .GetArchiveDonationsAsync(
                        archive.ArchiveId
                    );


            if (donations.Count == 0)
            {
                throw new InvalidOperationException(
                    "Archive batch contains no donations."
                );
            }


            var archiveDonations =
                donations
                    .OrderBy(x => x.Id)
                    .Select(x => new ArchiveDonation
                    {
                        Id = x.Id,

                        Username = x.Username,

                        DonationMoney =
                            x.DonationMoney,

                        DonationDate =
                            x.DonationDate
                    })
                    .ToList();


            // Hash only the actual donation payload.
            var payloadJson =
                JsonSerializer.Serialize(
                    archiveDonations,
                    HashJsonOptions
                );


            var payloadHash =
                Sha256(payloadJson);


            /*
             * Git-style commit hash:
             *
             * Previous commit hash
             * +
             * current donation payload hash
             * +
             * archive identity
             */
            var currentHash =
                Sha256(
                    $"{archive.SequenceNumber}|" +
                    $"{archive.ArchiveId:N}|" +
                    $"{archive.PreviousHash}|" +
                    $"{payloadHash}"
                );


            await _repository.UpdateArchiveHashAsync(
                archive.ArchiveId,
                currentHash
            );


            archive.CurrentHash = currentHash;


            var archiveFile =
                new ArchiveFile
                {
                    ArchiveId =
                        archive.ArchiveId,

                    SequenceNumber =
                        archive.SequenceNumber,

                    PreviousHash =
                        archive.PreviousHash,

                    PayloadHash =
                        payloadHash,

                    CurrentHash =
                        currentHash,

                    RecordCount =
                        archiveDonations.Count,

                    CreatedAt =
                        archive.CreatedAt,

                    Donations =
                        archiveDonations
                };


            var json =
                JsonSerializer.Serialize(
                    archiveFile,
                    JsonOptions
                );


            var fullPath = await _storage.WriteAsync(
                archive.ArchivePath,
                json
            );


            Console.WriteLine(
                $"[ARCHIVE] File created: {fullPath}"
            );


            if (!await _storage.ExistsAsync(archive.ArchivePath))
            {
                throw new IOException(
                    "Archive storage did not confirm that the object exists."
                );
            }


            // Never trust only the upload response.
            // Read it back and verify it.
            var storedJson =
                await _storage.ReadAsync(
                    archive.ArchivePath
                );


            var downloaded =
                JsonSerializer.Deserialize<ArchiveFile>(
                    storedJson,
                    JsonOptions
                );


            if (downloaded is null)
            {
                throw new InvalidOperationException(
                    "Could not deserialize stored archive."
                );
            }


            VerifyArchiveFile(
                downloaded,
                archive,
                archiveDonations.Select(x => x.Id)
            );


            Console.WriteLine(
                "[ARCHIVE] Stored archive verified successfully."
            );


            // Only now are SQL donations allowed to disappear.
            var archivedIds = archiveDonations.Select(x => x.Id).ToArray();
            var deletedIds = await _repository.CompleteArchiveAsync(
                archive,
                archivedIds
            );

            Console.WriteLine($"[ARCHIVE] Archived IDs: [{string.Join(", ", archivedIds)}]");
            Console.WriteLine($"[ARCHIVE] Deleted IDs:  [{string.Join(", ", deletedIds)}]");
            Console.WriteLine("[ARCHIVE] Database check confirmed the archived IDs are gone.");


            Console.WriteLine(
                $"[ARCHIVE] Archived " +
                $"{archive.RecordCount} donations successfully."
            );


            Console.WriteLine(
                $"[ARCHIVE] HASH: {currentHash}"
            );

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[ARCHIVE ERROR] {ex.Message}"
            );


            if (archive is not null)
            {
                try
                {
                    await _repository
                        .SaveArchiveErrorAsync(
                            archive.ArchiveId,
                            ex.Message
                        );
                }
                catch
                {
                    // Do not hide original error.
                }
            }

            return false;
        }
    }


    public async Task VerifyArchiveChainAsync()
    {
        var archives =
            await _repository
                .GetCompletedArchivesAsync();


        if (archives.Count == 0)
        {
            Console.WriteLine(
                "No completed archives."
            );

            return;
        }


        var expectedPreviousHash =
            "GENESIS";


        foreach (var archive in archives)
        {
            if (archive.PreviousHash !=
                expectedPreviousHash)
            {
                Console.WriteLine(
                    $"❌ Archive chain broken at " +
                    $"#{archive.SequenceNumber}"
                );

                return;
            }


            var json =
                await _storage.ReadAsync(
                    archive.ArchivePath
                );


            var file =
                JsonSerializer.Deserialize<ArchiveFile>(
                    json,
                    JsonOptions
                );


            if (file is null)
            {
                Console.WriteLine(
                    $"❌ Could not read archive " +
                    $"#{archive.SequenceNumber}"
                );

                return;
            }


            try
            {
                VerifyArchiveFile(
                    file,
                    archive
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"❌ Archive #{archive.SequenceNumber}: " +
                    ex.Message
                );

                return;
            }


            expectedPreviousHash =
                archive.CurrentHash!;
        }


        Console.WriteLine(
            $"✅ Entire archive chain verified. " +
            $"{archives.Count} archive(s) are valid."
        );
    }


    private static void VerifyArchiveFile(
        ArchiveFile file,
        ArchiveRecord databaseArchive,
        IEnumerable<long>? expectedIds = null)
    {
        if (file.ArchiveId !=
            databaseArchive.ArchiveId)
        {
            throw new InvalidOperationException(
                "Archive ID mismatch."
            );
        }


        if (file.PreviousHash !=
            databaseArchive.PreviousHash)
        {
            throw new InvalidOperationException(
                "Previous hash mismatch."
            );
        }


        if (file.SequenceNumber != databaseArchive.SequenceNumber)
        {
            throw new InvalidOperationException(
                "Archive sequence number mismatch."
            );
        }


        if (file.RecordCount !=
            file.Donations.Count)
        {
            throw new InvalidOperationException(
                "Donation count mismatch."
            );
        }


        if (file.RecordCount != databaseArchive.RecordCount)
        {
            throw new InvalidOperationException(
                "Database archive count mismatch."
            );
        }


        var orderedDonations =
            file.Donations
                .OrderBy(x => x.Id)
                .ToList();


        if (orderedDonations.Select(x => x.Id).Distinct().Count() !=
            orderedDonations.Count)
        {
            throw new InvalidOperationException(
                "Archive contains duplicate donation IDs."
            );
        }


        if (expectedIds is not null &&
            !orderedDonations.Select(x => x.Id)
                .SequenceEqual(expectedIds.OrderBy(id => id)))
        {
            throw new InvalidOperationException(
                "Archived donation IDs do not match the expected batch IDs."
            );
        }


        var payloadJson =
            JsonSerializer.Serialize(
                orderedDonations,
                HashJsonOptions
            );


        var calculatedPayloadHash =
            Sha256(payloadJson);


        if (!string.Equals(
                calculatedPayloadHash,
                file.PayloadHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Payload hash mismatch. " +
                "Archive data may have been modified."
            );
        }


        var calculatedCurrentHash =
            Sha256(
                $"{file.SequenceNumber}|" +
                $"{file.ArchiveId:N}|" +
                $"{file.PreviousHash}|" +
                $"{calculatedPayloadHash}"
            );


        if (!string.Equals(
                calculatedCurrentHash,
                file.CurrentHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Current archive hash mismatch."
            );
        }


        if (!string.Equals(
                calculatedCurrentHash,
                databaseArchive.CurrentHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Database archive hash mismatch."
            );
        }
    }


    private static string Sha256(string value)
    {
        var bytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(value)
            );

        return Convert
            .ToHexString(bytes)
            .ToLowerInvariant();
    }
}
