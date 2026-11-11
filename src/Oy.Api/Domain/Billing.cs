using System.Globalization;

namespace Oy.Api.Domain;

/// <summary>
/// Fixed values of invoices and payment tracking, set in the panel (single row). Fees are VAT-inclusive:
/// an invoice splits the paid amount into net price + VAT with <see cref="VatRate"/>.
/// </summary>
public class BillingSettings : Entity
{
    /// <summary>KDV oranı (%).</summary>
    public decimal VatRate { get; set; } = 10;
    /// <summary>Monthly fees are due on this day of the month (1–28; later days fall back to the month's last day).</summary>
    public int MonthlyDueDay { get; set; } = 5;
    /// <summary>Payments due within this many days show as "yaklaşan" and count in the warnings.</summary>
    public int ReminderDays { get; set; } = 7;
    /// <summary>Invoice line text; placeholders {program}, {cocuk}, {veli}, {donem}.</summary>
    public string ItemName { get; set; } = "{program} eğitim hizmeti - {cocuk} {donem}";
    public string Unit { get; set; } = "Adet";
    /// <summary>Optional note printed on the invoice; same placeholders.</summary>
    public string Note { get; set; } = "";
    /// <summary>TCKN used for individuals without a known one (e-Arşiv rule: 11111111111).</summary>
    public string DefaultTaxId { get; set; } = "11111111111";
}

public enum BillingKind { Individual, Corporate }

/// <summary>Who the invoices of an enrollment are made out to (filled in the panel; later by the parent).</summary>
public class BillingInfo
{
    public BillingKind Kind { get; set; }
    /// <summary>Ad soyad (individual) or ünvan (company).</summary>
    public string Name { get; set; } = "";
    /// <summary>TCKN (11 digits) or VKN (10 digits).</summary>
    public string TaxId { get; set; } = "";
    public string TaxOffice { get; set; } = "";
    public string Address { get; set; } = "";
    public string District { get; set; } = "";
    public string City { get; set; } = "";
    public string Email { get; set; } = "";
}

public enum ObligationCategory { Sgk, Salary, Tax, Rent, Other }

/// <summary>A monthly payment the school makes (SGK premiums, salaries, rent …), tracked month by month.</summary>
public class Obligation : Entity
{
    public required string Title { get; set; }
    public ObligationCategory Category { get; set; }
    /// <summary>Expected amount; null when it changes every month (entered when paid).</summary>
    public decimal? Amount { get; set; }
    /// <summary>Due day of the month (1–31; clamped to the month's last day).</summary>
    public int DayOfMonth { get; set; }
    /// <summary>First month (1st of the month).</summary>
    public DateOnly StartMonth { get; set; }
    /// <summary>Last month (1st of the month); null = open-ended.</summary>
    public DateOnly? EndMonth { get; set; }
    public bool IsActive { get; set; } = true;
    public string Note { get; set; } = "";
    /// <summary>Optional staff member (salary of …).</summary>
    public Guid? StaffId { get; set; }
    public Staff? Staff { get; set; }
    public List<ObligationPayment> Payments { get; set; } = [];
}

