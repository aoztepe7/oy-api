namespace Oy.Api.Domain;

public class Enrollment : Entity
{
    public Guid ProgramId { get; set; }
    public TrainingProgram Program { get; set; } = null!;
    public required string ChildName { get; set; }
    public DateOnly? ChildBirthDate { get; set; }
    public required string ParentName { get; set; }
    public string ParentPhone { get; set; } = "";
    public string ParentEmail { get; set; } = "";
    public decimal Fee { get; set; }
    public PricePeriod FeePeriod { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public EnrollmentStatus Status { get; set; }
    public string Note { get; set; } = "";
    /// <summary>Answers to the program's custom pre-registration questions.</summary>
    public List<EnrollmentAnswer> Answers { get; set; } = [];

    public List<Payment> Payments { get; set; } = [];

    public bool CountsAsRevenue => Status is EnrollmentStatus.Active or EnrollmentStatus.Completed;
}

public class Payment : Entity
{
    public Guid EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public DateTime PaidAt { get; set; }
    public string Note { get; set; } = "";
    public Invoice? Invoice { get; set; }
}

public class Invoice : Entity
{
    public Guid PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;
    public string Provider { get; set; } = "";
    public InvoiceStatus Status { get; set; }
    public string Number { get; set; } = "";
    /// <summary>ETTN / UUID returned by the e-invoice integrator.</summary>
    public string ExternalId { get; set; } = "";
    public string Error { get; set; } = "";
    public int Attempts { get; set; }
    public DateTime? SentAt { get; set; }
}
