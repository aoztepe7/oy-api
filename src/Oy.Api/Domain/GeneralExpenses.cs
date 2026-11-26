namespace Oy.Api.Domain;

/// <summary>
/// An overhead expense that belongs to no program or event (rent, cleaning, office supplies...). Any staff member can
/// enter one; the author's name is kept as a snapshot so it survives user deletion.
/// </summary>
public class GeneralExpense : Entity
{
    public DateOnly Date { get; set; }
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public Guid? AuthorUserId { get; set; }
    public string AuthorName { get; set; } = "";
}
