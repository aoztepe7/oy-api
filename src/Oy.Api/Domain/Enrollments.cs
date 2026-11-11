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

    /// <summary>Invoice recipient; null = not given (invoices go to the parent's name).</summary>
    public BillingInfo? Billing { get; set; }

    /// <summary>Registration form link sent to the parent (esas kayıt formu).</summary>
    public RegistrationRequest? Registration { get; set; }

    public List<Payment> Payments { get; set; } = [];
    /// <summary>Single instalments with a manual amount or due date.</summary>
    public List<DueAdjustment> DueAdjustments { get; set; } = [];

    public bool CountsAsRevenue => Status is EnrollmentStatus.Active or EnrollmentStatus.Completed;
}

public class Payment : Entity
{
    public Guid EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    public decimal Amount { get; set; }
    /// <summary>The instalment paid: 1st of the month (monthly fee) or of the start month (one-time fee); null = unassigned.</summary>
    public DateOnly? Period { get; set; }
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
    /// <summary>Invoice date (manual invoices: as entered).</summary>
    public DateOnly? IssuedOn { get; set; }
    /// <summary>Amounts the invoice was made with (VAT-inclusive total = net + VAT).</summary>
    public decimal VatRate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
}
