namespace Oy.Api.Domain;

public class Staff : Entity
{
    public required string FullName { get; set; }
    public string Title { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Bio { get; set; } = "";
    public string PhotoUrl { get; set; } = "";
    public bool ShowOnWebsite { get; set; }
    public bool IsActive { get; set; } = true;

    public EmploymentType EmploymentType { get; set; }
    public decimal MonthlyNetSalary { get; set; }
    public decimal MonthlySgkPremium { get; set; }
    public decimal MonthlyTaxAndOther { get; set; }
    public decimal PerLessonFee { get; set; }

    public List<StaffAssignment> Assignments { get; set; } = [];

    public decimal MonthlyFixedCost =>
        EmploymentType == EmploymentType.PerLesson ? 0 : MonthlyNetSalary + MonthlySgkPremium + MonthlyTaxAndOther;

    public decimal EffectiveLessonFee(decimal? overrideFee) =>
        EmploymentType == EmploymentType.Salaried ? 0 : overrideFee ?? PerLessonFee;
}

/// <summary>Staff member assigned to a program for a period.</summary>
public class StaffAssignment : Entity
{
    public Guid ProgramId { get; set; }
    public TrainingProgram Program { get; set; } = null!;
    public Guid StaffId { get; set; }
    public Staff Staff { get; set; } = null!;
    public AssignmentRole Role { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    /// <summary>Share (0-100) of the staff's monthly fixed cost charged to this program.</summary>
    public decimal SalaryAllocationPercent { get; set; } = 100;
    public decimal? PerLessonFeeOverride { get; set; }
}
