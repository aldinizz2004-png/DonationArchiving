using DonationArchiving.Models;
using DonationArchiving.Repositories;
using DonationArchiving.Services;
using DonationArchiving.Storage;
using Amazon;
using Amazon.S3;
using Microsoft.Extensions.Configuration;


var configuration =
    new ConfigurationBuilder()
        .AddUserSecrets<Program>()
        .AddEnvironmentVariables()
        .Build();


var connectionString =
    configuration.GetConnectionString(
        "DefaultConnection"
    );


if (string.IsNullOrWhiteSpace(
        connectionString))
{
    Console.Error.WriteLine(
        "Database connection string not configured."
    );

    Environment.ExitCode = 1;
    return;
}


var repository =
    new DonationRepository(
        connectionString
    );


var archiveRoot = Path.Combine(
    Directory.GetCurrentDirectory(),
    "LocalArchive"
);

var simulateWriteFailure = bool.TryParse(
    configuration["Archive:SimulateWriteFailure"],
    out var shouldFail
) && shouldFail;

IArchiveStorage storage;

var storageType = configuration["ARCHIVE_STORAGE"]?.Trim().ToLowerInvariant() ?? "local";

if (storageType == "s3")
{
    var regionName = configuration["AWS_REGION"];
    var bucketName = configuration["AWS_BUCKET_NAME"];

    if (string.IsNullOrWhiteSpace(regionName) || string.IsNullOrWhiteSpace(bucketName))
    {
        Console.Error.WriteLine(
            "AWS_REGION and AWS_BUCKET_NAME are required when ARCHIVE_STORAGE=s3."
        );
        Environment.ExitCode = 1;
        return;
    }

    var s3Client = new AmazonS3Client(RegionEndpoint.GetBySystemName(regionName));
    storage = new S3ArchiveStorage(s3Client, bucketName);
}
else if (storageType == "local")
{
    storage = new LocalArchiveStorage(
        archiveRoot,
        simulateWriteFailure
    );
}
else
{
    Console.Error.WriteLine("ARCHIVE_STORAGE must be either 'local' or 's3'.");
    Environment.ExitCode = 1;
    return;
}

var archivingService = new ArchivingService(
    repository,
    storage
);


// ===========================================================
// CRON COMMAND
// ===========================================================

if (args.Length > 0)
{
    var command =
        args[0].ToLowerInvariant();


    if (command == "archive")
    {
        var succeeded = await archivingService
            .ArchiveOldDonationsAsync();

        if (!succeeded)
        {
            Environment.ExitCode = 1;
        }

        return;
    }


    if (command == "verify")
    {
        await archivingService
            .VerifyArchiveChainAsync();

        return;
    }


    Console.Error.WriteLine($"Unknown command: {args[0]}");
    Environment.ExitCode = 1;
    return;
}


// ===========================================================
// INTERACTIVE APPLICATION
// ===========================================================

while (true)
{
    Console.WriteLine();
    Console.WriteLine("==============================");
    Console.WriteLine(" Donation Archiving System");
    Console.WriteLine("==============================");
    Console.WriteLine("1. Insert Donation");
    Console.WriteLine("2. Import Donations");
    Console.WriteLine("3. Display Donations");
    Console.WriteLine("4. Run Archive");
    Console.WriteLine("5. Verify Archive Chain");
    Console.WriteLine("0. Exit");
    Console.WriteLine();

    Console.Write("Choose: ");

    var option =
        Console.ReadLine();


    try
    {
        switch (option)
        {
            case "1":
                await InsertDonation();
                break;


            case "2":
                await ImportDonations();
                break;


            case "3":
                await DisplayDonations();
                break;


            case "4":

                await archivingService
                    .ArchiveOldDonationsAsync();

                break;


            case "5":

                await archivingService
                    .VerifyArchiveChainAsync();

                break;


            case "0":

                return;


            default:

                Console.WriteLine(
                    "Invalid option."
                );

                break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"Error: {ex.Message}"
        );
    }
}


// ===========================================================
// INSERT
// ===========================================================

async Task InsertDonation()
{
    Console.Write("Username: ");

    var username =
        Console.ReadLine() ?? "";


    Console.Write("Donation amount: ");

    if (!decimal.TryParse(
            Console.ReadLine(),
            out var amount))
    {
        Console.WriteLine(
            "Invalid amount."
        );

        return;
    }


    Console.Write(
        "Donation date (yyyy-MM-dd): "
    );

    if (!DateTimeOffset.TryParse(
            Console.ReadLine(),
            out var date))
    {
        Console.WriteLine(
            "Invalid date."
        );

        return;
    }


    var donation =
        new Donation
        {
            Username = username.Trim(),

            DonationMoney = amount,

            DonationDate = date.ToUniversalTime()
        };


    var id =
        await repository
            .InsertDonationAsync(
                donation
            );


    Console.WriteLine(
        $"Donation inserted. ID = {id}"
    );
}


// ===========================================================
// IMPORT
// ===========================================================

async Task ImportDonations()
{
    Console.Write(
        "How many donations? "
    );


    if (!int.TryParse(
            Console.ReadLine(),
            out var count)
        || count <= 0)
    {
        Console.WriteLine(
            "Invalid count."
        );

        return;
    }


    var donations =
        new List<Donation>();


    for (var i = 0; i < count; i++)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"Donation #{i + 1}"
        );


        Console.Write("Username: ");

        var username =
            Console.ReadLine() ?? "";


        Console.Write("Amount: ");

        if (!decimal.TryParse(
                Console.ReadLine(),
                out var amount))
        {
            throw new ArgumentException(
                "Invalid amount."
            );
        }


        Console.Write(
            "Date (yyyy-MM-dd): "
        );

        if (!DateTimeOffset.TryParse(
                Console.ReadLine(),
                out var date))
        {
            throw new ArgumentException(
                "Invalid date."
            );
        }


        donations.Add(
            new Donation
            {
                Username =
                    username.Trim(),

                DonationMoney =
                    amount,

                DonationDate =
                    date.ToUniversalTime()
            }
        );
    }


    var inserted =
        await repository
            .ImportDonationsAsync(
                donations
            );


    Console.WriteLine(
        $"{inserted} donations imported."
    );
}


// ===========================================================
// DISPLAY
// ===========================================================

async Task DisplayDonations()
{
    var donations =
        await repository
            .GetDonationsAsync();


    Console.WriteLine();

    Console.WriteLine(
        "ID\tUsername\tAmount\tDate"
    );

    Console.WriteLine(
        "------------------------------------------------------"
    );


    foreach (var donation in donations)
    {
        Console.WriteLine(
            $"{donation.Id}\t" +
            $"{donation.Username}\t" +
            $"{donation.DonationMoney:F2}\t" +
            $"{donation.DonationDate:yyyy-MM-dd}"
        );
    }


    Console.WriteLine();

    Console.WriteLine(
        $"Active donations: {donations.Count}"
    );
}
