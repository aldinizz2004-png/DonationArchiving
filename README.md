# DonationArchiving

DonationArchiving is a small C#/.NET console application with three learning goals:

1. **Dapper** performs all PostgreSQL operations, including inserts, archive batch creation, `DELETE ... RETURNING`, and post-delete verification.
2. **Amazon S3 / local simulation** stores archive JSON through a shared storage abstraction. `LocalArchiveStorage` is the default; `S3ArchiveStorage` is optional.
3. **Cron Job** runs the non-interactive archive command automatically every five minutes in Linux/WSL.

The archive process dynamically selects donations older than seven days. It keeps archive batches, the `PreviousHash`/`CurrentHash` chain, read-after-write verification, exact archived/deleted ID comparison, history, and safe retry behavior. SQL rows are deleted only after the stored JSON has been read back and verified.

## Configuration

The application reads .NET user secrets and environment variables. Configure the PostgreSQL connection with:

```bash
export ConnectionStrings__DefaultConnection='Host=...;Database=...;Username=...;Password=...;SSL Mode=Require'
```

Local storage is the default:

```bash
export ARCHIVE_STORAGE=local
```

`LocalArchiveStorage` is currently used as the tested storage simulation because AWS account setup required billing/card verification. It writes beneath `LocalArchive/` and remains fully supported.

For a Cron process, put configuration in an untracked project-root `.env` file. Use `KEY=value` lines; do not commit this file:

```dotenv
ConnectionStrings__DefaultConnection='Host=...;Database=...;Username=...;Password=...;SSL Mode=Require'
ARCHIVE_STORAGE=local
```

Restrict the file after creating it:

```bash
chmod 600 .env
```

Initialize PostgreSQL with [Database/schema.sql](Database/schema.sql). The optional [Database/seed.sql](Database/seed.sql) contains both old and recent sample donations.

## Build and commands

```bash
dotnet build
dotnet run -- archive
dotnet run -- verify
```

`archive` and `verify` do not prompt for input. An unsuccessful `archive` command exits non-zero so Cron records a real failure.

## Cron Job

The Cron schedule is:

```cron
*/5 * * * *
```

The installer publishes the Release application once, then installs a user crontab entry that calls [scripts/run-archive.sh](scripts/run-archive.sh). Re-running the installer replaces the marked entry instead of creating duplicates. The runner resolves the project directory from its own location, loads `.env`, prevents interactive input, and appends timestamps, command output, errors, and the exit code to `logs/archive-cron.log`.

### WSL Ubuntu setup

Open Ubuntu in WSL. If `dotnet --version` is unavailable, install the .NET 10 SDK from Microsoft's Ubuntu package feed. For Ubuntu 24.04:

```bash
sudo apt-get update
sudo apt-get install -y dotnet-sdk-10.0
```

If that package is unavailable on your Ubuntu release, follow Microsoft's package-feed setup for that release, then repeat the install command. Confirm the SDK before continuing:

```bash
dotnet --version
```

Move to this repository using its WSL path and create `.env` as described above:

```bash
cd "/mnt/c/Users/Asus i7/Desktop/DonationArchiving"
chmod +x scripts/run-archive.sh scripts/install-cron.sh
./scripts/install-cron.sh
```

Enable and start Cron in WSL Ubuntu:

```bash
sudo systemctl enable --now cron
```

On a WSL installation that is not using systemd, use:

```bash
sudo service cron start
sudo service cron status
```

Confirm the installed entry:

```bash
crontab -l
```

It should contain a line beginning with `*/5 * * * *` and ending with `# DonationArchiving automatic archive`.

Follow the automatic-run log live:

```bash
tail -f logs/archive-cron.log
```

After changing application code, rerun `./scripts/install-cron.sh` so the published copy is refreshed.

### End-to-end automatic test

1. Apply `Database/schema.sql` and configure `.env` with the PostgreSQL connection.
2. Insert one old and one recent donation:

   ```sql
   INSERT INTO donations (username, donation_money, donation_date)
   VALUES
       ('Cron old test', 10.00, NOW() - INTERVAL '8 days'),
       ('Cron recent test', 20.00, NOW());
   ```

3. Confirm both exist before Cron runs:

   ```sql
   SELECT id, username, donation_date, archive_batch_id
   FROM donations
   WHERE username IN ('Cron old test', 'Cron recent test')
   ORDER BY id;
   ```

4. Do **not** select console option 4 and do not run `archive` manually. Wait up to five minutes for the next Cron tick.
5. Confirm an automatic execution and successful exit:

   ```bash
   tail -n 100 logs/archive-cron.log
   ```

6. Confirm a JSON archive was created:

   ```bash
   find LocalArchive/archives/donations -type f -name '*.json' -printf '%TY-%Tm-%Td %TH:%TM:%TS %p\n' | sort
   ```

7. Query PostgreSQL again. `Cron old test` must be gone and `Cron recent test` must remain.
8. Optionally validate the complete hash chain:

   ```bash
   dotnet publish/DonationArchiving.dll verify
   ```

If upload/write, read-back, hash, count, or ID verification fails, the SQL deletion transaction is not completed and the donations remain available for a safe retry.

## Optional Amazon S3 storage

S3 support is a second implementation; it does not replace local storage. Set these only through environment variables, `.env`, or user secrets:

```dotenv
ARCHIVE_STORAGE=s3
AWS_REGION=us-east-1
AWS_BUCKET_NAME=your-private-archive-bucket
AWS_ACCESS_KEY_ID=...
AWS_SECRET_ACCESS_KEY=...
```

The configured AWS identity needs `s3:PutObject` and `s3:GetObject` permission on the archive prefix. The application uploads the JSON, confirms the object exists, downloads it, and runs the same hash and exact-ID verification before allowing SQL deletion. An S3 failure therefore leaves the donation rows untouched.

Do not commit AWS credentials. For production, prefer an IAM role or another short-lived AWS credential mechanism supported by the AWS SDK.