/// <summary>An obligation marked paid for one month.</summary>
public class ObligationPayment : Entity
{
    public Guid ObligationId { get; set; }
    public Obligation Obligation { get; set; } = null!;
    /// <summary>1st of the month paid for.</summary>
    public DateOnly Month { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaidOn { get; set; }
    public string Note { get; set; } = "";
}

/// <summary>
/// A manual change to one instalment of an enrollment: a different amount (discount, part of a month) and/or due date.
/// The rest of the instalments keep the enrollment fee.
/// </summary>
public class DueAdjustment : Entity
{
    public Guid EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    /// <summary>1st of the month of the instalment.</summary>
    public DateOnly Period { get; set; }
    /// <summary>null = the enrollment fee.</summary>
    public decimal? Amount { get; set; }
    /// <summary>null = the usual due date.</summary>
    public DateOnly? DueDate { get; set; }
    public string Note { get; set; } = "";
}

public enum DueStatus { Paid, Partial, Unpaid }

/// <summary>
/// One fee instalment of an enrollment: a month of a monthly fee, or the single fee.
/// <paramref name="StandardAmount"/> is the enrollment fee; <paramref name="Amount"/> differs when adjusted for this month.
/// </summary>
public record Due(DateOnly Period, DateOnly DueDate, decimal Amount, decimal StandardAmount = 0, bool Adjusted = false, string Note = "");

/// <summary>A due with what was paid towards it.</summary>
public record DueState(Due Due, decimal Paid, IReadOnlyList<Payment> Payments)
{
    public decimal Remaining => Math.Max(0, Due.Amount - Paid);
    public DueStatus Status => Paid >= Due.Amount ? DueStatus.Paid : Paid > 0 ? DueStatus.Partial : DueStatus.Unpaid;
}

/// <summary>Invoice recipient as it goes on the invoice.</summary>
public record InvoiceBuyer(
    BillingKind Kind, string Name, string TaxId, string TaxOffice, string Address, string District, string City, string Email,
    /// <summary>false: no billing info on the enrollment, the parent's name and the default TCKN are used.</summary>
    bool FromBillingInfo);

/// <summary>
/// Everything needed to issue the invoice of a payment: what the e-invoice integration sends, and the template the user
/// copies into GİB e-Arşiv Portal when there is no integration. One line, VAT-inclusive total = paid amount.
/// </summary>
public record InvoiceDraft(
    InvoiceBuyer Buyer, DateOnly IssueDate, string ItemName, decimal Quantity, string Unit,
    /// <summary>Net unit price (KDV hariç).</summary>
    decimal UnitPrice, decimal VatRate, decimal VatAmount, decimal Total, string Note);

public static class Billing
{
    /// <summary>Invoice draft of a payment (Enrollment and its Program loaded).</summary>
    public static InvoiceDraft Draft(Payment p, BillingSettings s, DateOnly issueDate)
    {
        var e = p.Enrollment;
        var info = e.Billing;
        var buyer = info is not null && info.Name.Trim().Length > 0
            ? new InvoiceBuyer(info.Kind, info.Name, info.TaxId.Length > 0 ? info.TaxId : s.DefaultTaxId, info.TaxOffice, info.Address,
                info.District, info.City, info.Email.Length > 0 ? info.Email : e.ParentEmail, true)
            : new InvoiceBuyer(BillingKind.Individual, e.ParentName, s.DefaultTaxId, "", "", "", "", e.ParentEmail, false);
        var period = p.Period is { } month && e.FeePeriod == PricePeriod.Monthly ? $"({MonthName(month)})" : "";
        var (net, vat) = SplitVat(p.Amount, s.VatRate);
        return new InvoiceDraft(
            buyer, issueDate,
            Fill(s.ItemName, e.Program.Title, e.ChildName, e.ParentName, period),
            1, s.Unit, net, s.VatRate, vat, p.Amount,
            Fill(s.Note, e.Program.Title, e.ChildName, e.ParentName, period));
    }

    private static readonly CultureInfo Tr = new("tr-TR");

    public static DateOnly MonthStart(DateOnly d) => new(d.Year, d.Month, 1);

    /// <summary>The given day of the month, or the month's last day when it is shorter.</summary>
    public static DateOnly DayOf(DateOnly month, int day) =>
        new(month.Year, month.Month, Math.Clamp(day, 1, DateTime.DaysInMonth(month.Year, month.Month)));

    /// <summary>
    /// Instalments of an enrollment. Monthly: one per month from the start month to the end (enrollment end or program end),
    /// due on <paramref name="dueDay"/> (not before the start date). One-time: a single fee due on the start date.
    /// Enrollments without a fee (siblings in a family package) have none.
    /// </summary>
    public static List<Due> DuesOf(Enrollment e, DateOnly programEnd, int dueDay)
    {
        var dues = new List<Due>();
        if (e.FeePeriod == PricePeriod.OneTime) dues.Add(new Due(MonthStart(e.StartDate), e.StartDate, e.Fee, e.Fee));
        else
        {
            var last = MonthStart(e.EndDate ?? programEnd);
            for (var m = MonthStart(e.StartDate); m <= last && dues.Count < 120; m = m.AddMonths(1))
            {
                var date = DayOf(m, dueDay);
                dues.Add(new Due(m, date < e.StartDate ? e.StartDate : date, e.Fee, e.Fee));
            }
        }

        // Manual changes of single instalments.
        var adjustments = e.DueAdjustments.ToDictionary(a => a.Period);
        dues = dues.Select(d => adjustments.TryGetValue(d.Period, out var a)
            ? d with { Amount = a.Amount ?? d.Amount, DueDate = a.DueDate ?? d.DueDate, Adjusted = true, Note = a.Note }
            : d).ToList();
        // No fee (siblings in a family package) and no adjusted amount: nothing to pay.
        return dues.Where(d => d.Amount > 0 || d.Adjusted).ToList();
    }

