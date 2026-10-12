namespace Glass.Data.Models;

// Represents the assignment of a character to a numbered slot within a character set.
public class SlotAssignment
{
    public int SlotNumber { get; set; }
    public DatabaseId DatabaseId { get; set; }
}