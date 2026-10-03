namespace DonationArchiving.Models;

public class ArchiveRecord
{
    public Guid ArchiveId { get; set; }

    public long SequenceNumber { get; set; }

    public string PreviousHash { get; set; } = string.Empty;

    public string? CurrentHash { get; set; }

    public string ArchivePath { get; set; } = string.Empty;

    public int RecordCount { get; set; }

    public string Status { get; set; } = "Pending";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? LastError { get; set; }
}


public class ArchiveDonation
{
    public long Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public decimal DonationMoney { get; set; }

    public DateTimeOffset DonationDate { get; set; }
}


public class ArchiveFile
{
    public Guid ArchiveId { get; set; }

    public long SequenceNumber { get; set; }

    public string PreviousHash { get; set; } = string.Empty;

    public string PayloadHash { get; set; } = string.Empty;

    public string CurrentHash { get; set; } = string.Empty;

    public int RecordCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<ArchiveDonation> Donations { get; set; } = [];
}