    /// <summary>
    /// Suggested fee of a month the enrollment starts or ends in the middle of: the share of the month's days it covers
    /// (rounded to whole lira). The full fee for whole months.
    /// </summary>
    public static decimal ProratedFee(decimal fee, DateOnly period, DateOnly start, DateOnly? end)
    {
        var days = DateTime.DaysInMonth(period.Year, period.Month);
        var first = start > period ? start.Day : 1;
        var lastDay = end is { } e && MonthStart(e) == period ? e.Day : days;
        var covered = Math.Max(0, lastDay - first + 1);
        return covered >= days ? fee : Math.Round(fee * covered / days, 0, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Matches payments to dues: a payment with a period pays that month; payments without one (or for a month
    /// that has no due) fill the oldest unpaid dues first.
    /// </summary>
    public static List<DueState> Allocate(IReadOnlyList<Due> dues, IEnumerable<Payment> payments)
    {
        var periods = dues.Select(d => d.Period).ToHashSet();
        var byPeriod = new Dictionary<DateOnly, List<Payment>>();
        var pool = 0m;
        var poolPayments = new List<Payment>();
        foreach (var p in payments.OrderBy(p => p.PaidAt))
        {
            if (p.Period is { } period && periods.Contains(period))
                (byPeriod.TryGetValue(period, out var list) ? list : byPeriod[period] = []).Add(p);
            else
            {
                pool += p.Amount;
                poolPayments.Add(p);
            }
        }

        var result = new List<DueState>();
        foreach (var due in dues.OrderBy(d => d.Period))
        {
            var own = byPeriod.GetValueOrDefault(due.Period) ?? [];
            var paid = own.Sum(p => p.Amount);
            var fromPool = Math.Min(pool, Math.Max(0, due.Amount - paid));
            pool -= fromPool;
            result.Add(new DueState(due, paid + fromPool, fromPool > 0 ? [.. own, .. poolPayments] : own));
        }
        return result;
    }

    /// <summary>"Kasım 2026".</summary>
    public static string MonthName(DateOnly month) => month.ToString("MMMM yyyy", Tr);

    /// <summary>
    /// Splits a VAT-inclusive amount into net price + VAT. GİB computes the VAT from the net price (rounded to kuruş), so the
    /// net is chosen so that net + VAT(net) gives back exactly the gross when such a net exists.
    /// </summary>
    public static (decimal Net, decimal Vat) SplitVat(decimal gross, decimal ratePercent)
    {
        var rate = ratePercent / 100m;
        var start = Math.Round(gross / (1 + rate), 2, MidpointRounding.AwayFromZero);
        foreach (var delta in new[] { 0m, -0.01m, 0.01m, -0.02m, 0.02m })
        {
            var net = start + delta;
            var vat = Math.Round(net * rate, 2, MidpointRounding.AwayFromZero);
            if (net + vat == gross) return (net, vat);
        }
        return (start, gross - start);
    }

    /// <summary>Fills {program}, {cocuk}, {veli}, {donem} and tidies the spaces left by empty ones.</summary>
    public static string Fill(string template, string program, string child, string parent, string period) =>
        string.Join(' ', template
            .Replace("{program}", program).Replace("{cocuk}", child).Replace("{veli}", parent).Replace("{donem}", period)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Replace("( )", "").Replace("()", "").Trim(' ', '-');

    public static bool IsTaxId(string value) =>
        (value.Length == 10 && value.All(char.IsAsciiDigit)) || EnrollmentForms.IsNationalId(value) || value == "11111111111";
}
