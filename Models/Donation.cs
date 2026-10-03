namespace DonationArchiving.Models;

public class Donation
{
    public long Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public decimal DonationMoney { get; set; }

    public DateTimeOffset DonationDate { get; set; }

    public Guid? ArchiveBatchId { get; set; }
}